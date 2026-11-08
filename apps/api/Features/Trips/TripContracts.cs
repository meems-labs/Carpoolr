namespace Carpoolr.Api.Features.Trips;

public sealed record CreateTripCommand(
    Guid RequestId,
    string Origin,
    string Destination,
    DateTimeOffset DepartureAt,
    int SeatsTotal,
    decimal DistanceKm = 0);

public sealed record SearchTripsQuery(
    string? Origin,
    string? Destination,
    DateTimeOffset From,
    DateTimeOffset Until,
    int Offset,
    int PageSize);

public sealed record TripSearchResult(IReadOnlyList<Trip> Trips, int? NextOffset);
