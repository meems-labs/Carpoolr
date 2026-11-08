using System.Net;
using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Features.Billing;
using Carpoolr.Api.Features.Bookings;
using Carpoolr.Api.Features.Profiles;
using Carpoolr.Api.Features.Trips;
using Carpoolr.Api.Grpc;
using Carpoolr.Api.Security;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using DomainEnergyKind = Carpoolr.Api.Features.Profiles.EnergyKind;

namespace Carpoolr.Api.Tests;

public sealed class BillingOverviewTests(GrpcApiFixture fixture) : IClassFixture<GrpcApiFixture>
{
    [Fact]
    public async Task Overview_AggregatesAllFinalizedBillsAndKeepsPassengerSharesPrivateAcrossPages()
    {
        var clock = new OverviewClock();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        var driver = Actor();
        var passenger = Actor();
        var otherPassenger = Actor();
        foreach (var actor in new[] { driver, passenger, otherPassenger })
            await InvokeAsync<ProfileApplicationService, Features.Profiles.UserProfile>(factory, service =>
                service.UpdateAsync("Test person", "Test Road 1, Test City", new VehicleProfile
                {
                    Name = "Test vehicle",
                    PassengerSeats = 2,
                    EnergyKind = DomainEnergyKind.Petrol,
                    ConsumptionPer100Km = 6,
                    UnitPriceEur = 2,
                }, actor, CancellationToken.None));

        var trips = new List<Guid>();
        for (var index = 0; index < 4; index++)
        {
            var trip = await InvokeAsync<TripApplicationService, Features.Trips.Trip>(factory, service => service.CreateAsync(
                new CreateTripCommand(Guid.NewGuid(), $"Start {Guid.NewGuid():N}", "Destination", clock.Now.AddHours(1), 2, 10),
                driver, CancellationToken.None));
            trips.Add(trip.Id);
            foreach (var actor in new[] { passenger, otherPassenger })
            {
                var booking = await InvokeAsync<BookingApplicationService, BookingDetails>(factory, service =>
                    service.RequestAsync(trip.Id, Guid.NewGuid(), actor, CancellationToken.None));
                await InvokeAsync<BookingApplicationService, BookingDetails>(factory, service =>
                    service.AcceptAsync(booking.Booking.Id, driver, CancellationToken.None));
            }
        }
        clock.Now = clock.Now.AddHours(2);
        foreach (var trip in trips)
            await InvokeAsync<BillingApplicationService, BillDetails>(factory, service =>
                service.CompleteAsync(trip, new CompleteTripCommand(10, 6, 2), driver, CancellationToken.None));

        await ReportAsync(trips[1]);
        var confirmed = await ReportAsync(trips[2]);
        var passengerId = Assert.Single(confirmed.Shares).ProfileId;
        await InvokeAsync<BillingApplicationService, BillDetails>(factory, service =>
            service.ConfirmPaymentAsync(trips[2], passengerId, driver, CancellationToken.None));
        await ReportAsync(trips[3]);
        await InvokeAsync<BillingApplicationService, BillDetails>(factory, service =>
            service.RejectPaymentAsync(trips[3], passengerId, driver, CancellationToken.None));

        var first = await ReadAsync(factory, passenger, new GetMyCostOverviewRequest { PageSize = 1 });
        Assert.Equal(160, first.Totals.OwnCostCents);
        Assert.Equal(40, first.Totals.OwnUnpaidCents);
        Assert.Equal(40, first.Totals.OwnReportedCents);
        Assert.Equal(40, first.Totals.OwnConfirmedCents);
        Assert.Equal(40, first.Totals.OwnRejectedCents);
        Assert.Equal(0, first.Totals.DriverTotalReceivableCents);
        Assert.Single(first.Bills);
        Assert.True(first.HasNextOffset);
        Assert.Equal(1, first.NextOffset);
        Assert.Single(first.Bills[0].Bill.Shares);
        Assert.True(first.Bills[0].Bill.Shares[0].IsMine);
        Assert.False(first.Bills[0].Bill.IsDriver);
        Assert.False(string.IsNullOrWhiteSpace(first.Bills[0].Origin));
        Assert.False(string.IsNullOrWhiteSpace(first.Bills[0].Destination));
        Assert.NotNull(first.Bills[0].DepartureAt);

        var rest = await ReadAsync(factory, passenger, new GetMyCostOverviewRequest { Offset = 1, PageSize = 100 });
        Assert.Equal(first.Totals, rest.Totals);
        Assert.Equal(3, rest.Bills.Count);
        Assert.False(rest.HasNextOffset);
        Assert.DoesNotContain(rest.Bills, entry => entry.Bill.TripId == first.Bills[0].Bill.TripId);
        Assert.All(rest.Bills, entry => Assert.Single(entry.Bill.Shares));

        var received = await ReadAsync(factory, driver, new GetMyCostOverviewRequest { PageSize = 100 });
        Assert.Equal(160, received.Totals.OwnCostCents);
        Assert.Equal(0, received.Totals.OwnUnpaidCents);
        Assert.Equal(320, received.Totals.DriverTotalReceivableCents);
        Assert.Equal(280, received.Totals.DriverOutstandingCents);
        Assert.Equal(40, received.Totals.DriverReportedCents);
        Assert.Equal(40, received.Totals.DriverConfirmedCents);
        Assert.Equal(4, received.Bills.Count);
        Assert.All(received.Bills, entry =>
        {
            Assert.True(entry.Bill.IsDriver);
            Assert.Equal(3, entry.Bill.Shares.Count);
        });

        var foreignIssuer = passenger with { Issuer = GrpcApiFixture.OtherIssuer };
        var unauthorized = await ReadAsync(factory, foreignIssuer, new GetMyCostOverviewRequest());
        Assert.Empty(unauthorized.Bills);
        Assert.Equal(new CostOverviewTotals(), unauthorized.Totals);

        var untouched = await ReadAsync(factory, otherPassenger, new GetMyCostOverviewRequest());
        Assert.Equal(160, untouched.Totals.OwnUnpaidCents);
        Assert.Equal(0, untouched.Totals.OwnReportedCents);

        var outOfRange = await ReadAsync(factory, passenger, new GetMyCostOverviewRequest { Offset = 100, PageSize = 10 });
        Assert.Empty(outOfRange.Bills);
        Assert.Equal(first.Totals, outOfRange.Totals);

        async Task<BillDetails> ReportAsync(Guid tripId) =>
            await InvokeAsync<BillingApplicationService, BillDetails>(factory, service =>
                service.ReportPaymentAsync(tripId, Guid.NewGuid(), passenger, CancellationToken.None));
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(10001, 20)]
    [InlineData(0, 101)]
    public async Task Overview_RejectsInvalidPagination(int offset, int size)
    {
        var failure = await Assert.ThrowsAsync<RpcException>(() => ReadAsync(fixture.Factory, Actor(),
            new GetMyCostOverviewRequest { Offset = offset, PageSize = size }));
        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
    }

    [Fact]
    public async Task Overview_RequiresAuthentication()
    {
        var failure = await Assert.ThrowsAsync<RpcException>(() =>
            ReadAsync(fixture.Factory, null, new GetMyCostOverviewRequest()));
        Assert.Equal(StatusCode.Unauthenticated, failure.StatusCode);
    }

    private static UserIdentity Actor() => new(GrpcApiFixture.Issuer, Guid.NewGuid().ToString("D"));

    private static async Task<TResult> InvokeAsync<TService, TResult>(
        WebApplicationFactory<TripGrpcService> factory, Func<TService, Task<TResult>> action) where TService : notnull
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static async Task<GetMyCostOverviewResponse> ReadAsync(
        WebApplicationFactory<TripGrpcService> factory, UserIdentity? actor, GetMyCostOverviewRequest request)
    {
        var http = new HttpClient(new GrpcWebHandler(GrpcWebMode.GrpcWeb, factory.Server.CreateHandler()));
        if (actor is not null)
            http.DefaultRequestHeaders.Authorization = new("Bearer", GrpcApiFixture.CreateToken(actor.Subject, actor.Issuer));
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpClient = http,
            DisposeHttpClient = true,
            HttpVersion = HttpVersion.Version11,
            HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        });
        return await new BillingService.BillingServiceClient(channel).GetMyCostOverviewAsync(request);
    }

    private sealed class OverviewClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2032, 1, 5, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
