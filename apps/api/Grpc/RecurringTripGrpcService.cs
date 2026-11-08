using System.Globalization;
using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Features;
using Carpoolr.Api.Features.RecurringTrips;
using Carpoolr.Api.Features.Trips;
using Carpoolr.Api.Security;
using global::Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Carpoolr.Api.Grpc;

[Authorize(Policy = AuthenticationExtensions.ColleaguePolicy)]
public sealed class RecurringTripGrpcService(RecurringTripApplicationService series, ICurrentUser user)
    : RecurringTripService.RecurringTripServiceBase
{
    public override async Task<SeriesResponse> CreateSeries(CreateSeriesRequest request, ServerCallContext context)
    {
        if (!DateOnly.TryParseExact(request.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !DateOnly.TryParseExact(request.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
            !TimeOnly.TryParseExact(request.LocalDepartureTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new TripValidationException("Use yyyy-MM-dd dates and an HH:mm Europe/Berlin time.");
        var value = await series.CreateAsync(new CreateSeriesCommand(ApplicationRules.Id(request.RequestId, "request_id"),
                request.Origin, request.Destination, request.SeatsTotal, ApplicationRules.Decimal(request.DistanceKm, "distance_km", 10000),
                start, end, request.Weekdays.ToArray(), time), user.GetRequiredIdentity(), context.CancellationToken);
        return new SeriesResponse { Series = Map(value) };
    }

    public override async Task<ListMySeriesResponse> ListMySeries(ListMySeriesRequest request, ServerCallContext context)
    {
        var page = await series.ListMineAsync(user.GetRequiredIdentity(), request.Offset, request.PageSize, context.CancellationToken);
        var response = new ListMySeriesResponse();
        response.Series.AddRange(page.Series.Select(Map));
        if (page.NextOffset is { } offset) response.NextOffset = offset;
        return response;
    }

    public override async Task<SeriesResponse> CancelSeries(CancelSeriesRequest request, ServerCallContext context) => new()
    {
        Series = Map(await series.CancelAsync(ApplicationRules.Id(request.SeriesId, "series_id"),
            user.GetRequiredIdentity(), context.CancellationToken)),
    };

    private static Contracts.Trips.V1.RecurringSeries Map(Features.RecurringTrips.RecurringSeries value)
    {
        var result = new Contracts.Trips.V1.RecurringSeries
        {
            Id = value.Id.ToString("D"),
            Origin = value.Origin,
            Destination = value.Destination,
            SeatsTotal = value.SeatsTotal,
            DistanceKm = ApplicationRules.Format(value.DistanceKm),
            StartDate = value.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDate = value.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            LocalDepartureTime = value.LocalDepartureTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            TimeZone = "Europe/Berlin",
            IsCancelled = value.IsCancelled,
        };
        result.Weekdays.AddRange(value.Weekdays);
        return result;
    }
}
