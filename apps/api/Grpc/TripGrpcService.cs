using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Features;
using Carpoolr.Api.Features.Trips;
using Carpoolr.Api.Security;
using Google.Protobuf.WellKnownTypes;
using global::Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using ContractTrip = Carpoolr.Api.Contracts.Trips.V1.Trip;
using DomainTrip = Carpoolr.Api.Features.Trips.Trip;

namespace Carpoolr.Api.Grpc;

[Authorize(Policy = AuthenticationExtensions.ColleaguePolicy)]
public sealed class TripGrpcService(TripApplicationService trips, ICurrentUser currentUser, TimeProvider timeProvider)
    : TripService.TripServiceBase
{
    public override async Task<CreateTripResponse> CreateTrip(
        CreateTripRequest request, ServerCallContext context)
    {
        var command = new CreateTripCommand(
            ParseId(request.RequestId, "request_id"),
            request.Origin,
            request.Destination,
            ParseTimestamp(request.DepartureAt, "departure_at"),
            request.SeatsTotal,
            ApplicationRules.Decimal(request.DistanceKm, "distance_km", 10000));

        var trip = await trips.CreateAsync(command, currentUser.GetRequiredIdentity(), context.CancellationToken);
        return new CreateTripResponse { Trip = ToMessage(trip) };
    }

    public override async Task<GetTripResponse> GetTrip(
        GetTripRequest request, ServerCallContext context)
    {
        var trip = await trips.GetAsync(ParseId(request.TripId, "trip_id"), context.CancellationToken);
        var actor = currentUser.GetRequiredIdentity();
        var response = new GetTripResponse { Trip = ToMessage(trip) };
        response.Bookings.AddRange(trip.Bookings.Where(booking =>
                ApplicationRules.IsOwner(trip, actor) || TripWireMapper.IsRequester(booking, actor))
            .OrderByDescending(booking => booking.RequestedAt).Take(200)
            .Select(booking => TripWireMapper.MapBooking(booking, trip, actor, timeProvider.GetUtcNow())));
        if (ApplicationRules.IsOwner(trip, actor))
        {
            response.RouteStops.Add(new RouteStop { DisplayName = trip.DriverProfile?.DisplayName ?? "Start", Address = trip.Origin });
            var stops = trip.Bookings.Where(booking => booking.State == Features.Bookings.BookingState.Accepted)
                .OrderBy(booking => booking.RequestedAt).ThenBy(booking => booking.Id).ToArray();
            response.RouteStops.AddRange(stops.Select(booking => new RouteStop
            { DisplayName = booking.Profile.DisplayName, Address = booking.Profile.PickupAddress }));
            response.RouteStops.Add(new RouteStop { DisplayName = "Ziel", Address = trip.Destination });
            // Stops retain request order. This link neither optimizes routes nor calculates distance.
            response.RouteUrl = "https://www.google.com/maps/dir/?api=1&origin=" + Uri.EscapeDataString(trip.Origin) +
                "&destination=" + Uri.EscapeDataString(trip.Destination) +
                (stops.Length == 0 ? "" : "&waypoints=" + Uri.EscapeDataString(string.Join("|", stops.Select(booking => booking.Profile.PickupAddress))));
        }
        return response;
    }

    public override async Task<SearchTripsResponse> SearchTrips(
        SearchTripsRequest request, ServerCallContext context)
    {
        var query = new SearchTripsQuery(
            string.IsNullOrEmpty(request.Origin) ? null : request.Origin,
            string.IsNullOrEmpty(request.Destination) ? null : request.Destination,
            ParseTimestamp(request.DepartureFrom, "departure_from"),
            ParseTimestamp(request.DepartureUntil, "departure_until"),
            request.Offset,
            request.PageSize == 0 ? 20 : request.PageSize);

        var result = await trips.SearchAsync(query, context.CancellationToken);
        var response = new SearchTripsResponse();
        response.Trips.AddRange(result.Trips.Select(ToMessage));

        if (result.NextOffset is { } nextOffset)
        {
            response.NextOffset = nextOffset;
        }

        return response;
    }

    public override async Task<CancelTripResponse> CancelTrip(
        CancelTripRequest request, ServerCallContext context)
    {
        var trip = await trips.CancelAsync(
            ParseId(request.TripId, "trip_id"), currentUser.GetRequiredIdentity(), context.CancellationToken);
        return new CancelTripResponse { Trip = ToMessage(trip) };
    }

    public override async Task<SearchTripsResponse> ListMyTrips(ListMyTripsRequest request, ServerCallContext context)
    {
        var result = await trips.ListMineAsync(currentUser.GetRequiredIdentity(), request.Offset, request.PageSize, context.CancellationToken);
        var response = new SearchTripsResponse();
        response.Trips.AddRange(result.Trips.Select(ToMessage));
        if (result.NextOffset is { } offset) response.NextOffset = offset;
        return response;
    }

    private static Guid ParseId(string value, string field)
    {
        if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
        {
            throw new TripValidationException($"{field} must be a non-empty UUID in hyphenated format.");
        }

        return id;
    }

    private static DateTimeOffset ParseTimestamp(Timestamp? value, string field)
    {
        if (value is null)
        {
            throw new TripValidationException($"{field} is required.");
        }

        if (value.Seconds is < -62135596800 or > 253402300799 ||
            value.Nanos is < 0 or > 999999999)
        {
            throw new TripValidationException($"{field} must be a valid UTC timestamp.");
        }

        return value.ToDateTimeOffset();
    }

    private ContractTrip ToMessage(DomainTrip trip) =>
        TripWireMapper.Map(trip, currentUser.GetRequiredIdentity(), timeProvider.GetUtcNow());
}
