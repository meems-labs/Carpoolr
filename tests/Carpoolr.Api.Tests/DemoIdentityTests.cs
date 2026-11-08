using System.Net;
using System.Net.Http.Json;
using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Grpc;
using Carpoolr.Api.Security;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Carpoolr.Api.Tests;

public sealed class DemoIdentityTests(GrpcApiFixture fixture) : IClassFixture<GrpcApiFixture>
{
    [Fact]
    public async Task EnabledStaging_ExposesOnlyPredefinedPeopleWithoutLogin()
    {
        using var factory = CreateDemoFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/demo/users");
        var catalog = await response.Content.ReadFromJsonAsync<DemoUsersResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(catalog);
        Assert.True(catalog.Enabled);
        Assert.Equal(DemoUserCatalog.Users, catalog.Users);
    }

    [Fact]
    public async Task SelectedPerson_CanUseGrpcWithoutJwtWhileOwnershipChecksStillApply()
    {
        using var factory = CreateDemoFactory();
        using var client = GrpcApiFixture.CreateClient(factory, token: null);
        var owner = PersonHeaders(0);
        var anotherPerson = PersonHeaders(1);
        await client.Profiles.UpdateMyProfileAsync(new UpdateMyProfileRequest
        {
            DisplayName = "Testperson 1",
            PickupAddress = "Demo start address",
            Vehicle = new Vehicle
            {
                Name = "Demo car",
                PassengerSeats = 2,
                EnergyKind = EnergyKind.Petrol,
                ConsumptionPer100Km = "7",
                UnitPriceEur = "1.8",
            },
        }, headers: owner);
        var created = await client.Trips.CreateTripAsync(new CreateTripRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Origin = "Demo start",
            Destination = "Demo destination",
            DepartureAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(1)),
            SeatsTotal = 2,
            DistanceKm = "25",
        }, headers: owner);

        var trip = await client.Trips.GetTripAsync(new GetTripRequest { TripId = created.Trip.Id },
            headers: anotherPerson);
        var denied = await Assert.ThrowsAsync<RpcException>(() => client.Trips.CancelTripAsync(
            new CancelTripRequest { TripId = created.Trip.Id }, headers: anotherPerson).ResponseAsync);
        var cancelled = await client.Trips.CancelTripAsync(new CancelTripRequest { TripId = created.Trip.Id },
            headers: owner);

        Assert.Equal(created.Trip.Id, trip.Trip.Id);
        Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
        Assert.Equal(TripState.Cancelled, cancelled.Trip.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-person")]
    [InlineData("44444444-4444-4444-8444-444444444444")]
    public async Task MissingOrUnknownPerson_IsRejected(string? selectedId)
    {
        using var factory = CreateDemoFactory();
        using var client = GrpcApiFixture.CreateClient(factory, token: null);
        var headers = new Metadata();
        if (selectedId is not null)
        {
            headers.Add(DemoUserCatalog.HeaderName.ToLowerInvariant(), selectedId);
        }

        var denied = await Assert.ThrowsAsync<RpcException>(() => client.Trips.GetTripAsync(
            new GetTripRequest { TripId = Guid.NewGuid().ToString("D") }, headers: headers).ResponseAsync);

        Assert.Equal(StatusCode.Unauthenticated, denied.StatusCode);
    }

    [Fact]
    public async Task DemoHeader_WhenDisabled_DoesNotAuthenticate()
    {
        using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("DemoMode:Enabled", "false"));
        using var client = GrpcApiFixture.CreateClient(factory, token: null);

        var denied = await Assert.ThrowsAsync<RpcException>(() => client.Trips.GetTripAsync(
            new GetTripRequest { TripId = Guid.NewGuid().ToString("D") }, headers: PersonHeaders(0)).ResponseAsync);

        Assert.Equal(StatusCode.Unauthenticated, denied.StatusCode);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void DemoEnabledOutsideStaging_RefusesToStart(string environment)
    {
        using var factory = new WebApplicationFactory<TripGrpcService>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("DemoMode:Enabled", "true");
        });

        var failure = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("DemoMode:Enabled", failure.Message);
    }

    private WebApplicationFactory<TripGrpcService> CreateDemoFactory() =>
        fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("DemoMode:Enabled", "true");
        });

    private static Metadata PersonHeaders(int index) => new()
    {
        { DemoUserCatalog.HeaderName.ToLowerInvariant(), DemoUserCatalog.Users[index].Id.ToString("D") },
    };
}
