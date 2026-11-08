using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Features;
using Carpoolr.Api.Features.Profiles;
using Carpoolr.Api.Security;
using global::Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using ContractProfile = Carpoolr.Api.Contracts.Trips.V1.UserProfile;
using ContractVehicle = Carpoolr.Api.Contracts.Trips.V1.Vehicle;

namespace Carpoolr.Api.Grpc;

[Authorize(Policy = AuthenticationExtensions.ColleaguePolicy)]
public sealed class ProfileGrpcService(ProfileApplicationService profiles, ICurrentUser currentUser)
    : ProfileService.ProfileServiceBase
{
    public override async Task<GetMyProfileResponse> GetMyProfile(GetMyProfileRequest request, ServerCallContext context)
    {
        var actor = currentUser.GetRequiredIdentity();
        var profile = await profiles.GetAsync(actor, context.CancellationToken);
        var message = profile is null ? new ContractProfile() : Map(profile);
        if (profile is null && actor.Issuer == DemoUserCatalog.Issuer && Guid.TryParse(actor.Subject, out var id))
            message.DisplayName = DemoUserCatalog.Users.SingleOrDefault(user => user.Id == id)?.DisplayName ?? "";
        return new GetMyProfileResponse { Profile = message };
    }

    public override async Task<GetMyProfileResponse> UpdateMyProfile(UpdateMyProfileRequest request, ServerCallContext context)
    {
        VehicleProfile? vehicle = request.Vehicle is null ? null : new VehicleProfile
        {
            Name = request.Vehicle.Name,
            PassengerSeats = request.Vehicle.PassengerSeats,
            EnergyKind = (Features.Profiles.EnergyKind)request.Vehicle.EnergyKind,
            ConsumptionPer100Km = ApplicationRules.Decimal(request.Vehicle.ConsumptionPer100Km, "consumption_per_100_km", 1000),
            UnitPriceEur = ApplicationRules.Decimal(request.Vehicle.UnitPriceEur, "unit_price_eur", 100),
        };
        var profile = await profiles.UpdateAsync(request.DisplayName, request.PickupAddress, vehicle,
            currentUser.GetRequiredIdentity(), context.CancellationToken);
        return new GetMyProfileResponse { Profile = Map(profile) };
    }

    private static ContractProfile Map(Features.Profiles.UserProfile profile) => new()
    {
        Id = profile.Id.ToString("D"),
        DisplayName = profile.DisplayName,
        PickupAddress = profile.PickupAddress,
        IsSaved = true,
        Vehicle = profile.Vehicle is { } vehicle ? MapVehicle(vehicle) : null,
    };

    internal static ContractVehicle MapVehicle(VehicleProfile vehicle) => new()
    {
        Name = vehicle.Name,
        PassengerSeats = vehicle.PassengerSeats,
        EnergyKind = (Contracts.Trips.V1.EnergyKind)vehicle.EnergyKind,
        ConsumptionPer100Km = ApplicationRules.Format(vehicle.ConsumptionPer100Km),
        UnitPriceEur = ApplicationRules.Format(vehicle.UnitPriceEur),
    };
}
