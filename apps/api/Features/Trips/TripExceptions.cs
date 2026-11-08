namespace Carpoolr.Api.Features.Trips;

// These messages describe expected failures and may be returned to API clients.
// Unexpected exceptions are translated separately at the transport boundary.
public sealed class TripValidationException(string message) : Exception(message)
{
}

public sealed class TripNotFoundException(string message = "The trip was not found.") : Exception(message)
{
}

public sealed class TripPermissionException(string message = "You cannot modify this trip.") : Exception(message)
{
}

public sealed class TripConflictException(string message = "The request_id has already been used with different trip data.")
    : Exception(message)
{
}

public sealed class TripPreconditionException(string message) : Exception(message)
{
}
