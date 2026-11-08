using System.Text;
using Carpoolr.Api.Data;
using Carpoolr.Api.Features.Bookings;
using Carpoolr.Api.Features.Profiles;
using Carpoolr.Api.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carpoolr.Api.Features.Trips;

/// <summary>
/// Implements trip rules independently of HTTP, gRPC, and generated protobuf types.
/// </summary>
public sealed class TripApplicationService(
    CarpoolrDbContext dbContext, TimeProvider timeProvider, ProfileApplicationService profiles)
{
    public async Task<Trip> CreateAsync(
        CreateTripCommand command,
        UserIdentity owner,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(owner);
        ValidateId(command.RequestId, "request_id");

        var origin = NormalizeLocation(command.Origin, "origin");
        var destination = NormalizeLocation(command.Destination, "destination");
        var departureAt = NormalizeInstant(command.DepartureAt);
        if (command.DistanceKm is <= 0 or > 10000 || decimal.Round(command.DistanceKm, 4) != command.DistanceKm)
            throw new TripValidationException("distance_km must be positive, at most 10000 and have at most four decimals.");

        if (string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase))
        {
            throw new TripValidationException("Origin and destination must be different.");
        }

        if (command.SeatsTotal is < 1 or > 8)
        {
            throw new TripValidationException("seats_total must be between 1 and 8.");
        }

        // Check retries before time-sensitive rules: a successful request remains retryable
        // even if the trip has departed or was cancelled since its original creation.
        var existing = await FindByRequestAsync(command.RequestId, owner, cancellationToken);
        if (existing is not null)
        {
            return MatchExistingRequest(existing, origin, destination, departureAt, command.SeatsTotal, command.DistanceKm);
        }

        var now = NormalizeInstant(timeProvider.GetUtcNow());
        if (departureAt <= now || departureAt > now.AddDays(366))
        {
            throw new TripValidationException("departure_at must be in the future and within 366 days.");
        }

        var profile = await profiles.RequireAsync(owner, cancellationToken);
        if (profile.Vehicle is null)
            throw new TripPreconditionException("Save a vehicle before offering a trip.");
        if (command.SeatsTotal > profile.Vehicle.PassengerSeats)
            throw new TripValidationException("Offered passenger seats exceed the vehicle capacity.");

        var trip = new Trip(
            Guid.NewGuid(),
            command.RequestId,
            owner.Issuer,
            owner.Subject,
            origin,
            destination,
            departureAt,
            command.SeatsTotal,
            now)
        {
            DriverProfileId = profile.Id,
            DistanceKm = command.DistanceKm,
            VehicleName = profile.Vehicle.Name,
            EnergyKind = profile.Vehicle.EnergyKind,
            ConsumptionPer100Km = profile.Vehicle.ConsumptionPer100Km,
            UnitPriceEur = profile.Vehicle.UnitPriceEur,
        };

        dbContext.Trips.Add(trip);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return await GetAsync(trip.Id, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: CarpoolrDbContext.IdempotencyIndexName,
            })
        {
            // Another request can win between the initial lookup and INSERT.
            // Only the specific idempotency constraint is handled as a retry.
            dbContext.Entry(trip).State = EntityState.Detached;
            existing = await FindByRequestAsync(command.RequestId, owner, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return MatchExistingRequest(existing, origin, destination, departureAt, command.SeatsTotal, command.DistanceKm);
        }
    }

    public async Task<Trip> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        ValidateId(id, "trip_id");

        return await DetailQuery()
            .SingleOrDefaultAsync(trip => trip.Id == id, cancellationToken)
            ?? throw new TripNotFoundException();
    }

    public async Task<TripSearchResult> SearchAsync(
        SearchTripsQuery query,
        CancellationToken cancellationToken)
    {
        var from = NormalizeInstant(query.From);
        var until = NormalizeInstant(query.Until);
        if (until <= from || until - from > TimeSpan.FromDays(31))
        {
            throw new TripValidationException("The search interval must be positive and at most 31 days.");
        }

        if (query.Offset is < 0 or > 10_000 || query.PageSize is < 1 or > 100)
        {
            throw new TripValidationException("offset must be 0..10000 and page_size must be 1..100.");
        }

        var origin = query.Origin is null ? null : NormalizeLocation(query.Origin, "origin");
        var destination = query.Destination is null
            ? null
            : NormalizeLocation(query.Destination, "destination");
        var now = NormalizeInstant(timeProvider.GetUtcNow());

        var trips = DetailQuery().Where(trip =>
            trip.State == TripState.Planned
            && trip.DepartureAt > now
            && trip.DepartureAt >= from
            && trip.DepartureAt < until);

        if (origin is not null)
        {
            trips = trips.Where(trip => trip.Origin == origin);
        }

        if (destination is not null)
        {
            trips = trips.Where(trip => trip.Destination == destination);
        }

        var page = await trips.OrderBy(trip => trip.DepartureAt)
            .ThenBy(trip => trip.Id)
            .Skip(query.Offset)
            .Take(query.PageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > query.PageSize;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var nextOffset = query.Offset + query.PageSize;
        return new TripSearchResult(page, hasMore && nextOffset <= 10_000 ? nextOffset : null);
    }

    public async Task<Trip> CancelAsync(
        Guid id,
        UserIdentity actor,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(actor);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var trip = await LockAsync(id, cancellationToken);

        if (!string.Equals(trip.OwnerIssuer, actor.Issuer, StringComparison.Ordinal)
            || !string.Equals(trip.OwnerSubject, actor.Subject, StringComparison.Ordinal))
        {
            throw new TripPermissionException();
        }

        if (trip.State == TripState.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(id, cancellationToken);
        }

        var now = NormalizeInstant(timeProvider.GetUtcNow());
        if (trip.DepartureAt <= now)
        {
            throw new TripPreconditionException("A departed trip cannot be cancelled.");
        }

        if (trip.State != TripState.Planned)
            throw new TripPreconditionException("Only planned trips can be cancelled.");

        // A conditional UPDATE makes concurrent cancellations safe without a read/modify/write race.
        await dbContext.Trips.Where(value =>
                value.Id == id
                && value.OwnerIssuer == actor.Issuer
                && value.OwnerSubject == actor.Subject
                && value.State == TripState.Planned)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.State, TripState.Cancelled),
                cancellationToken);

        await dbContext.Bookings.Where(value => value.TripId == id &&
                (value.State == BookingState.Pending || value.State == BookingState.Accepted))
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.State, BookingState.Cancelled), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    private Task<Trip?> FindByRequestAsync(
        Guid requestId,
        UserIdentity owner,
        CancellationToken cancellationToken)
    {
        return DetailQuery().SingleOrDefaultAsync(trip =>
                trip.OwnerIssuer == owner.Issuer
                && trip.OwnerSubject == owner.Subject
                && trip.RequestId == requestId,
            cancellationToken);
    }

    private static Trip MatchExistingRequest(
        Trip existing,
        string origin,
        string destination,
        DateTimeOffset departureAt,
        int seatsTotal,
        decimal distanceKm)
    {
        if (existing.Origin != origin
            || existing.Destination != destination
            || existing.DepartureAt != departureAt
            || existing.SeatsTotal != seatsTotal
            || existing.DistanceKm != distanceKm)
        {
            throw new TripConflictException();
        }

        return existing;
    }

    public async Task<TripSearchResult> ListMineAsync(
        UserIdentity actor, int offset, int pageSize, CancellationToken cancellationToken)
    {
        offset = ApplicationRules.Offset(offset);
        pageSize = ApplicationRules.PageSize(pageSize);
        var now = NormalizeInstant(timeProvider.GetUtcNow());
        var page = await DetailQuery().Where(trip => trip.OwnerIssuer == actor.Issuer && trip.OwnerSubject == actor.Subject)
            .OrderBy(trip => trip.DepartureAt > now && trip.State == TripState.Planned ? 0 :
                trip.DepartureAt > now ? 1 : 2)
            .ThenBy(trip => trip.DepartureAt > now ? trip.DepartureAt : now)
            .ThenByDescending(trip => trip.DepartureAt)
            .ThenBy(trip => trip.Id)
            .Skip(offset).Take(pageSize + 1).ToListAsync(cancellationToken);
        var more = page.Count > pageSize;
        if (more) page.RemoveAt(page.Count - 1);
        return new TripSearchResult(page, more && offset + pageSize <= 10000 ? offset + pageSize : null);
    }

    public Task<Trip?> FindLockedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Trips.FromSqlInterpolated($"SELECT * FROM \"Trips\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<Trip> LockAsync(Guid id, CancellationToken cancellationToken) =>
        await FindLockedAsync(id, cancellationToken) ?? throw new TripNotFoundException();

    public async Task<IReadOnlyDictionary<Guid, Trip>> GetManyAsync(
        IEnumerable<Guid> identifiers, CancellationToken cancellationToken)
    {
        var ids = identifiers.Distinct().Take(101).ToArray();
        if (ids.Length > 100) throw new TripValidationException("At most 100 trips can be read together.");
        return await DetailQuery().Where(trip => ids.Contains(trip.Id)).ToDictionaryAsync(trip => trip.Id, cancellationToken);
    }

    private IQueryable<Trip> DetailQuery() => dbContext.Trips.AsNoTracking()
        .Include(trip => trip.DriverProfile)
        // Active requests come first, so capacity and route calculations remain complete.
        // Older rejected/withdrawn requests remain available in ListMyBookings.
        .Include(trip => trip.Bookings
            .OrderBy(booking => booking.State == BookingState.Accepted ? 0 : booking.State == BookingState.Pending ? 1 : 2)
            .ThenByDescending(booking => booking.RequestedAt).ThenBy(booking => booking.Id).Take(200))
        .ThenInclude(booking => booking.Profile)
        .AsSplitQuery();

    private static string NormalizeLocation(string? value, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 500 || normalized.Any(char.IsControl))
        {
            throw new TripValidationException($"{fieldName} must contain 1..500 characters without control characters.");
        }

        return normalized;
    }

    private static DateTimeOffset NormalizeInstant(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();

        // PostgreSQL timestamps have microsecond precision. Use that precision before
        // comparing retries so a database round-trip cannot change request equality.
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    private static void ValidateId(Guid id, string fieldName)
    {
        if (id == Guid.Empty)
        {
            throw new TripValidationException($"{fieldName} must be a non-empty UUID.");
        }
    }

    private static void ValidateIdentity(UserIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.Issuer) || Encoding.UTF8.GetByteCount(identity.Issuer) > 512
            || identity.Issuer.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(identity.Subject) || Encoding.UTF8.GetByteCount(identity.Subject) > 255
            || identity.Subject.Any(char.IsControl))
        {
            throw new TripPermissionException();
        }
    }
}
