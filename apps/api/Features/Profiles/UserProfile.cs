namespace Carpoolr.Api.Features.Profiles;

public enum EnergyKind { Petrol = 1, Diesel = 2, Electric = 3 }

public sealed class VehicleProfile
{
    public string Name { get; set; } = string.Empty;
    public int PassengerSeats { get; set; }
    public EnergyKind EnergyKind { get; set; }
    public decimal ConsumptionPer100Km { get; set; }
    public decimal UnitPriceEur { get; set; }
}

public sealed class UserProfile
{
    public Guid Id { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PickupAddress { get; set; } = string.Empty;
    public VehicleProfile? Vehicle { get; set; }
}
