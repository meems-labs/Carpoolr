using Carpoolr.Api.Features;
using Carpoolr.Api.Features.Billing;
using Carpoolr.Api.Security;
using Google.Protobuf.WellKnownTypes;
using global::Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Wire = global::Carpoolr.Api.Contracts.Trips.V1;

namespace Carpoolr.Api.Grpc;

[Authorize(Policy = AuthenticationExtensions.ColleaguePolicy)]
public sealed class BillingGrpcService(BillingApplicationService billing, ICurrentUser currentUser)
    : Wire.BillingService.BillingServiceBase
{
    public override async Task<Wire.BillResponse> CompleteTrip(
        Wire.CompleteTripRequest request,
        ServerCallContext context)
    {
        var command = new CompleteTripCommand(
            ApplicationRules.Decimal(request.DistanceKm, "distance_km", 10000m),
            ApplicationRules.Decimal(request.ConsumptionPer100Km, "consumption_per_100_km", 1000m),
            ApplicationRules.Decimal(request.UnitPriceEur, "unit_price_eur", 100m));
        var bill = await billing.CompleteAsync(ApplicationRules.Id(request.TripId, "trip_id"), command,
            currentUser.GetRequiredIdentity(), context.CancellationToken);
        return ToResponse(bill);
    }

    public override async Task<Wire.BillResponse> GetBill(
        Wire.GetBillRequest request,
        ServerCallContext context)
    {
        var bill = await billing.GetAsync(ApplicationRules.Id(request.TripId, "trip_id"),
            currentUser.GetRequiredIdentity(), context.CancellationToken);
        return ToResponse(bill);
    }

    public override async Task<Wire.BillResponse> ReportPayment(
        Wire.ReportPaymentRequest request,
        ServerCallContext context)
    {
        var bill = await billing.ReportPaymentAsync(ApplicationRules.Id(request.TripId, "trip_id"),
            ApplicationRules.Id(request.RequestId, "request_id"), currentUser.GetRequiredIdentity(),
            context.CancellationToken);
        return ToResponse(bill);
    }

    public override async Task<Wire.BillResponse> ConfirmPayment(
        Wire.ChangePaymentRequest request,
        ServerCallContext context)
    {
        var bill = await billing.ConfirmPaymentAsync(ApplicationRules.Id(request.TripId, "trip_id"),
            ApplicationRules.Id(request.ProfileId, "profile_id"), currentUser.GetRequiredIdentity(),
            context.CancellationToken);
        return ToResponse(bill);
    }

    public override async Task<Wire.BillResponse> RejectPayment(
        Wire.ChangePaymentRequest request,
        ServerCallContext context)
    {
        var bill = await billing.RejectPaymentAsync(ApplicationRules.Id(request.TripId, "trip_id"),
            ApplicationRules.Id(request.ProfileId, "profile_id"), currentUser.GetRequiredIdentity(),
            context.CancellationToken);
        return ToResponse(bill);
    }

    private static Wire.BillResponse ToResponse(BillDetails details)
    {
        var bill = new Wire.Bill
        {
            TripId = details.TripId.ToString("D"),
            DistanceKm = ApplicationRules.Format(details.DistanceKm),
            ConsumptionPer100Km = ApplicationRules.Format(details.ConsumptionPer100Km),
            UnitPriceEur = ApplicationRules.Format(details.UnitPriceEur),
            TotalCents = details.TotalCents,
            CompletedAt = Timestamp.FromDateTimeOffset(details.CompletedAt),
            IsDriver = details.IsDriver,
        };
        bill.Shares.AddRange(details.Shares.Select(share => new Wire.BillShare
        {
            ProfileId = share.ProfileId.ToString("D"),
            DisplayName = share.DisplayName,
            AmountCents = share.AmountCents,
            PaymentState = (Wire.PaymentState)(int)share.PaymentState,
            IsMine = share.IsMine,
            CanReport = share.IsMine && !share.IsDriver &&
                share.PaymentState is PaymentState.Unpaid or PaymentState.Rejected,
            CanConfirm = details.IsDriver && !share.IsDriver && share.PaymentState == PaymentState.Reported,
            CanReject = details.IsDriver && !share.IsDriver && share.PaymentState == PaymentState.Reported,
        }));
        return new Wire.BillResponse { Bill = bill };
    }
}
