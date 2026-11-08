using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Features;
using Carpoolr.Api.Security;
using Google.Protobuf.WellKnownTypes;
using DomainBooking = Carpoolr.Api.Features.Bookings.TripBooking;
using DomainBookingState = Carpoolr.Api.Features.Bookings.BookingState;
using DomainTrip = Carpoolr.Api.Features.Trips.Trip;
using DomainTripState = Carpoolr.Api.Features.Trips.TripState;

namespace Carpoolr.Api.Grpc;

internal static class TripWireMapper
{
    public static Trip Map(DomainTrip trip, UserIdentity actor, DateTimeOffset now)
    {
        var accepted = trip.Bookings.Count(booking => booking.State == DomainBookingState.Accepted);
        var owner = ApplicationRules.IsOwner(trip, actor);
        var totalCents = checked((long)decimal.Round(
            trip.DistanceKm * trip.ConsumptionPer100Km / 100 * trip.UnitPriceEur * 100, 0, MidpointRounding.AwayFromZero));
        var mine = trip.Bookings.Where(booking => IsRequester(booking, actor))
            .OrderByDescending(booking => booking.RequestedAt).ThenByDescending(booking => booking.Id).FirstOrDefault();
        return new Trip
        {
            Id = trip.Id.ToString("D"),
            Origin = trip.Origin,
            Destination = trip.Destination,
            DepartureAt = Timestamp.FromDateTimeOffset(trip.DepartureAt),
            SeatsTotal = trip.SeatsTotal,
            CreatedAt = Timestamp.FromDateTimeOffset(trip.CreatedAt),
            State = (TripState)trip.State,
            DriverDisplayName = trip.DriverProfile?.DisplayName ?? "",
            DriverProfileId = trip.DriverProfileId?.ToString("D") ?? "",
            IsOwner = owner,
            CanCancel = owner && trip.State == DomainTripState.Planned && trip.DepartureAt > now,
            CanComplete = owner && trip.State == DomainTripState.Planned && trip.DepartureAt <= now,
            AvailableSeats = Math.Max(0, trip.SeatsTotal - accepted),
            DistanceKm = ApplicationRules.Format(trip.DistanceKm),
            Vehicle = new Vehicle
            {
                Name = trip.VehicleName,
                PassengerSeats = trip.SeatsTotal,
                EnergyKind = (EnergyKind)trip.EnergyKind,
                ConsumptionPer100Km = ApplicationRules.Format(trip.ConsumptionPer100Km),
                UnitPriceEur = ApplicationRules.Format(trip.UnitPriceEur),
            },
            SeriesId = trip.SeriesId?.ToString("D") ?? "",
            MyBooking = mine is null ? null : MapBooking(mine, trip, actor, now),
            CostPreview = new CostPreview
            {
                TotalCents = totalCents,
                PersonCount = accepted + 1,
                ApproximateShareCents = (long)decimal.Round((decimal)totalCents / (accepted + 1), 0, MidpointRounding.AwayFromZero),
            },
        };
    }

    public static Booking MapBooking(DomainBooking booking, DomainTrip trip, UserIdentity actor, DateTimeOffset now)
    {
        var driver = ApplicationRules.IsOwner(trip, actor);
        var mine = IsRequester(booking, actor);
        var open = trip.State == DomainTripState.Planned && trip.DepartureAt > now;
        return new Booking
        {
            Id = booking.Id.ToString("D"),
            TripId = booking.TripId.ToString("D"),
            RequesterProfileId = booking.ProfileId.ToString("D"),
            RequesterDisplayName = booking.Profile.DisplayName,
            PickupAddress = mine || driver && booking.State == DomainBookingState.Accepted ? booking.Profile.PickupAddress : "",
            State = (BookingState)booking.State,
            RequestedAt = Timestamp.FromDateTimeOffset(booking.RequestedAt),
            CanAccept = driver && open && booking.State == DomainBookingState.Pending &&
                trip.Bookings.Count(value => value.State == DomainBookingState.Accepted) < trip.SeatsTotal,
            CanReject = driver && open && booking.State == DomainBookingState.Pending,
            CanWithdraw = mine && open && booking.State is DomainBookingState.Pending or DomainBookingState.Accepted,
        };
    }

    public static bool IsRequester(DomainBooking booking, UserIdentity actor) =>
        booking.Profile.Issuer == actor.Issuer && booking.Profile.Subject == actor.Subject;
}
