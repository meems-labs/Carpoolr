using System.Net;
using System.Net.Http.Headers;
using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Grpc;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Carpoolr.Api.Tests;

public sealed class IncomingBookingTests(GrpcApiFixture fixture) : IClassFixture<GrpcApiFixture>
{
    [Fact]
    public async Task IncomingOverview_ContainsOnlyOwnedTripsAndPreservesRequestPermissionsAndPrivacy()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var soon = await environment.OfferAsync(environment.Driver, 20);
        var later = await environment.OfferAsync(environment.Driver, 30);
        var foreignTrip = await environment.OfferAsync(environment.Other, 15);
        var first = await environment.RequestAsync(environment.Passenger, soon);
        var second = await environment.RequestAsync(environment.Passenger, later);
        var foreign = await environment.RequestAsync(environment.Passenger, foreignTrip);

        var pending = await environment.Driver.Bookings.ListIncomingBookingsAsync(
            new ListIncomingBookingsRequest { PendingOnly = true });
        Assert.Equal(new[] { first.Id, second.Id }, pending.Entries.Select(entry => entry.Booking.Id));
        Assert.All(pending.Entries, entry =>
        {
            Assert.True(entry.Trip.IsOwner);
            Assert.True(entry.Booking.CanAccept);
            Assert.True(entry.Booking.CanReject);
            Assert.False(entry.Booking.CanWithdraw);
            Assert.Empty(entry.Booking.PickupAddress);
        });

        await environment.Driver.Bookings.AcceptBookingAsync(new ChangeBookingRequest { BookingId = first.Id });
        var afterAcceptance = await environment.Driver.Bookings.ListIncomingBookingsAsync(new ListIncomingBookingsRequest());
        Assert.Equal(new[] { second.Id, first.Id }, afterAcceptance.Entries.Select(entry => entry.Booking.Id));
        Assert.Equal(TestEnvironment.PassengerAddress,
            Assert.Single(afterAcceptance.Entries, entry => entry.Booking.Id == first.Id).Booking.PickupAddress);
        Assert.Empty(Assert.Single(afterAcceptance.Entries, entry => entry.Booking.Id == second.Id).Booking.PickupAddress);
        var remaining = await environment.Driver.Bookings.ListIncomingBookingsAsync(
            new ListIncomingBookingsRequest { PendingOnly = true });
        Assert.Equal(second.Id, Assert.Single(remaining.Entries).Booking.Id);

        var foreignOwner = await environment.Other.Bookings.ListIncomingBookingsAsync(new ListIncomingBookingsRequest());
        Assert.Equal(foreign.Id, Assert.Single(foreignOwner.Entries).Booking.Id);
        var passenger = await environment.Passenger.Bookings.ListIncomingBookingsAsync(new ListIncomingBookingsRequest());
        Assert.Empty(passenger.Entries);

        await environment.Driver.Bookings.RejectBookingAsync(new ChangeBookingRequest { BookingId = second.Id });
        var history = await environment.Driver.Bookings.ListIncomingBookingsAsync(new ListIncomingBookingsRequest());
        Assert.Contains(history.Entries, entry => entry.Booking.State == BookingState.Accepted);
        Assert.Contains(history.Entries, entry => entry.Booking.State == BookingState.Rejected);
        Assert.Empty((await environment.Driver.Bookings.ListIncomingBookingsAsync(
            new ListIncomingBookingsRequest { PendingOnly = true })).Entries);
    }

    [Fact]
    public async Task PendingOnly_ExcludesDepartedCancelledAndCompletedTrips()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var departed = await environment.OfferAsync(environment.Driver, 5);
        var completed = await environment.OfferAsync(environment.Driver, 6);
        var future = await environment.OfferAsync(environment.Driver, 30);
        var cancelled = await environment.OfferAsync(environment.Driver, 40);
        var departedRequest = await environment.RequestAsync(environment.Passenger, departed);
        var completedRequest = await environment.RequestAsync(environment.Passenger, completed);
        var futureRequest = await environment.RequestAsync(environment.Passenger, future);
        var cancelledRequest = await environment.RequestAsync(environment.Passenger, cancelled);
        await environment.Driver.Trips.CancelTripAsync(new CancelTripRequest { TripId = cancelled.Id });
        environment.Clock.Advance(TimeSpan.FromMinutes(10));
        await environment.Driver.Billing.CompleteTripAsync(new CompleteTripRequest
        {
            TripId = completed.Id,
            DistanceKm = "25",
            ConsumptionPer100Km = "6.5",
            UnitPriceEur = "1.8",
        });

        var pending = await environment.Driver.Bookings.ListIncomingBookingsAsync(
            new ListIncomingBookingsRequest { PendingOnly = true });
        Assert.Equal(futureRequest.Id, Assert.Single(pending.Entries).Booking.Id);
        var all = await environment.Driver.Bookings.ListIncomingBookingsAsync(new ListIncomingBookingsRequest());
        Assert.Equal(new[] { futureRequest.Id, cancelledRequest.Id, completedRequest.Id, departedRequest.Id },
            all.Entries.Select(entry => entry.Booking.Id));
        Assert.All(all.Entries.Where(entry => entry.Booking.Id != futureRequest.Id), entry =>
        {
            Assert.False(entry.Booking.CanAccept);
            Assert.False(entry.Booking.CanReject);
        });
    }

    [Fact]
    public async Task IncomingPagination_IsBoundedAndStableAcrossEqualDepartureAndRequestTimes()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var firstTrip = await environment.OfferAsync(environment.Driver, 20);
        var secondTrip = await environment.OfferAsync(environment.Driver, 20);
        var rows = new[]
        {
            await environment.RequestAsync(environment.Passenger, firstTrip),
            await environment.RequestAsync(environment.Other, firstTrip),
            await environment.RequestAsync(environment.Passenger, secondTrip),
            await environment.RequestAsync(environment.Other, secondTrip),
        };
        var query = new ListIncomingBookingsRequest { PageSize = 2, PendingOnly = true };

        var firstPage = await environment.Driver.Bookings.ListIncomingBookingsAsync(query);
        var repeatedPage = await environment.Driver.Bookings.ListIncomingBookingsAsync(query);
        Assert.Equal(firstPage, repeatedPage);
        Assert.True(firstPage.HasNextOffset);
        Assert.Equal(2, firstPage.NextOffset);
        query.Offset = firstPage.NextOffset;
        var secondPage = await environment.Driver.Bookings.ListIncomingBookingsAsync(query);
        Assert.False(secondPage.HasNextOffset);
        var identifiers = firstPage.Entries.Concat(secondPage.Entries).Select(entry => entry.Booking.Id).ToArray();
        var expected = rows.OrderBy(booking => booking.TripId, StringComparer.Ordinal)
            .ThenBy(booking => booking.Id, StringComparer.Ordinal).Select(booking => booking.Id).ToArray();
        Assert.Equal(expected, identifiers);
        Assert.Equal(4, identifiers.Distinct().Count());
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(10001, 20)]
    [InlineData(0, -1)]
    [InlineData(0, 101)]
    public async Task IncomingPagination_RejectsInvalidBounds(int offset, int pageSize)
    {
        await using var environment = new TestEnvironment(fixture);

        var exception = await Assert.ThrowsAsync<RpcException>(() => environment.Driver.Bookings.ListIncomingBookingsAsync(
            new ListIncomingBookingsRequest { Offset = offset, PageSize = pageSize }).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task MyTrips_PrioritizesUpcomingActiveThenCancelledFutureThenRecentHistory()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var far = await environment.OfferAsync(environment.Driver, 40);
        var older = await environment.OfferAsync(environment.Driver, 5);
        var cancelledFar = await environment.OfferAsync(environment.Driver, 30);
        var recent = await environment.OfferAsync(environment.Driver, 10);
        var near = await environment.OfferAsync(environment.Driver, 20);
        var cancelledNear = await environment.OfferAsync(environment.Driver, 25);
        await environment.Driver.Trips.CancelTripAsync(new CancelTripRequest { TripId = cancelledFar.Id });
        await environment.Driver.Trips.CancelTripAsync(new CancelTripRequest { TripId = cancelledNear.Id });
        environment.Clock.Advance(TimeSpan.FromMinutes(15));

        var mine = await environment.Driver.Trips.ListMyTripsAsync(new ListMyTripsRequest { PageSize = 100 });

        Assert.Equal(new[] { near.Id, far.Id, cancelledNear.Id, cancelledFar.Id, recent.Id, older.Id },
            mine.Trips.Select(trip => trip.Id));
        var firstPage = await environment.Driver.Trips.ListMyTripsAsync(new ListMyTripsRequest { PageSize = 2 });
        Assert.Equal(new[] { near.Id, far.Id }, firstPage.Trips.Select(trip => trip.Id));
        Assert.Equal(2, firstPage.NextOffset);
        var search = await environment.Passenger.Trips.SearchTripsAsync(new SearchTripsRequest
        {
            Origin = environment.Origin,
            DepartureFrom = Timestamp.FromDateTimeOffset(environment.Clock.GetUtcNow()),
            DepartureUntil = Timestamp.FromDateTimeOffset(environment.Clock.GetUtcNow().AddHours(1)),
        });
        Assert.Equal(new[] { near.Id, far.Id }, search.Trips.Select(trip => trip.Id));
    }

    private sealed class TestEnvironment : IAsyncDisposable
    {
        public const string PassengerAddress = "Passenger Street 2, Test City";
        private readonly WebApplicationFactory<TripGrpcService> factory;
        public TestClock Clock { get; } = new(DateTimeOffset.UtcNow);
        public string Origin { get; } = $"Incoming start {Guid.NewGuid():N}";
        public Client Driver { get; }
        public Client Passenger { get; }
        public Client Other { get; }

        public TestEnvironment(GrpcApiFixture fixture)
        {
            factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            }));
            Driver = CreateClient();
            Passenger = CreateClient();
            Other = CreateClient();
        }

        public async Task SaveProfilesAsync()
        {
            foreach (var client in new[] { Driver, Passenger, Other })
            {
                await client.Profiles.UpdateMyProfileAsync(new UpdateMyProfileRequest
                {
                    DisplayName = "Incoming test colleague",
                    PickupAddress = PassengerAddress,
                    Vehicle = new Vehicle
                    {
                        Name = "Test vehicle",
                        PassengerSeats = 2,
                        EnergyKind = EnergyKind.Petrol,
                        ConsumptionPer100Km = "6.5",
                        UnitPriceEur = "1.8",
                    },
                });
            }
        }

        public async Task<Trip> OfferAsync(Client client, int minutesUntilDeparture) =>
            (await client.Trips.CreateTripAsync(new CreateTripRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                Origin = Origin,
                Destination = "Incoming office",
                DepartureAt = Timestamp.FromDateTimeOffset(Clock.GetUtcNow().AddMinutes(minutesUntilDeparture)),
                SeatsTotal = 2,
                DistanceKm = "25",
            })).Trip;

        public async Task<Booking> RequestAsync(Client client, Trip trip) =>
            (await client.Bookings.RequestSeatAsync(new RequestSeatRequest
            {
                TripId = trip.Id,
                RequestId = Guid.NewGuid().ToString("D"),
            })).Booking;

        public async ValueTask DisposeAsync()
        {
            Driver.Dispose();
            Passenger.Dispose();
            Other.Dispose();
            await factory.DisposeAsync();
        }

        private Client CreateClient()
        {
            var http = new HttpClient(new GrpcWebHandler(GrpcWebMode.GrpcWeb, factory.Server.CreateHandler()));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                GrpcApiFixture.CreateToken($"incoming-{Guid.NewGuid():N}"));
            return new Client(GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpClient = http,
                HttpVersion = HttpVersion.Version11,
                HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
                DisposeHttpClient = true,
            }));
        }
    }

    private sealed class Client(GrpcChannel channel) : IDisposable
    {
        public ProfileService.ProfileServiceClient Profiles { get; } = new(channel);
        public TripService.TripServiceClient Trips { get; } = new(channel);
        public BookingService.BookingServiceClient Bookings { get; } = new(channel);
        public BillingService.BillingServiceClient Billing { get; } = new(channel);

        public void Dispose() => channel.Dispose();
    }

    private sealed class TestClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset now = initial;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
