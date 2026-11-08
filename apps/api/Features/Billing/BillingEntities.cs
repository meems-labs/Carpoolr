using Carpoolr.Api.Features.Trips;

namespace Carpoolr.Api.Features.Billing;

public enum PaymentState { Unpaid = 1, Reported = 2, Confirmed = 3, Rejected = 4, DriverShare = 5 }

public sealed class TripBill
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public decimal DistanceKm { get; set; }
    public decimal ConsumptionPer100Km { get; set; }
    public decimal UnitPriceEur { get; set; }
    public long TotalCents { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public List<BillShare> Shares { get; set; } = [];
}

public sealed class BillShare
{
    public Guid TripId { get; set; }
    public TripBill Bill { get; set; } = null!;
    public Guid ProfileId { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public bool IsDriver { get; set; }
    public PaymentState PaymentState { get; set; }
    public Guid? LastReportId { get; set; }
}
