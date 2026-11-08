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

public sealed class DashboardTests(GrpcApiFixture fixture) : IClassFixture<GrpcApiFixture>
{
    [Fact]
    public async Task Actions_AreScopedToOwnerIssuerAndSubjectAndContainNoPickupAddresses()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var owned = await environment.OfferAsync(environment.Driver, 20);
        var foreign = await environment.OfferAsync(environment.Other, 15);
        var ownedRequest = await environment.RequestAsync(environment.Passenger, owned);
        var foreignRequest = await environment.RequestAsync(environment.Passenger, foreign);

        var driver = await environment.Driver.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest());

        Assert.Equal(1, driver.BookingRequestCount);
        Assert.Equal(0, driver.PaymentReportCount);
        Assert.Equal(ownedRequest.Id, Assert.Single(driver.Actions).BookingId);
        Assert.DoesNotContain(TestEnvironment.PickupAddress, driver.ToString());
        var other = await environment.Other.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest());
        Assert.Equal(foreignRequest.Id, Assert.Single(other.Actions).BookingId);
        var passenger = await environment.Passenger.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest());
        Assert.Equal(0, passenger.BookingRequestCount);
        Assert.Equal(0, passenger.PaymentReportCount);
        Assert.Empty(passenger.Actions);

        var sameSubjectOtherIssuer = environment.CreateClient(environment.Driver.Subject, GrpcApiFixture.OtherIssuer);
        var isolated = await sameSubjectOtherIssuer.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest());
        Assert.Equal(0, isolated.BookingRequestCount);
        Assert.Equal(0, isolated.PaymentReportCount);
        Assert.Empty(isolated.Actions);
    }

    [Fact]
    public async Task CountsIncludeEveryActionWhileEachPreviewIsLimitedToFive()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var completedTrips = new List<Trip>();
        for (var index = 0; index < 6; index++)
        {
            var trip = await environment.OfferAsync(environment.Driver, 1);
            var booking = await environment.RequestAsync(environment.Passenger, trip);
            await environment.Driver.Bookings.AcceptBookingAsync(new ChangeBookingRequest { BookingId = booking.Id });
            completedTrips.Add(trip);
        }

        environment.Clock.Advance(TimeSpan.FromMinutes(2));
        foreach (var trip in completedTrips)
        {
            await environment.CompleteAsync(trip);
            await environment.Passenger.Billing.ReportPaymentAsync(new ReportPaymentRequest
            {
                TripId = trip.Id,
                RequestId = Guid.NewGuid().ToString("D"),
            });
        }

        var pending = new List<(Trip Trip, Booking Booking)>();
        foreach (var minutes in new[] { 25, 20, 24, 21, 23, 22 })
        {
            var trip = await environment.OfferAsync(environment.Driver, minutes);
            pending.Add((trip, await environment.RequestAsync(environment.Passenger, trip)));
        }

        var response = await environment.Driver.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest());

        Assert.Equal(6, response.BookingRequestCount);
        Assert.Equal(6, response.PaymentReportCount);
        Assert.Equal(10, response.Actions.Count);
        var requests = response.Actions.Where(action => action.Kind == PendingActionKind.BookingRequest).ToArray();
        var payments = response.Actions.Where(action => action.Kind == PendingActionKind.PaymentReport).ToArray();
        Assert.Equal(pending.OrderBy(entry => entry.Trip.DepartureAt.Seconds).Take(5)
            .Select(entry => entry.Booking.Id), requests.Select(action => action.BookingId));
        Assert.Equal(completedTrips.Select(trip => trip.Id).Order(StringComparer.Ordinal).Take(5),
            payments.Select(action => action.TripId));
        Assert.All(payments, action =>
        {
            Assert.Empty(action.BookingId);
            Assert.True(action.AmountCents > 0);
        });
        Assert.DoesNotContain(TestEnvironment.PickupAddress, response.ToString());
    }

    [Fact]
    public async Task BookingActions_DisappearWhenProcessedWithdrawnCancelledOrDeparted()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var accepted = await environment.OfferAsync(environment.Driver, 20);
        var rejected = await environment.OfferAsync(environment.Driver, 20);
        var withdrawn = await environment.OfferAsync(environment.Driver, 20);
        var cancelled = await environment.OfferAsync(environment.Driver, 20);
        var departed = await environment.OfferAsync(environment.Driver, 1);
        var open = await environment.OfferAsync(environment.Driver, 30);
        var acceptedRequest = await environment.RequestAsync(environment.Passenger, accepted);
        var rejectedRequest = await environment.RequestAsync(environment.Passenger, rejected);
        var withdrawnRequest = await environment.RequestAsync(environment.Passenger, withdrawn);
        await environment.RequestAsync(environment.Passenger, cancelled);
        await environment.RequestAsync(environment.Passenger, departed);
        var openRequest = await environment.RequestAsync(environment.Passenger, open);
        var before = await environment.Driver.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest());
        Assert.Equal(6, before.BookingRequestCount);

        await environment.Driver.Bookings.AcceptBookingAsync(new ChangeBookingRequest { BookingId = acceptedRequest.Id });
        await environment.Driver.Bookings.RejectBookingAsync(new ChangeBookingRequest { BookingId = rejectedRequest.Id });
        await environment.Passenger.Bookings.WithdrawBookingAsync(new ChangeBookingRequest { BookingId = withdrawnRequest.Id });
        await environment.Driver.Trips.CancelTripAsync(new CancelTripRequest { TripId = cancelled.Id });
        environment.Clock.Advance(TimeSpan.FromMinutes(2));

        var after = await environment.Driver.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest());

        Assert.Equal(1, after.BookingRequestCount);
        Assert.Equal(openRequest.Id, Assert.Single(after.Actions).BookingId);
    }

    [Fact]
    public async Task PaymentReports_DisappearAfterConfirmationOrRejectionAndReappearAfterNewReport()
    {
        await using var environment = new TestEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var trip = await environment.OfferAsync(environment.Driver, 1);
        var passengerBooking = await environment.RequestAsync(environment.Passenger, trip);
        var otherBooking = await environment.RequestAsync(environment.Other, trip);
        await environment.Driver.Bookings.AcceptBookingAsync(new ChangeBookingRequest { BookingId = passengerBooking.Id });
        await environment.Driver.Bookings.AcceptBookingAsync(new ChangeBookingRequest { BookingId = otherBooking.Id });
        environment.Clock.Advance(TimeSpan.FromMinutes(2));
        await environment.CompleteAsync(trip);
        await environment.Passenger.Billing.ReportPaymentAsync(new ReportPaymentRequest
        {
            TripId = trip.Id,
            RequestId = Guid.NewGuid().ToString("D"),
        });
        var secondReport = new ReportPaymentRequest { TripId = trip.Id, RequestId = Guid.NewGuid().ToString("D") };
        await environment.Other.Billing.ReportPaymentAsync(secondReport);
        var reported = await environment.Driver.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest());
        Assert.Equal(2, reported.PaymentReportCount);

        await environment.Driver.Billing.ConfirmPaymentAsync(new ChangePaymentRequest
        {
            TripId = trip.Id,
            ProfileId = passengerBooking.RequesterProfileId,
        });
        var remaining = await environment.Driver.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest());
        Assert.Equal(1, remaining.PaymentReportCount);
        Assert.Equal(otherBooking.RequesterProfileId, Assert.Single(remaining.Actions).ProfileId);
        await environment.Driver.Billing.RejectPaymentAsync(new ChangePaymentRequest
        {
            TripId = trip.Id,
            ProfileId = otherBooking.RequesterProfileId,
        });
        var rejected = await environment.Driver.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest());
        Assert.Equal(0, rejected.PaymentReportCount);
        Assert.Empty(rejected.Actions);

        secondReport.RequestId = Guid.NewGuid().ToString("D");
        await environment.Other.Billing.ReportPaymentAsync(secondReport);
        Assert.Equal(1, (await environment.Driver.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest())).PaymentReportCount);
        var passenger = await environment.Passenger.Dashboard.GetPendingActionsAsync(new GetPendingActionsRequest());
        Assert.Equal(0, passenger.PaymentReportCount);
        Assert.Empty(passenger.Actions);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotReadDashboard()
    {
        await using var environment = new TestEnvironment(fixture);
        var anonymous = environment.CreateClient(authenticated: false);

        var failure = await Assert.ThrowsAsync<RpcException>(() => anonymous.Dashboard
            .GetPendingActionsAsync(new GetPendingActionsRequest()).ResponseAsync);

        Assert.Equal(StatusCode.Unauthenticated, failure.StatusCode);
    }

    private sealed class TestEnvironment : IAsyncDisposable
    {
        public const string PickupAddress = "Private Pickup Street 999, Test City";
        private readonly WebApplicationFactory<TripGrpcService> factory;
        private readonly List<Client> clients = [];
        public TestClock Clock { get; } = new(DateTimeOffset.UtcNow);
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
                    DisplayName = "Dashboard test colleague",
                    PickupAddress = PickupAddress,
                    Vehicle = new Vehicle
                    {
                        Name = "Dashboard vehicle",
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
                Origin = $"Dashboard route {Guid.NewGuid():N}",
                Destination = "Dashboard office",
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

        public async Task CompleteAsync(Trip trip) => await Driver.Billing.CompleteTripAsync(new CompleteTripRequest
        {
            TripId = trip.Id,
            DistanceKm = "25",
            ConsumptionPer100Km = "6.5",
            UnitPriceEur = "1.8",
        });

        public Client CreateClient(string? subject = null, string issuer = GrpcApiFixture.Issuer, bool authenticated = true)
        {
            subject ??= $"dashboard-{Guid.NewGuid():N}";
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
        public ProfileService.ProfileServiceClient Profiles { get; } = new(channel);
        public TripService.TripServiceClient Trips { get; } = new(channel);
        public BookingService.BookingServiceClient Bookings { get; } = new(channel);
        public BillingService.BillingServiceClient Billing { get; } = new(channel);
        public DashboardService.DashboardServiceClient Dashboard { get; } = new(channel);

        public void Dispose() => channel.Dispose();
    }

    private sealed class TestClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset now = initial;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
