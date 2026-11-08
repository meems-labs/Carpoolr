using System.Net;
using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Grpc;
using Carpoolr.Api.Security;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Carpoolr.Api.Tests;

// The staging simulation deliberately shares three fixed profiles. Isolate these
// flows from parallel collections so profile changes cannot interfere with them.
[CollectionDefinition("Staging application flows", DisableParallelization = true)]
public sealed class StagingApplicationFlowCollection
{
}

[Collection("Staging application flows")]
public sealed class ApplicationFlowTests(GrpcApiFixture fixture) : IClassFixture<GrpcApiFixture>
{
    [Fact]
    public async Task ProfilesAndBookings_PreserveCapacityPrivacyAndAllowReapplication()
    {
        await using var environment = new FlowEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var trip = await environment.OfferAsync(seats: 2);

        var first = await environment.Passenger.Bookings.RequestSeatAsync(NewSeatRequest(trip.Id));
        var second = await environment.OtherPassenger.Bookings.RequestSeatAsync(NewSeatRequest(trip.Id));
        var pending = await environment.Driver.Trips.GetTripAsync(new GetTripRequest { TripId = trip.Id });

        Assert.Equal(2, pending.Trip.AvailableSeats);
        Assert.Equal(2, pending.Bookings.Count);
        Assert.All(pending.Bookings, booking =>
        {
            Assert.Equal(BookingState.Pending, booking.State);
            Assert.Empty(booking.PickupAddress);
        });

        var accepted = await environment.Driver.Bookings.AcceptBookingAsync(Change(first.Booking.Id));
        Assert.Equal(BookingState.Accepted, accepted.Booking.State);
        Assert.Equal(FlowEnvironment.PassengerAddress, accepted.Booking.PickupAddress);
        var rejected = await environment.Driver.Bookings.RejectBookingAsync(Change(second.Booking.Id));
        Assert.Equal(BookingState.Rejected, rejected.Booking.State);

        var driverView = await environment.Driver.Trips.GetTripAsync(new GetTripRequest { TripId = trip.Id });
        Assert.Equal(1, driverView.Trip.AvailableSeats);
        Assert.Contains(driverView.RouteStops, stop => stop.Address == FlowEnvironment.PassengerAddress);
        Assert.DoesNotContain(driverView.RouteStops, stop => stop.Address == FlowEnvironment.OtherPassengerAddress);
        Assert.Contains(Uri.EscapeDataString(FlowEnvironment.PassengerAddress), driverView.RouteUrl);

        var otherView = await environment.OtherPassenger.Trips.GetTripAsync(new GetTripRequest { TripId = trip.Id });
        Assert.Single(otherView.Bookings);
        Assert.Equal(second.Booking.Id, otherView.Bookings[0].Id);
        Assert.Empty(otherView.RouteStops);
        Assert.Empty(otherView.RouteUrl);

        var withdrawn = await environment.Passenger.Bookings.WithdrawBookingAsync(Change(first.Booking.Id));
        Assert.Equal(BookingState.Withdrawn, withdrawn.Booking.State);
        var retry = await environment.OtherPassenger.Bookings.RequestSeatAsync(NewSeatRequest(trip.Id));
        Assert.NotEqual(second.Booking.Id, retry.Booking.Id);
        Assert.Equal(BookingState.Pending, retry.Booking.State);
        await environment.Driver.Bookings.AcceptBookingAsync(Change(retry.Booking.Id));

        var final = await environment.Driver.Trips.GetTripAsync(new GetTripRequest { TripId = trip.Id });
        Assert.Equal(1, final.Trip.AvailableSeats);
        Assert.DoesNotContain(final.RouteStops, stop => stop.Address == FlowEnvironment.PassengerAddress);
        Assert.Contains(final.RouteStops, stop => stop.Address == FlowEnvironment.OtherPassengerAddress);
    }

    [Fact]
    public async Task ConcurrentAcceptance_CannotOverbookTheLastSeat()
    {
        await using var environment = new FlowEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var trip = await environment.OfferAsync(seats: 1);
        var first = await environment.Passenger.Bookings.RequestSeatAsync(NewSeatRequest(trip.Id));
        var second = await environment.OtherPassenger.Bookings.RequestSeatAsync(NewSeatRequest(trip.Id));

        var outcomes = await Task.WhenAll(
            AcceptStatusAsync(environment.Driver, first.Booking.Id),
            AcceptStatusAsync(environment.Driver, second.Booking.Id));

        Assert.Single(outcomes, status => status == StatusCode.OK);
        Assert.Single(outcomes, status => status == StatusCode.FailedPrecondition);
        var details = await environment.Driver.Trips.GetTripAsync(new GetTripRequest { TripId = trip.Id });
        Assert.Equal(0, details.Trip.AvailableSeats);
        Assert.Single(details.Bookings, booking => booking.State == BookingState.Accepted);
        Assert.Single(details.Bookings, booking => booking.State == BookingState.Pending);
    }

    [Fact]
    public async Task CompletedBill_IsCentExactImmutablePrivateAndManuallyConfirmed()
    {
        await using var environment = new FlowEnvironment(fixture);
        await environment.SaveProfilesAsync(consumption: "5", unitPrice: "2.01");
        var trip = await environment.OfferAsync(seats: 2, distanceKm: "10");
        var first = await environment.Passenger.Bookings.RequestSeatAsync(NewSeatRequest(trip.Id));
        var second = await environment.OtherPassenger.Bookings.RequestSeatAsync(NewSeatRequest(trip.Id));
        await environment.Driver.Bookings.AcceptBookingAsync(Change(first.Booking.Id));
        await environment.Driver.Bookings.AcceptBookingAsync(Change(second.Booking.Id));
        var preview = await environment.Driver.Trips.GetTripAsync(new GetTripRequest { TripId = trip.Id });
        Assert.Equal(101, preview.Trip.CostPreview.TotalCents);
        Assert.Equal(3, preview.Trip.CostPreview.PersonCount);
        var completion = new CompleteTripRequest
        {
            TripId = trip.Id,
            DistanceKm = "10",
            ConsumptionPer100Km = "5",
            UnitPriceEur = "2.01",
        };
        await AssertStatusAsync(StatusCode.FailedPrecondition,
            () => environment.Driver.Billing.CompleteTripAsync(completion).ResponseAsync);

        environment.Clock.Advance(TimeSpan.FromHours(2));
        await AssertStatusAsync(StatusCode.PermissionDenied,
            () => environment.Passenger.Billing.CompleteTripAsync(completion).ResponseAsync);
        var completed = await environment.Driver.Billing.CompleteTripAsync(completion);
        Assert.True(completed.Bill.IsDriver);
        Assert.Equal(101, completed.Bill.TotalCents);
        Assert.Equal(3, completed.Bill.Shares.Count);
        Assert.Equal(completed.Bill.TotalCents, completed.Bill.Shares.Sum(share => share.AmountCents));
        Assert.InRange(completed.Bill.Shares.Max(share => share.AmountCents) -
            completed.Bill.Shares.Min(share => share.AmountCents), 0, 1);
        Assert.Equal(34, Assert.Single(completed.Bill.Shares, share => share.IsMine).AmountCents);
        Assert.Equal(PaymentState.DriverShare, Assert.Single(completed.Bill.Shares, share => share.IsMine).PaymentState);
        Assert.Equal(completed.Bill, (await environment.Driver.Billing.CompleteTripAsync(completion)).Bill);

        var changedCompletion = completion.Clone();
        changedCompletion.DistanceKm = "11";
        await AssertStatusAsync(StatusCode.AlreadyExists,
            () => environment.Driver.Billing.CompleteTripAsync(changedCompletion).ResponseAsync);

        var passengerBill = await environment.Passenger.Billing.GetBillAsync(new GetBillRequest { TripId = trip.Id });
        var passengerShare = Assert.Single(passengerBill.Bill.Shares);
        Assert.True(passengerShare.IsMine);
        Assert.False(passengerBill.Bill.IsDriver);
        var otherBill = await environment.OtherPassenger.Billing.GetBillAsync(new GetBillRequest { TripId = trip.Id });
        var otherShare = Assert.Single(otherBill.Bill.Shares);

        await environment.Passenger.Profiles.UpdateMyProfileAsync(new UpdateMyProfileRequest
        {
            DisplayName = "Changed passenger name",
            PickupAddress = "Changed address 99, Test City",
        });
        await environment.Driver.Profiles.UpdateMyProfileAsync(new UpdateMyProfileRequest
        {
            DisplayName = "Changed driver name",
            PickupAddress = FlowEnvironment.DriverAddress,
            Vehicle = FlowEnvironment.Vehicle("100", "99"),
        });
        var afterProfileEdit = await environment.Driver.Billing.GetBillAsync(new GetBillRequest { TripId = trip.Id });
        Assert.Equal(completed.Bill, afterProfileEdit.Bill);

        var report = new ReportPaymentRequest { TripId = trip.Id, RequestId = Guid.NewGuid().ToString("D") };
        var reported = await environment.Passenger.Billing.ReportPaymentAsync(report);
        Assert.Equal(PaymentState.Reported, Assert.Single(reported.Bill.Shares).PaymentState);
        var firstPayment = new ChangePaymentRequest { TripId = trip.Id, ProfileId = passengerShare.ProfileId };
        await AssertStatusAsync(StatusCode.PermissionDenied,
            () => environment.OtherPassenger.Billing.ConfirmPaymentAsync(firstPayment).ResponseAsync);
        var confirmed = await environment.Driver.Billing.ConfirmPaymentAsync(firstPayment);
        Assert.Equal(PaymentState.Confirmed,
            Assert.Single(confirmed.Bill.Shares, share => share.ProfileId == passengerShare.ProfileId).PaymentState);
        Assert.Equal(confirmed.Bill, (await environment.Driver.Billing.ConfirmPaymentAsync(firstPayment)).Bill);
        Assert.Equal(PaymentState.Confirmed,
            Assert.Single((await environment.Passenger.Billing.ReportPaymentAsync(report)).Bill.Shares).PaymentState);

        var otherReport = new ReportPaymentRequest { TripId = trip.Id, RequestId = Guid.NewGuid().ToString("D") };
        await environment.OtherPassenger.Billing.ReportPaymentAsync(otherReport);
        var otherPayment = new ChangePaymentRequest { TripId = trip.Id, ProfileId = otherShare.ProfileId };
        await environment.Driver.Billing.RejectPaymentAsync(otherPayment);
        Assert.Equal(PaymentState.Rejected,
            Assert.Single((await environment.OtherPassenger.Billing.ReportPaymentAsync(otherReport)).Bill.Shares).PaymentState);
        otherReport.RequestId = Guid.NewGuid().ToString("D");
        Assert.Equal(PaymentState.Reported,
            Assert.Single((await environment.OtherPassenger.Billing.ReportPaymentAsync(otherReport)).Bill.Shares).PaymentState);
        await environment.Driver.Billing.ConfirmPaymentAsync(otherPayment);
        var finalBill = await environment.Driver.Billing.GetBillAsync(new GetBillRequest { TripId = trip.Id });
        Assert.Equal(2, finalBill.Bill.Shares.Count(share => share.PaymentState == PaymentState.Confirmed));
        Assert.Equal(101, finalBill.Bill.TotalCents);
    }

    [Fact]
    public async Task WeeklySeries_UsesBerlinTimeAndSupportsSingleAndFutureCancellation()
    {
        await using var environment = new FlowEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var request = new CreateSeriesRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Origin = $"Series start {Guid.NewGuid():N}",
            Destination = "Series office",
            SeatsTotal = 2,
            DistanceKm = "25",
            StartDate = "2030-01-14",
            EndDate = "2030-01-28",
            LocalDepartureTime = "07:30",
        };
        request.Weekdays.AddRange([1, 3]);
        var series = await environment.Driver.Series.CreateSeriesAsync(request);
        Assert.Equal("Europe/Berlin", series.Series.TimeZone);
        var mine = await environment.Driver.Trips.ListMyTripsAsync(new ListMyTripsRequest { PageSize = 100 });
        var occurrences = mine.Trips.Where(trip => trip.SeriesId == series.Series.Id)
            .OrderBy(trip => trip.DepartureAt.Seconds).ToArray();
        Assert.Equal(5, occurrences.Length);
        Assert.All(occurrences, occurrence =>
        {
            var utc = occurrence.DepartureAt.ToDateTimeOffset();
            Assert.Equal(6, utc.Hour);
            Assert.Equal(30, utc.Minute);
            Assert.Contains(utc.DayOfWeek, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday });
        });

        await environment.Driver.Trips.CancelTripAsync(new CancelTripRequest { TripId = occurrences[1].Id });
        var unaffected = await environment.Driver.Trips.GetTripAsync(new GetTripRequest { TripId = occurrences[2].Id });
        Assert.Equal(TripState.Planned, unaffected.Trip.State);
        var seriesBefore = await environment.Driver.Series.ListMySeriesAsync(new ListMySeriesRequest { PageSize = 100 });
        Assert.False(Assert.Single(seriesBefore.Series, value => value.Id == series.Series.Id).IsCancelled);

        environment.Clock.Set(new DateTimeOffset(2030, 1, 14, 8, 0, 0, TimeSpan.Zero));
        await environment.Driver.Series.CancelSeriesAsync(new CancelSeriesRequest { SeriesId = series.Series.Id });
        var final = await environment.Driver.Trips.ListMyTripsAsync(new ListMyTripsRequest { PageSize = 100 });
        var finalOccurrences = final.Trips.Where(trip => trip.SeriesId == series.Series.Id).ToArray();
        Assert.Equal(TripState.Planned, Assert.Single(finalOccurrences, trip => trip.Id == occurrences[0].Id).State);
        Assert.All(finalOccurrences.Where(trip => trip.Id != occurrences[0].Id),
            trip => Assert.Equal(TripState.Cancelled, trip.State));
        var finalSeries = await environment.Driver.Series.ListMySeriesAsync(new ListMySeriesRequest { PageSize = 100 });
        Assert.True(Assert.Single(finalSeries.Series, value => value.Id == series.Series.Id).IsCancelled);
    }

    [Theory]
    [InlineData("2030-03-31")]
    [InlineData("2030-10-27")]
    public async Task RecurringDepartureInClockGapOrOverlap_IsRejectedWithoutPartialSeries(string date)
    {
        await using var environment = new FlowEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var request = new CreateSeriesRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Origin = $"DST start {Guid.NewGuid():N}",
            Destination = "DST office",
            SeatsTotal = 2,
            DistanceKm = "25",
            StartDate = date,
            EndDate = date,
            LocalDepartureTime = "02:30",
        };
        request.Weekdays.Add(7);

        await AssertStatusAsync(StatusCode.InvalidArgument,
            () => environment.Driver.Series.CreateSeriesAsync(request).ResponseAsync);

        var series = await environment.Driver.Series.ListMySeriesAsync(new ListMySeriesRequest { PageSize = 100 });
        Assert.DoesNotContain(series.Series, value => value.Origin == request.Origin);
    }

    [Fact]
    public async Task DriverOnlyBill_DeniesNonParticipantsAndNeedsNoPaymentReport()
    {
        await using var environment = new FlowEnvironment(fixture);
        await environment.SaveProfilesAsync();
        var trip = await environment.OfferAsync(seats: 1, distanceKm: "25");
        environment.Clock.Advance(TimeSpan.FromHours(2));
        var bill = await environment.Driver.Billing.CompleteTripAsync(new CompleteTripRequest
        {
            TripId = trip.Id,
            DistanceKm = "25",
            ConsumptionPer100Km = "6.5",
            UnitPriceEur = "1.8",
        });

        var share = Assert.Single(bill.Bill.Shares);
        Assert.Equal(bill.Bill.TotalCents, share.AmountCents);
        Assert.Equal(PaymentState.DriverShare, share.PaymentState);
        Assert.False(share.CanReport);
        await AssertStatusAsync(StatusCode.PermissionDenied,
            () => environment.Passenger.Billing.GetBillAsync(new GetBillRequest { TripId = trip.Id }).ResponseAsync);
        await AssertStatusAsync(StatusCode.PermissionDenied,
            () => environment.Driver.Billing.ReportPaymentAsync(new ReportPaymentRequest
            {
                TripId = trip.Id,
                RequestId = Guid.NewGuid().ToString("D"),
            }).ResponseAsync);
    }

    private static RequestSeatRequest NewSeatRequest(string tripId) =>
        new() { TripId = tripId, RequestId = Guid.NewGuid().ToString("D") };

    private static ChangeBookingRequest Change(string bookingId) => new() { BookingId = bookingId };

    private static async Task<StatusCode> AcceptStatusAsync(FlowClient client, string bookingId)
    {
        try
        {
            await client.Bookings.AcceptBookingAsync(Change(bookingId));
            return StatusCode.OK;
        }
        catch (RpcException exception)
        {
            return exception.StatusCode;
        }
    }

    private static async Task AssertStatusAsync<T>(StatusCode expected, Func<Task<T>> call)
    {
        var exception = await Assert.ThrowsAsync<RpcException>(async () => await call());
        Assert.Equal(expected, exception.StatusCode);
    }

    private sealed class FlowEnvironment : IAsyncDisposable
    {
        public const string DriverAddress = "Driver Road 1, Test City";
        public const string PassengerAddress = "Passenger Road 2, Test City";
        public const string OtherPassengerAddress = "Other Passenger Road 3, Test City";
        private readonly WebApplicationFactory<TripGrpcService> factory;
        public MutableClock Clock { get; } = new(new DateTimeOffset(2030, 1, 7, 8, 0, 0, TimeSpan.Zero));
        public FlowClient Driver { get; }
        public FlowClient Passenger { get; }
        public FlowClient OtherPassenger { get; }

        public FlowEnvironment(GrpcApiFixture fixture)
        {
            factory = fixture.Factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Staging");
                builder.UseSetting("DemoMode:Enabled", "true");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(Clock);
                });
            });
            Driver = CreateClient(DemoUserCatalog.Users[0].Id);
            Passenger = CreateClient(DemoUserCatalog.Users[1].Id);
            OtherPassenger = CreateClient(DemoUserCatalog.Users[2].Id);
        }

        public async Task SaveProfilesAsync(string consumption = "6.5", string unitPrice = "1.8")
        {
            await Driver.Profiles.UpdateMyProfileAsync(new UpdateMyProfileRequest
            {
                DisplayName = "Driver snapshot",
                PickupAddress = DriverAddress,
                Vehicle = Vehicle(consumption, unitPrice),
            });
            await Passenger.Profiles.UpdateMyProfileAsync(new UpdateMyProfileRequest
            {
                DisplayName = "Passenger snapshot",
                PickupAddress = PassengerAddress,
            });
            await OtherPassenger.Profiles.UpdateMyProfileAsync(new UpdateMyProfileRequest
            {
                DisplayName = "Other passenger snapshot",
                PickupAddress = OtherPassengerAddress,
            });
        }

        public async Task<Trip> OfferAsync(int seats, string distanceKm = "25") =>
            (await Driver.Trips.CreateTripAsync(new CreateTripRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                Origin = $"Trip start {Guid.NewGuid():N}",
                Destination = "Trip office",
                DepartureAt = Timestamp.FromDateTimeOffset(Clock.GetUtcNow().AddHours(1)),
                SeatsTotal = seats,
                DistanceKm = distanceKm,
            })).Trip;

        public static Vehicle Vehicle(string consumption, string unitPrice) => new()
        {
            Name = "Test vehicle",
            PassengerSeats = 2,
            EnergyKind = EnergyKind.Petrol,
            ConsumptionPer100Km = consumption,
            UnitPriceEur = unitPrice,
        };

        public async ValueTask DisposeAsync()
        {
            Driver.Dispose();
            Passenger.Dispose();
            OtherPassenger.Dispose();
            await factory.DisposeAsync();
        }

        private FlowClient CreateClient(Guid personId)
        {
            var http = new HttpClient(new GrpcWebHandler(GrpcWebMode.GrpcWeb, factory.Server.CreateHandler()));
            http.DefaultRequestHeaders.Add(DemoUserCatalog.HeaderName, personId.ToString("D"));
            return new FlowClient(GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpClient = http,
                HttpVersion = HttpVersion.Version11,
                HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
                DisposeHttpClient = true,
            }));
        }
    }

    private sealed class FlowClient(GrpcChannel channel) : IDisposable
    {
        public TripService.TripServiceClient Trips { get; } = new(channel);
        public ProfileService.ProfileServiceClient Profiles { get; } = new(channel);
        public BookingService.BookingServiceClient Bookings { get; } = new(channel);
        public RecurringTripService.RecurringTripServiceClient Series { get; } = new(channel);
        public BillingService.BillingServiceClient Billing { get; } = new(channel);

        public void Dispose() => channel.Dispose();
    }

    private sealed class MutableClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset current = initial;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan elapsed) => current += elapsed;
        public void Set(DateTimeOffset value) => current = value;
    }
}
