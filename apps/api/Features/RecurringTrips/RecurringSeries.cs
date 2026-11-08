namespace Carpoolr.Api.Features.RecurringTrips;

public sealed class RecurringSeries
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public string OwnerIssuer { get; set; } = string.Empty;
    public string OwnerSubject { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public int SeatsTotal { get; set; }
    public decimal DistanceKm { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public TimeOnly LocalDepartureTime { get; set; }
    public int[] Weekdays { get; set; } = [];
    public bool IsCancelled { get; set; }
}
