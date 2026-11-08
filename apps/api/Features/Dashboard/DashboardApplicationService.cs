using System.Data;
using Carpoolr.Api.Data;
using Carpoolr.Api.Features.Billing;
using Carpoolr.Api.Features.Bookings;
using Carpoolr.Api.Features.Trips;
using Carpoolr.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Carpoolr.Api.Features.Dashboard;

public enum PendingActionKind { BookingRequest = 1, PaymentReport = 2 }

public sealed record PendingActionDetails(
    PendingActionKind Kind, Guid TripId, Guid? BookingId, Guid ProfileId,
    string DisplayName, string Origin, string Destination, DateTimeOffset DepartureAt, long AmountCents);

public sealed record PendingActionsDetails(
    long BookingRequestCount, long PaymentReportCount, IReadOnlyList<PendingActionDetails> Actions);

/// <summary>Live tasks derived from current state; processing a task removes it without a separate read flag.</summary>
public sealed class DashboardApplicationService(CarpoolrDbContext database, TimeProvider clock)
{
    private const int PreviewSizePerKind = 5;

    public async Task<PendingActionsDetails> GetPendingAsync(UserIdentity actor, CancellationToken cancellationToken)
    {
        // Counts and previews must agree even while another person processes a request.
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var now = clock.GetUtcNow();
        var requests = database.Bookings.AsNoTracking().Where(booking =>
            booking.Trip.OwnerIssuer == actor.Issuer && booking.Trip.OwnerSubject == actor.Subject &&
            booking.State == BookingState.Pending && booking.Trip.State == TripState.Planned &&
            booking.Trip.DepartureAt > now);
        var payments = database.BillShares.AsNoTracking().Where(share =>
            share.Bill.Trip.OwnerIssuer == actor.Issuer && share.Bill.Trip.OwnerSubject == actor.Subject &&
            !share.IsDriver && share.PaymentState == PaymentState.Reported);

        // Count in SQL independently of the bounded previews; never load every request or bill.
        var requestCount = await requests.LongCountAsync(cancellationToken);
        var paymentCount = await payments.LongCountAsync(cancellationToken);
        var requestActions = await requests.OrderBy(booking => booking.Trip.DepartureAt)
            .ThenBy(booking => booking.RequestedAt).ThenBy(booking => booking.Id)
            .Take(PreviewSizePerKind)
            .Select(booking => new PendingActionDetails(PendingActionKind.BookingRequest,
                booking.TripId, booking.Id, booking.ProfileId, booking.Profile.DisplayName,
                booking.Trip.Origin, booking.Trip.Destination, booking.Trip.DepartureAt, 0))
            .ToListAsync(cancellationToken);
        var paymentActions = await payments.OrderByDescending(share => share.Bill.CompletedAt)
            .ThenBy(share => share.TripId).ThenBy(share => share.ProfileId)
            .Take(PreviewSizePerKind)
            .Select(share => new PendingActionDetails(PendingActionKind.PaymentReport,
                share.TripId, null, share.ProfileId, share.DisplayName,
                share.Bill.Trip.Origin, share.Bill.Trip.Destination, share.Bill.Trip.DepartureAt, share.AmountCents))
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new PendingActionsDetails(requestCount, paymentCount, [.. requestActions, .. paymentActions]);
    }
}
