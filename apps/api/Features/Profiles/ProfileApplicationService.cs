using Carpoolr.Api.Data;
using Carpoolr.Api.Features.Trips;
using Carpoolr.Api.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carpoolr.Api.Features.Profiles;

public sealed class ProfileApplicationService(CarpoolrDbContext database)
{
    public Task<UserProfile?> GetAsync(UserIdentity actor, CancellationToken cancellationToken) =>
        database.Profiles.AsNoTracking().SingleOrDefaultAsync(
            profile => profile.Issuer == actor.Issuer && profile.Subject == actor.Subject, cancellationToken);

    public async Task<UserProfile> RequireAsync(UserIdentity actor, CancellationToken cancellationToken)
    {
        var profile = await GetAsync(actor, cancellationToken);
        if (profile is null || string.IsNullOrWhiteSpace(profile.PickupAddress))
            throw new TripPreconditionException("Save your display name and full pickup address first.");
        return profile;
    }

    public async Task<UserProfile> UpdateAsync(
        string displayName, string pickupAddress, VehicleProfile? vehicle,
        UserIdentity actor, CancellationToken cancellationToken)
    {
        displayName = ApplicationRules.Text(displayName, "display_name", 100);
        pickupAddress = ApplicationRules.Text(pickupAddress, "pickup_address", 500);
        if (vehicle is not null)
        {
            vehicle.Name = ApplicationRules.Text(vehicle.Name, "vehicle.name", 100);
            if (vehicle.PassengerSeats is < 1 or > 8 || !Enum.IsDefined(vehicle.EnergyKind) ||
                vehicle.ConsumptionPer100Km is <= 0 or > 1000 || vehicle.UnitPriceEur is <= 0 or > 100)
                throw new TripValidationException("Vehicle capacity, energy kind, consumption or unit price is invalid.");
        }

        var profile = await database.Profiles.SingleOrDefaultAsync(
            value => value.Issuer == actor.Issuer && value.Subject == actor.Subject, cancellationToken);
        if (profile is null)
        {
            profile = new UserProfile { Id = Guid.NewGuid(), Issuer = actor.Issuer, Subject = actor.Subject };
            database.Profiles.Add(profile);
        }

        Apply(profile, displayName, pickupAddress, vehicle);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Profiles_Issuer_Subject" })
        {
            // Two first saves for the same account must update one profile, never create two.
            database.ChangeTracker.Clear();
            profile = await database.Profiles.SingleAsync(
                value => value.Issuer == actor.Issuer && value.Subject == actor.Subject, cancellationToken);
            Apply(profile, displayName, pickupAddress, vehicle);
            await database.SaveChangesAsync(cancellationToken);
        }
        return profile;
    }

    private static void Apply(UserProfile profile, string name, string address, VehicleProfile? vehicle)
    {
        profile.DisplayName = name;
        profile.PickupAddress = address;
        profile.Vehicle = vehicle;
    }
}
