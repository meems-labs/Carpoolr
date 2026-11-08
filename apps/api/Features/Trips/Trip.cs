using Carpoolr.Api.Features.Bookings;
using Carpoolr.Api.Features.Profiles;

namespace Carpoolr.Api.Features.Trips;

public enum TripState
{
    Planned = 1,
    Cancelled = 2,
    Completed = 3,
}

/// <summary>
/// Persisted trip data. Ownership is assigned by the server, never by a request message.
/// </summary>
public sealed class Trip
{
    private Trip()
    {
    }

    internal Trip(
        Guid id,
        Guid requestId,
        string ownerIssuer,
        string ownerSubject,
        string origin,
        string destination,
        DateTimeOffset departureAt,
        int seatsTotal,
        DateTimeOffset createdAt)
    {
        Id = id;
        RequestId = requestId;
        OwnerIssuer = ownerIssuer;
        OwnerSubject = ownerSubject;
        Origin = origin;
        Destination = destination;
        DepartureAt = departureAt;
        SeatsTotal = seatsTotal;
        CreatedAt = createdAt;
        State = TripState.Planned;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public string OwnerIssuer { get; private set; } = string.Empty;

    public string OwnerSubject { get; private set; } = string.Empty;

    public string Origin { get; private set; } = string.Empty;

    public string Destination { get; private set; } = string.Empty;

    public DateTimeOffset DepartureAt { get; private set; }

    public int SeatsTotal { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public TripState State { get; internal set; }

    public Guid? DriverProfileId { get; set; }
    public UserProfile? DriverProfile { get; set; }
    public decimal DistanceKm { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public EnergyKind EnergyKind { get; set; }
    public decimal ConsumptionPer100Km { get; set; }
    public decimal UnitPriceEur { get; set; }
    public Guid? SeriesId { get; set; }
    public List<TripBooking> Bookings { get; set; } = [];
}
