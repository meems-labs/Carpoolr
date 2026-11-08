using Carpoolr.Api.Data;
using Carpoolr.Api.Features.Bookings;
using Carpoolr.Api.Features.Profiles;
using Carpoolr.Api.Features.Trips;
using Carpoolr.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Carpoolr.Api.Features.RecurringTrips;

public sealed record CreateSeriesCommand(Guid RequestId, string Origin, string Destination, int SeatsTotal,
    decimal DistanceKm, DateOnly StartDate, DateOnly EndDate, int[] Weekdays, TimeOnly LocalDepartureTime);
public sealed record SeriesPage(IReadOnlyList<RecurringSeries> Series, int? NextOffset);

public sealed class RecurringTripApplicationService(
    CarpoolrDbContext database, ProfileApplicationService profiles, TimeProvider clock)
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public async Task<RecurringSeries> CreateAsync(CreateSeriesCommand command, UserIdentity actor, CancellationToken cancellationToken)
    {
        var origin = ApplicationRules.Text(command.Origin, "origin", 500);
        var destination = ApplicationRules.Text(command.Destination, "destination", 500);
        if (string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase) || command.SeatsTotal is < 1 or > 8 ||
            command.DistanceKm is <= 0 or > 10000 || decimal.Round(command.DistanceKm, 4) != command.DistanceKm ||
            command.RequestId == Guid.Empty || command.Weekdays.Length is < 1 or > 7 ||
            command.Weekdays.Any(day => day is < 1 or > 7) || command.Weekdays.Distinct().Count() != command.Weekdays.Length)
            throw new TripValidationException("Route, capacity, distance, request ID or selected weekdays is invalid.");
        var days = command.Weekdays.Order().ToArray();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var requestKey = $"series:{actor.Issuer}:{actor.Subject}:{command.RequestId:D}";
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({requestKey}, 0))", cancellationToken);
        var existing = await database.Series.AsNoTracking().SingleOrDefaultAsync(series =>
            series.OwnerIssuer == actor.Issuer && series.OwnerSubject == actor.Subject && series.RequestId == command.RequestId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.Origin != origin || existing.Destination != destination || existing.SeatsTotal != command.SeatsTotal ||
                existing.DistanceKm != command.DistanceKm || existing.StartDate != command.StartDate || existing.EndDate != command.EndDate ||
                existing.LocalDepartureTime != command.LocalDepartureTime || !existing.Weekdays.SequenceEqual(days))
                throw new TripConflictException();
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Berlin).DateTime);
        if (command.StartDate < today || command.EndDate < command.StartDate || command.EndDate > today.AddDays(366))
            throw new TripValidationException("Series dates must be ordered, start today or later, and end within 366 days.");
        var profile = await profiles.RequireAsync(actor, cancellationToken);
        if (profile.Vehicle is null)
            throw new TripPreconditionException("Save a vehicle before offering recurring trips.");
        if (command.SeatsTotal > profile.Vehicle.PassengerSeats)
            throw new TripValidationException("Offered passenger seats exceed the vehicle capacity.");
        var series = new RecurringSeries
        {
            Id = Guid.NewGuid(),
            RequestId = command.RequestId,
            OwnerIssuer = actor.Issuer,
            OwnerSubject = actor.Subject,
            Origin = origin,
            Destination = destination,
            SeatsTotal = command.SeatsTotal,
            DistanceKm = command.DistanceKm,
            StartDate = command.StartDate,
            EndDate = command.EndDate,
            LocalDepartureTime = command.LocalDepartureTime,
            Weekdays = days,
        };
        var occurrences = new List<Trip>();
        for (var date = command.StartDate; date <= command.EndDate; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var weekday = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            if (!days.Contains(weekday)) continue;
            var local = DateTime.SpecifyKind(date.ToDateTime(command.LocalDepartureTime), DateTimeKind.Unspecified);
            if (Berlin.IsInvalidTime(local) || Berlin.IsAmbiguousTime(local))
                throw new TripValidationException($"The Europe/Berlin departure time on {date:yyyy-MM-dd} is ambiguous or nonexistent.");
            var departure = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Berlin));
            if (departure <= now) continue;
            occurrences.Add(new Trip(Guid.NewGuid(), Guid.NewGuid(), actor.Issuer, actor.Subject,
                origin, destination, departure, command.SeatsTotal, now)
            {
                SeriesId = series.Id,
                DriverProfileId = profile.Id,
                DistanceKm = command.DistanceKm,
                VehicleName = profile.Vehicle.Name,
                EnergyKind = profile.Vehicle.EnergyKind,
                ConsumptionPer100Km = profile.Vehicle.ConsumptionPer100Km,
                UnitPriceEur = profile.Vehicle.UnitPriceEur,
            });
        }
        if (occurrences.Count == 0)
            throw new TripValidationException("The selected dates and weekdays contain no future departures.");
        database.Series.Add(series);
        database.Trips.AddRange(occurrences);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return series;
    }

    public async Task<SeriesPage> ListMineAsync(UserIdentity actor, int offset, int pageSize, CancellationToken cancellationToken)
    {
        offset = ApplicationRules.Offset(offset);
        pageSize = ApplicationRules.PageSize(pageSize);
        var list = await database.Series.AsNoTracking().Where(series =>
                series.OwnerIssuer == actor.Issuer && series.OwnerSubject == actor.Subject)
            .OrderByDescending(series => series.StartDate).ThenBy(series => series.Id)
            .Skip(offset).Take(pageSize + 1).ToListAsync(cancellationToken);
        var more = list.Count > pageSize;
        if (more) list.RemoveAt(list.Count - 1);
        return new SeriesPage(list, more && offset + pageSize <= 10000 ? offset + pageSize : null);
    }

    public async Task<RecurringSeries> CancelAsync(Guid id, UserIdentity actor, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var series = await database.Series.FromSqlInterpolated(
                $"SELECT * FROM \"RecurringSeries\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new TripNotFoundException();
        if (series.OwnerIssuer != actor.Issuer || series.OwnerSubject != actor.Subject) throw new TripPermissionException();
        var now = clock.GetUtcNow();
        var future = await database.Trips.FromSqlInterpolated(
            $"SELECT * FROM \"Trips\" WHERE \"SeriesId\" = {id} AND \"State\" = 1 AND \"DepartureAt\" > {now} ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(cancellationToken);
        foreach (var trip in future) trip.State = TripState.Cancelled;
        var ids = future.Select(trip => trip.Id).ToArray();
        await database.Bookings.Where(booking => ids.Contains(booking.TripId) &&
                (booking.State == BookingState.Pending || booking.State == BookingState.Accepted))
            .ExecuteUpdateAsync(update => update.SetProperty(booking => booking.State, BookingState.Cancelled), cancellationToken);
        series.IsCancelled = true;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return series;
    }
}
