using Carpoolr.Api.Features.Dashboard;
using Carpoolr.Api.Security;
using Google.Protobuf.WellKnownTypes;
using global::Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Wire = Carpoolr.Api.Contracts.Trips.V1;

namespace Carpoolr.Api.Grpc;

[Authorize(Policy = AuthenticationExtensions.ColleaguePolicy)]
public sealed class DashboardGrpcService(DashboardApplicationService dashboard, ICurrentUser user)
    : Wire.DashboardService.DashboardServiceBase
{
    public override async Task<Wire.GetPendingActionsResponse> GetPendingActions(
        Wire.GetPendingActionsRequest request, ServerCallContext context)
    {
        var details = await dashboard.GetPendingAsync(user.GetRequiredIdentity(), context.CancellationToken);
        var response = new Wire.GetPendingActionsResponse
        {
            BookingRequestCount = details.BookingRequestCount,
            PaymentReportCount = details.PaymentReportCount,
        };
        response.Actions.AddRange(details.Actions.Select(action => new Wire.PendingAction
        {
            Kind = (Wire.PendingActionKind)action.Kind,
            TripId = action.TripId.ToString("D"),
            BookingId = action.BookingId?.ToString("D") ?? "",
            ProfileId = action.ProfileId.ToString("D"),
            DisplayName = action.DisplayName,
            Origin = action.Origin,
            Destination = action.Destination,
            DepartureAt = Timestamp.FromDateTimeOffset(action.DepartureAt),
            AmountCents = action.AmountCents,
        }));
        return response;
    }
}
