using System.Data;
using Carpoolr.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Carpoolr.Api.Features.Billing;

public sealed record CostOverviewTotalsDetails(
    long OwnCostCents,
    long OwnUnpaidCents,
    long OwnReportedCents,
    long OwnConfirmedCents,
    long OwnRejectedCents,
    long DriverTotalReceivableCents,
    long DriverOutstandingCents,
    long DriverReportedCents,
    long DriverConfirmedCents);

public sealed record MyBillEntryDetails(BillDetails Bill, string Origin, string Destination, DateTimeOffset DepartureAt);

public sealed record CostOverviewDetails(
    CostOverviewTotalsDetails Totals,
    IReadOnlyList<MyBillEntryDetails> Bills,
    int? NextOffset);

public sealed partial class BillingApplicationService
{
    public async Task<CostOverviewDetails> GetMyCostOverviewAsync(
        UserIdentity actor, int offset, int pageSize, CancellationToken cancellationToken)
    {
        offset = ApplicationRules.Offset(offset);
        pageSize = ApplicationRules.PageSize(pageSize);

        // Totals and the selected page describe one database snapshot. Aggregates stay
        // in SQL and are independent of pagination; historical bills are never loaded wholesale.
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);

        var own = await database.BillShares.AsNoTracking()
            .Where(share => share.Issuer == actor.Issuer && share.Subject == actor.Subject)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Cost = group.Sum(share => share.AmountCents),
                Unpaid = group.Sum(share => !share.IsDriver && share.PaymentState == PaymentState.Unpaid ? share.AmountCents : 0L),
                Reported = group.Sum(share => !share.IsDriver && share.PaymentState == PaymentState.Reported ? share.AmountCents : 0L),
                Confirmed = group.Sum(share => !share.IsDriver && share.PaymentState == PaymentState.Confirmed ? share.AmountCents : 0L),
                Rejected = group.Sum(share => !share.IsDriver && share.PaymentState == PaymentState.Rejected ? share.AmountCents : 0L),
            }).SingleOrDefaultAsync(cancellationToken);

        var driver = await database.BillShares.AsNoTracking()
            .Where(share => !share.IsDriver && share.Bill.Trip.OwnerIssuer == actor.Issuer &&
                share.Bill.Trip.OwnerSubject == actor.Subject)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Sum(share => share.AmountCents),
                Outstanding = group.Sum(share => share.PaymentState != PaymentState.Confirmed ? share.AmountCents : 0L),
                Reported = group.Sum(share => share.PaymentState == PaymentState.Reported ? share.AmountCents : 0L),
                Confirmed = group.Sum(share => share.PaymentState == PaymentState.Confirmed ? share.AmountCents : 0L),
            }).SingleOrDefaultAsync(cancellationToken);

        var page = await database.Bills.AsNoTracking()
            .Where(bill => bill.Trip.OwnerIssuer == actor.Issuer && bill.Trip.OwnerSubject == actor.Subject ||
                bill.Shares.Any(share => share.Issuer == actor.Issuer && share.Subject == actor.Subject))
            .OrderByDescending(bill => bill.CompletedAt).ThenByDescending(bill => bill.TripId)
            .Skip(offset).Take(pageSize + 1)
            .Select(bill => new
            {
                bill.TripId,
                bill.Trip.Origin,
                bill.Trip.Destination,
                bill.Trip.DepartureAt,
                IsDriver = bill.Trip.OwnerIssuer == actor.Issuer && bill.Trip.OwnerSubject == actor.Subject,
            }).ToListAsync(cancellationToken);

        var hasMore = page.Count > pageSize;
        if (hasMore) page.RemoveAt(page.Count - 1);
        var entries = new List<MyBillEntryDetails>(page.Count);
        if (page.Count > 0)
        {
            var ids = page.Select(value => value.TripId).ToArray();
            var ownedIds = page.Where(value => value.IsDriver).Select(value => value.TripId).ToArray();
            var bills = await database.Bills.AsNoTracking()
                .Where(bill => ids.Contains(bill.TripId))
                .Include(bill => bill.Trip)
                // Filter at the SQL boundary as well as in ToDetails: passengers do not
                // load other passengers' identities, payment states, or individual amounts.
                .Include(bill => bill.Shares.Where(share => ownedIds.Contains(share.TripId) ||
                    share.Issuer == actor.Issuer && share.Subject == actor.Subject))
                .AsSplitQuery()
                .ToDictionaryAsync(bill => bill.TripId, cancellationToken);
            foreach (var item in page)
            {
                var bill = bills[item.TripId];
                entries.Add(new MyBillEntryDetails(ToDetails(bill, bill.Trip, actor), item.Origin, item.Destination, item.DepartureAt));
            }
        }

        var totals = new CostOverviewTotalsDetails(
            own?.Cost ?? 0, own?.Unpaid ?? 0, own?.Reported ?? 0, own?.Confirmed ?? 0, own?.Rejected ?? 0,
            driver?.Total ?? 0, driver?.Outstanding ?? 0, driver?.Reported ?? 0, driver?.Confirmed ?? 0);
        await transaction.CommitAsync(cancellationToken);
        return new CostOverviewDetails(totals, entries,
            hasMore && offset + pageSize <= 10000 ? offset + pageSize : null);
    }
}
