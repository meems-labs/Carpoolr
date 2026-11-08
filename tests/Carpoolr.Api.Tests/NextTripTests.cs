using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Data;
using Carpoolr.Api.Grpc;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Carpoolr.Api.Tests;

public sealed class NextTripTests(GrpcApiFixture fixture) : IClassFixture<GrpcApiFixture>
{
    [Fact]
    public async Task NextTrip_ChoosesEarliestAcrossDriverAndConfirmedPassengerRolesInOneQuery()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var own = await environment.OfferAsync(environment.Actor, 40);
        var shared = await environment.OfferAsync(environment.Other, 20);
        var mine = await environment.AcceptAsync(environment.Other, environment.Actor, shared);
        await environment.AcceptAsync(environment.Other, environment.Passenger, shared);
        environment.Queries.Reset();

        var response = await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest());

        Assert.Equal(1, environment.Queries.ReadCount);
        Assert.NotNull(response.Trip);
        Assert.Equal(shared.Id, response.Trip.Id);
        Assert.False(response.Trip.IsOwner);
        Assert.Equal(mine.Id, response.Trip.MyBooking.Id);
        Assert.Equal(BookingState.Accepted, response.Trip.MyBooking.State);
        Assert.Equal(environment.Actor.Address, response.Trip.MyBooking.PickupAddress);
        Assert.DoesNotContain(environment.Passenger.Address, response.ToString());
        Assert.Equal(0, response.Trip.AvailableSeats);
        Assert.Equal(3, response.Trip.CostPreview.PersonCount);
        Assert.NotEmpty(response.Trip.DriverDisplayName);

        await environment.Actor.Bookings.WithdrawBookingAsync(new ChangeBookingRequest { BookingId = mine.Id });
        var next = await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest());
        Assert.Equal(own.Id, next.Trip.Id);
        Assert.True(next.Trip.IsOwner);
        Assert.Null(next.Trip.MyBooking);
    }

    [Fact]
    public async Task PendingRejectedAndWithdrawnRequests_DoNotBecomeTheNextTrip()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var pending = await environment.OfferAsync(environment.Other, 5);
        var rejected = await environment.OfferAsync(environment.Other, 10);
        var withdrawn = await environment.OfferAsync(environment.Other, 15);
        var own = await environment.OfferAsync(environment.Actor, 30);
        await environment.RequestAsync(environment.Actor, pending);
        var rejection = await environment.RequestAsync(environment.Actor, rejected);
        await environment.Other.Bookings.RejectBookingAsync(new ChangeBookingRequest { BookingId = rejection.Id });
        var withdrawal = await environment.AcceptAsync(environment.Other, environment.Actor, withdrawn);
        await environment.Actor.Bookings.WithdrawBookingAsync(new ChangeBookingRequest { BookingId = withdrawal.Id });

        var response = await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest());

        Assert.Equal(own.Id, response.Trip.Id);
        await environment.Actor.Trips.CancelTripAsync(new CancelTripRequest { TripId = own.Id });
        Assert.Null((await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest())).Trip);
    }

    [Fact]
    public async Task CancelledAndCompletedTrips_AreExcludedEvenIfTheirDepartureIsInTheFuture()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var ownCompleted = await environment.OfferAsync(environment.Actor, 1);
        var sharedCompleted = await environment.OfferAsync(environment.Other, 2);
        await environment.AcceptAsync(environment.Other, environment.Actor, sharedCompleted);
        var ownCancelled = await environment.OfferAsync(environment.Actor, 5);
        var sharedCancelled = await environment.OfferAsync(environment.Other, 6);
        await environment.AcceptAsync(environment.Other, environment.Actor, sharedCancelled);
        await environment.Actor.Trips.CancelTripAsync(new CancelTripRequest { TripId = ownCancelled.Id });
        await environment.Other.Trips.CancelTripAsync(new CancelTripRequest { TripId = sharedCancelled.Id });
        var upcoming = await environment.OfferAsync(environment.Actor, 20);
        environment.Clock.Advance(TimeSpan.FromMinutes(3));
        await environment.CompleteAsync(environment.Actor, ownCompleted);
        await environment.CompleteAsync(environment.Other, sharedCompleted);
        // A clock correction must never make an already completed trip upcoming again.
        environment.Clock.Reset();

        var response = await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest());

        Assert.Equal(upcoming.Id, response.Trip.Id);
        Assert.Null((await environment.Other.Dashboard.GetNextTripAsync(new GetNextTripRequest())).Trip);
    }

    [Fact]
    public async Task DepartedTrips_AreExcludedAndDepartureAtNowIsNotUpcoming()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        await environment.OfferAsync(environment.Actor, 1);
        var departedShared = await environment.OfferAsync(environment.Other, 2);
        await environment.AcceptAsync(environment.Other, environment.Actor, departedShared);
        var nextShared = await environment.OfferAsync(environment.Other, 10);
        await environment.AcceptAsync(environment.Other, environment.Actor, nextShared);
        var own = await environment.OfferAsync(environment.Actor, 20);
        environment.Clock.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal(nextShared.Id, (await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest())).Trip.Id);
        environment.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(own.Id, (await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest())).Trip.Id);
        environment.Clock.Advance(TimeSpan.FromMinutes(7));
        Assert.Null((await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest())).Trip);
    }

    [Fact]
    public async Task EqualDepartures_HaveStableTripIdOrderingAcrossRoles()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var own = await environment.OfferAsync(environment.Actor, 30);
        var shared = await environment.OfferAsync(environment.Other, 30);
        await environment.AcceptAsync(environment.Other, environment.Actor, shared);
        var expected = new[] { own.Id, shared.Id }.Order(StringComparer.Ordinal).First();

        var first = await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest());
        var repeated = await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest());

        Assert.Equal(expected, first.Trip.Id);
        Assert.Equal(first, repeated);
    }

    [Fact]
    public async Task IdentityMatching_UsesBothIssuerAndSubjectForOwnershipAndMembership()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        await environment.OfferAsync(environment.Actor, 30);
        var shared = await environment.OfferAsync(environment.Other, 10);
        await environment.AcceptAsync(environment.Other, environment.Actor, shared);
        var sameSubject = environment.CreateClient(environment.Actor.Subject, GrpcApiFixture.OtherIssuer);

        Assert.Null((await sameSubject.Dashboard.GetNextTripAsync(new GetNextTripRequest())).Trip);
        Assert.Null((await environment.Passenger.Dashboard.GetNextTripAsync(new GetNextTripRequest())).Trip);
        Assert.Equal(shared.Id, (await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest())).Trip.Id);
    }

    [Fact]
    public async Task NextTrip_IsIndependentOfTheFirstPageOfPassengerRequests()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var confirmed = await environment.OfferAsync(environment.Other, 10);
        var accepted = await environment.AcceptAsync(environment.Other, environment.Actor, confirmed);
        for (var index = 0; index < 21; index++)
        {
            environment.Clock.Advance(TimeSpan.FromSeconds(1));
            var pending = await environment.OfferAsync(environment.Other, 60 + index);
            await environment.RequestAsync(environment.Actor, pending);
        }
        var firstPage = await environment.Actor.Bookings.ListMyBookingsAsync(new ListMyBookingsRequest { PageSize = 20 });
        Assert.DoesNotContain(firstPage.Entries, entry => entry.Booking.Id == accepted.Id);
        Assert.True(firstPage.HasNextOffset);

        var next = await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest());

        Assert.Equal(confirmed.Id, next.Trip.Id);
    }

    [Fact]
    public async Task PersonWithoutEligibleTrips_ReceivesAnEmptyResponse()
    {
        await using var environment = new TestEnvironment(fixture);

        var response = await environment.Actor.Dashboard.GetNextTripAsync(new GetNextTripRequest());

        Assert.Null(response.Trip);
    }

    [Fact]
    public async Task AnonymousCaller_CannotReadNextTrip()
    {
        await using var environment = new TestEnvironment(fixture);
        var anonymous = environment.CreateClient(authenticated: false);

        var failure = await Assert.ThrowsAsync<RpcException>(() => anonymous.Dashboard
            .GetNextTripAsync(new GetNextTripRequest()).ResponseAsync);

        Assert.Equal(StatusCode.Unauthenticated, failure.StatusCode);
    }

    private sealed class TestEnvironment : IAsyncDisposable
    {
        private readonly WebApplicationFactory<TripGrpcService> factory;
        private readonly List<Client> clients = [];
        public TestClock Clock { get; } = new(DateTimeOffset.UtcNow);
        public ReadCounter Queries { get; } = new();
        public Client Actor { get; }
        public Client Passenger { get; }
        public Client Other { get; }

        public TestEnvironment(GrpcApiFixture fixture)
        {
            factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddDbContext<CarpoolrDbContext>(options => options.AddInterceptors(Queries));
            }));
            Actor = CreateClient();
            Passenger = CreateClient();
            Other = CreateClient();
        }

        public async Task SaveProfilesAsync()
        {
            foreach (var client in new[] { Actor, Passenger, Other })
            {
                await client.Profiles.UpdateMyProfileAsync(new UpdateMyProfileRequest
                {
                    DisplayName = "Next trip colleague",
                    PickupAddress = client.Address,
                    Vehicle = new Vehicle
                    {
                        Name = "Next trip vehicle",
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
                Origin = $"Next trip origin {Guid.NewGuid():N}",
                Destination = "Next trip office",
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

        public async Task<Booking> AcceptAsync(Client driver, Client passenger, Trip trip)
        {
            var request = await RequestAsync(passenger, trip);
            return (await driver.Bookings.AcceptBookingAsync(new ChangeBookingRequest { BookingId = request.Id })).Booking;
        }

        public async Task CompleteAsync(Client driver, Trip trip) => await driver.Billing.CompleteTripAsync(new CompleteTripRequest
        {
            TripId = trip.Id,
            DistanceKm = "25",
            ConsumptionPer100Km = "6.5",
            UnitPriceEur = "1.8",
        });

        public Client CreateClient(string? subject = null, string issuer = GrpcApiFixture.Issuer, bool authenticated = true)
        {
            subject ??= $"next-trip-{Guid.NewGuid():N}";
            var http = new HttpClient(new GrpcWebHandler(GrpcWebMode.GrpcWeb, factory.Server.CreateHandler()));
            if (authenticated)
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                    GrpcApiFixture.CreateToken(subject, issuer));
            }
            var client = new Client(subject, GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpClient = http,
                HttpVersion = HttpVersion.Version11,
                HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
                DisposeHttpClient = true,
            }));
            clients.Add(client);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in clients) client.Dispose();
            await factory.DisposeAsync();
        }
    }

    private sealed class Client(string subject, GrpcChannel channel) : IDisposable
    {
        public string Subject { get; } = subject;
        public string Address { get; } = $"Private stop {subject}, Test City";
        public ProfileService.ProfileServiceClient Profiles { get; } = new(channel);
        public TripService.TripServiceClient Trips { get; } = new(channel);
        public BookingService.BookingServiceClient Bookings { get; } = new(channel);
        public BillingService.BillingServiceClient Billing { get; } = new(channel);
        public DashboardService.DashboardServiceClient Dashboard { get; } = new(channel);

        public void Dispose() => channel.Dispose();
    }

    private sealed class TestClock : TimeProvider
    {
        private readonly DateTimeOffset initial;
        private DateTimeOffset now;

        public TestClock(DateTimeOffset value)
        {
            initial = new DateTimeOffset(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
            now = initial;
        }

        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
        public void Reset() => now = initial;
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        private int count;
        public int ReadCount => Volatile.Read(ref count);
        public void Reset() => Interlocked.Exchange(ref count, 0);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return ValueTask.FromResult(result);
        }
    }
}
