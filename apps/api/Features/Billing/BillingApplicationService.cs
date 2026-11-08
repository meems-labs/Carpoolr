using Carpoolr.Api.Data;
using Carpoolr.Api.Features.Bookings;
using Carpoolr.Api.Features.Profiles;
using Carpoolr.Api.Features.Trips;
using Carpoolr.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Carpoolr.Api.Features.Billing;

public sealed record CompleteTripCommand(decimal DistanceKm, decimal ConsumptionPer100Km, decimal UnitPriceEur);

public sealed record BillShareDetails(
    Guid ProfileId,
    string DisplayName,
    long AmountCents,
    bool IsDriver,
    bool IsMine,
    PaymentState PaymentState);

public sealed record BillDetails(
    Guid TripId,
    decimal DistanceKm,
    decimal ConsumptionPer100Km,
    decimal UnitPriceEur,
    long TotalCents,
    DateTimeOffset CompletedAt,
    bool IsDriver,
    IReadOnlyList<BillShareDetails> Shares);

/// <summary>
/// Creates an immutable cost allocation, then manages its manual payment acknowledgements.
/// Every mutation takes the same trip row lock as bookings so accepted participants cannot
/// change while the final allocation is being created.
/// </summary>
public sealed class BillingApplicationService(CarpoolrDbContext database, TimeProvider timeProvider)
{
    public async Task<BillDetails> CompleteAsync(
        Guid tripId,
        CompleteTripCommand command,
        UserIdentity actor,
        CancellationToken cancellationToken)
    {
        ValidateId(tripId, "trip_id");
        ValidateDecimal(command.DistanceKm, "distance_km", 10000m);
        ValidateDecimal(command.ConsumptionPer100Km, "consumption_per_100_km", 1000m);
        ValidateDecimal(command.UnitPriceEur, "unit_price_eur", 100m);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var trip = await LockTripAsync(tripId, cancellationToken);
        RequireOwner(trip, actor);

        var existing = await database.Bills.Include(bill => bill.Shares)
            .SingleOrDefaultAsync(bill => bill.TripId == tripId, cancellationToken);
        if (existing is not null)
        {
            if (existing.DistanceKm != command.DistanceKm ||
                existing.ConsumptionPer100Km != command.ConsumptionPer100Km ||
                existing.UnitPriceEur != command.UnitPriceEur)
            {
                throw new TripConflictException("A completed bill cannot be changed.");
            }

            await transaction.CommitAsync(cancellationToken);
            return ToDetails(existing, trip, actor);
        }

        var now = MicrosecondPrecision(timeProvider.GetUtcNow());
        if (trip.State != TripState.Planned || trip.DepartureAt > now)
        {
            throw new TripPreconditionException("Only a planned trip after its departure can be completed.");
        }

        var driver = trip.DriverProfileId is { } profileId
            ? await database.Profiles.AsNoTracking().SingleOrDefaultAsync(
                profile => profile.Id == profileId, cancellationToken)
            : null;
        if (driver is null || driver.Issuer != actor.Issuer || driver.Subject != actor.Subject)
        {
            throw new TripPreconditionException("The trip needs its driver's saved profile before completion.");
        }

        var passengers = await database.Bookings.AsNoTracking()
            .Where(booking => booking.TripId == tripId && booking.State == BookingState.Accepted)
            .OrderBy(booking => booking.ProfileId)
            .Select(booking => booking.Profile)
            .ToListAsync(cancellationToken);
        if (passengers.Count > trip.SeatsTotal || passengers.Any(profile => profile.Id == driver.Id) ||
            passengers.Select(profile => profile.Id).Distinct().Count() != passengers.Count)
        {
            throw new TripPreconditionException("The trip's accepted participants are inconsistent.");
        }

        // Decimal arithmetic preserves the entered precision. Round the total once, then
        // distribute whole cents so the shares always add up exactly to that total.
        var totalEur = command.DistanceKm * command.ConsumptionPer100Km / 100m * command.UnitPriceEur;
        var totalCents = checked((long)decimal.Round(totalEur * 100m, 0, MidpointRounding.AwayFromZero));
        var participants = new List<UserProfile> { driver };
        participants.AddRange(passengers);
        var baseShare = totalCents / participants.Count;
        var remainder = totalCents % participants.Count;
        var bill = new TripBill
        {
            TripId = tripId,
            Trip = trip,
            DistanceKm = command.DistanceKm,
            ConsumptionPer100Km = command.ConsumptionPer100Km,
            UnitPriceEur = command.UnitPriceEur,
            TotalCents = totalCents,
            CompletedAt = now,
        };

        // The driver receives the first remainder cent; passengers follow by stable profile ID.
        for (var index = 0; index < participants.Count; index++)
        {
            var participant = participants[index];
            bill.Shares.Add(new BillShare
            {
                TripId = tripId,
                Bill = bill,
                ProfileId = participant.Id,
                Issuer = participant.Issuer,
                Subject = participant.Subject,
                DisplayName = participant.DisplayName,
                AmountCents = baseShare + (index < remainder ? 1 : 0),
                IsDriver = index == 0,
                PaymentState = index == 0 ? PaymentState.DriverShare : PaymentState.Unpaid,
            });
        }

        trip.State = TripState.Completed;
        database.Bills.Add(bill);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDetails(bill, trip, actor);
    }

    public async Task<BillDetails> GetAsync(Guid tripId, UserIdentity actor, CancellationToken cancellationToken)
    {
        ValidateId(tripId, "trip_id");
        var bill = await database.Bills.AsNoTracking().Include(value => value.Trip).Include(value => value.Shares)
            .SingleOrDefaultAsync(value => value.TripId == tripId, cancellationToken)
            ?? throw new TripNotFoundException("The trip has no completed bill.");

        return ToDetails(bill, bill.Trip, actor);
    }

    public async Task<BillDetails> ReportPaymentAsync(
        Guid tripId,
        Guid requestId,
        UserIdentity actor,
        CancellationToken cancellationToken)
    {
        ValidateId(tripId, "trip_id");
        ValidateId(requestId, "request_id");
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var trip = await LockTripAsync(tripId, cancellationToken);
        var bill = await LoadBillAsync(tripId, cancellationToken);
        var share = bill.Shares.SingleOrDefault(value => IsMine(value, actor));
        if (share is null || share.IsDriver)
        {
            throw new TripPermissionException("Only a passenger can report their own payment.");
        }

        // A retry must preserve the result even when the driver has already processed it.
        if (share.LastReportId == requestId)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToDetails(bill, trip, actor);
        }

        if (share.PaymentState is not (PaymentState.Unpaid or PaymentState.Rejected))
        {
            throw new TripPreconditionException("Only an unpaid or rejected payment can be reported again.");
        }

        share.LastReportId = requestId;
        share.PaymentState = PaymentState.Reported;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDetails(bill, trip, actor);
    }

    public Task<BillDetails> ConfirmPaymentAsync(
        Guid tripId,
        Guid profileId,
        UserIdentity actor,
        CancellationToken cancellationToken) =>
        ChangePaymentAsync(tripId, profileId, actor, PaymentState.Confirmed, cancellationToken);

    public Task<BillDetails> RejectPaymentAsync(
        Guid tripId,
        Guid profileId,
        UserIdentity actor,
        CancellationToken cancellationToken) =>
        ChangePaymentAsync(tripId, profileId, actor, PaymentState.Rejected, cancellationToken);

    private async Task<BillDetails> ChangePaymentAsync(
        Guid tripId,
        Guid profileId,
        UserIdentity actor,
        PaymentState target,
        CancellationToken cancellationToken)
    {
        ValidateId(tripId, "trip_id");
        ValidateId(profileId, "profile_id");
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var trip = await LockTripAsync(tripId, cancellationToken);
        RequireOwner(trip, actor);
        var bill = await LoadBillAsync(tripId, cancellationToken);
        var share = bill.Shares.SingleOrDefault(value => value.ProfileId == profileId)
            ?? throw new TripNotFoundException("The passenger has no share in this bill.");
        if (share.IsDriver)
        {
            throw new TripPreconditionException("The driver's own share does not require payment confirmation.");
        }

        if (share.PaymentState != target)
        {
            if (share.PaymentState != PaymentState.Reported)
            {
                throw new TripPreconditionException("Only a reported payment can be confirmed or rejected.");
            }

            share.PaymentState = target;
            await database.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return ToDetails(bill, trip, actor);
    }

    private async Task<Trip> LockTripAsync(Guid tripId, CancellationToken cancellationToken) =>
        await database.Trips.FromSqlInterpolated($"SELECT * FROM \"Trips\" WHERE \"Id\" = {tripId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new TripNotFoundException("The trip does not exist.");

    private async Task<TripBill> LoadBillAsync(Guid tripId, CancellationToken cancellationToken) =>
        await database.Bills.Include(value => value.Shares)
            .SingleOrDefaultAsync(value => value.TripId == tripId, cancellationToken)
        ?? throw new TripNotFoundException("The trip has no completed bill.");

    private static BillDetails ToDetails(TripBill bill, Trip trip, UserIdentity actor)
    {
        var isDriver = ApplicationRules.IsOwner(trip, actor);
        if (!isDriver && !bill.Shares.Any(share => IsMine(share, actor)))
        {
            throw new TripPermissionException("Only the driver and accepted passengers can view this bill.");
        }

        var shares = bill.Shares.Where(share => isDriver || IsMine(share, actor))
            .OrderByDescending(share => share.IsDriver).ThenBy(share => share.ProfileId)
            .Select(share => new BillShareDetails(share.ProfileId, share.DisplayName, share.AmountCents,
                share.IsDriver, IsMine(share, actor), share.PaymentState))
            .ToArray();
        return new BillDetails(bill.TripId, bill.DistanceKm, bill.ConsumptionPer100Km, bill.UnitPriceEur,
            bill.TotalCents, bill.CompletedAt, isDriver, shares);
    }

    private static bool IsMine(BillShare share, UserIdentity actor) =>
        share.Issuer == actor.Issuer && share.Subject == actor.Subject;

    private static void RequireOwner(Trip trip, UserIdentity actor)
    {
        if (!ApplicationRules.IsOwner(trip, actor))
        {
            throw new TripPermissionException("Only the driver can manage this trip's bill.");
        }
    }

    private static void ValidateId(Guid id, string field)
    {
        if (id == Guid.Empty)
        {
            throw new TripValidationException($"{field} must be a non-empty UUID.");
        }
    }

    private static void ValidateDecimal(decimal value, string field, decimal maximum)
    {
        if (value <= 0 || value > maximum || decimal.Round(value, 4) != value)
        {
            throw new TripValidationException($"{field} must be positive, no greater than {maximum}, and have at most four decimal places.");
        }
    }

    private static DateTimeOffset MicrosecondPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
}
