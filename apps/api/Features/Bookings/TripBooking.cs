using Carpoolr.Api.Features.Profiles;
using Carpoolr.Api.Features.Trips;

namespace Carpoolr.Api.Features.Bookings;

public enum BookingState { Pending = 1, Accepted = 2, Rejected = 3, Withdrawn = 4, Cancelled = 5 }

public sealed class TripBooking
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public Guid ProfileId { get; set; }
    public UserProfile Profile { get; set; } = null!;
    public Guid RequestId { get; set; }
    public BookingState State { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
}
