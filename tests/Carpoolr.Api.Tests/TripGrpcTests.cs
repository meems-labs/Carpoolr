using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Data;
using TripApplicationService = Carpoolr.Api.Features.Trips.TripApplicationService;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Carpoolr.Api.Tests;

public sealed class TripGrpcTests(GrpcApiFixture fixture) : IClassFixture<GrpcApiFixture>
{
    [Fact]
    public async Task CreateAndGet_ReturnPersistedTripWithoutExposingOwnerClaims()
    {
        using var driver = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));
        var request = NewTrip();

        var created = await CreateTripWithProfileAsync(driver, request);
        using var colleague = fixture.CreateClient(GrpcApiFixture.CreateToken("colleague"));
        var fetched = await colleague.Trips.GetTripAsync(new GetTripRequest { TripId = created.Trip.Id });

        Assert.True(Guid.TryParse(created.Trip.Id, out _));
        Assert.Equal(request.Origin, fetched.Trip.Origin);
        Assert.Equal(request.Destination, fetched.Trip.Destination);
        Assert.Equal(request.DepartureAt, fetched.Trip.DepartureAt);
        Assert.Equal(request.SeatsTotal, fetched.Trip.SeatsTotal);
        Assert.Equal(TripState.Planned, fetched.Trip.State);
        Assert.NotNull(fetched.Trip.CreatedAt);
        Assert.DoesNotContain(Trip.Descriptor.Fields.InDeclarationOrder(),
            field => field.Name is "owner_issuer" or "owner_subject");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    [InlineData("forged-signature")]
    [InlineData("missing-issuer")]
    public async Task UntrustedIdentity_IsRejected(string tokenKind)
    {
        var token = tokenKind switch
        {
            "missing" => null,
            "malformed" => "not-a-jwt",
            "expired" => GrpcApiFixture.CreateToken(expired: true),
            "wrong-audience" => GrpcApiFixture.CreateToken(audience: "another-api"),
            "wrong-issuer" => GrpcApiFixture.CreateToken(issuer: "https://untrusted.test"),
            "forged-signature" => GrpcApiFixture.CreateToken(invalidSignature: true),
            "missing-issuer" => GrpcApiFixture.CreateToken(issuer: null),
            _ => throw new ArgumentOutOfRangeException(nameof(tokenKind)),
        };
        using var client = fixture.CreateClient(token);

        await AssertStatusAsync(StatusCode.Unauthenticated,
            () => client.Trips.GetTripAsync(new GetTripRequest { TripId = Guid.NewGuid().ToString() }).ResponseAsync);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task AuthenticatedTokenWithoutUsableSubject_IsRejected(string? subject)
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken(subject));

        await AssertStatusAsync(StatusCode.PermissionDenied,
            () => CreateTripWithProfileAsync(client, NewTrip()));
    }

    [Fact]
    public async Task RetryWithSameRequestIdAndPayload_ReturnsOriginalTrip()
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));
        var request = NewTrip();

        var first = await CreateTripWithProfileAsync(client, request);
        var second = await CreateTripWithProfileAsync(client, request);

        Assert.Equal(first.Trip, second.Trip);
    }

    [Fact]
    public async Task RetryWithChangedPayload_IsRejected()
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));
        var request = NewTrip();
        await CreateTripWithProfileAsync(client, request);
        request.SeatsTotal++;

        await AssertStatusAsync(StatusCode.AlreadyExists, () => CreateTripWithProfileAsync(client, request));
    }

    [Fact]
    public async Task SubMicrosecondTimestamp_RoundTripsAndRetriesAtPostgresPrecision()
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));
        var request = NewTrip();
        request.DepartureAt.Nanos += 100;

        var created = await CreateTripWithProfileAsync(client, request);
        var retry = await CreateTripWithProfileAsync(client, request);
        var fetched = await client.Trips.GetTripAsync(new GetTripRequest { TripId = created.Trip.Id });

        Assert.Equal(request.DepartureAt.Seconds, created.Trip.DepartureAt.Seconds);
        Assert.Equal(request.DepartureAt.Nanos - request.DepartureAt.Nanos % 1000, created.Trip.DepartureAt.Nanos);
        Assert.Equal(0, created.Trip.CreatedAt.Nanos % 1000);
        Assert.Equal(created.Trip, retry.Trip);
        Assert.Equal(created.Trip, fetched.Trip);
    }

    [Fact]
    public async Task ConcurrentRetries_CreateOnlyOneTrip()
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));
        var request = NewTrip();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => CreateTripWithProfileAsync(client, request)));

        Assert.Single(responses.Select(response => response.Trip.Id).Distinct());
        var search = await client.Trips.SearchTripsAsync(SearchFor(request));
        Assert.Single(search.Trips);
    }

    [Fact]
    public async Task RequestId_IsScopedToTheAuthenticatedOwner()
    {
        var request = NewTrip();
        using var firstDriver = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));
        using var secondDriver = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));

        var first = await CreateTripWithProfileAsync(firstDriver, request);
        var second = await CreateTripWithProfileAsync(secondDriver, request);

        Assert.NotEqual(first.Trip.Id, second.Trip.Id);
    }

    [Theory]
    [InlineData("different-subject")]
    [InlineData("same-subject-different-issuer")]
    public async Task AnotherIdentity_CannotCancelTrip(string scenario)
    {
        var subject = Guid.NewGuid().ToString();
        using var owner = fixture.CreateClient(GrpcApiFixture.CreateToken(subject));
        var created = await CreateTripWithProfileAsync(owner, NewTrip());
        var attackerToken = scenario == "different-subject"
            ? GrpcApiFixture.CreateToken("someone-else")
            : GrpcApiFixture.CreateToken(subject, GrpcApiFixture.OtherIssuer);
        using var attacker = fixture.CreateClient(attackerToken);

        await AssertStatusAsync(StatusCode.PermissionDenied,
            () => attacker.Trips.CancelTripAsync(new CancelTripRequest { TripId = created.Trip.Id }).ResponseAsync);
        var unchanged = await owner.Trips.GetTripAsync(new GetTripRequest { TripId = created.Trip.Id });
        Assert.Equal(TripState.Planned, unchanged.Trip.State);
    }

    [Fact]
    public async Task OwnerCancellation_IsIdempotentAndRemovesTripFromSearch()
    {
        using var owner = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));
        var request = NewTrip();
        var created = await CreateTripWithProfileAsync(owner, request);
        var cancelRequest = new CancelTripRequest { TripId = created.Trip.Id };

        var first = await owner.Trips.CancelTripAsync(cancelRequest);
        var repeated = await owner.Trips.CancelTripAsync(cancelRequest);
        var search = await owner.Trips.SearchTripsAsync(SearchFor(request));

        Assert.Equal(TripState.Cancelled, first.Trip.State);
        Assert.Equal(first.Trip, repeated.Trip);
        Assert.Empty(search.Trips);
    }

    [Fact]
    public async Task CreateRetryAfterCancellation_ReturnsTheSameCancelledTrip()
    {
        using var owner = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));
        var request = NewTrip();
        var created = await CreateTripWithProfileAsync(owner, request);
        var cancelled = await owner.Trips.CancelTripAsync(new CancelTripRequest { TripId = created.Trip.Id });

        var retry = await CreateTripWithProfileAsync(owner, request);

        Assert.Equal(cancelled.Trip, retry.Trip);
        Assert.Equal(TripState.Cancelled, retry.Trip.State);
    }

    [Fact]
    public async Task ConcurrentCancellation_ReturnsCancelledForEveryRequest()
    {
        using var owner = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));
        var created = await CreateTripWithProfileAsync(owner, NewTrip());
        var request = new CancelTripRequest { TripId = created.Trip.Id };

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => owner.Trips.CancelTripAsync(request).ResponseAsync));

        Assert.All(responses, response =>
        {
            Assert.Equal(created.Trip.Id, response.Trip.Id);
            Assert.Equal(TripState.Cancelled, response.Trip.State);
        });
    }

    [Fact]
    public async Task NativeGrpcOverHttp2_UsesTheSameServiceAsGrpcWeb()
    {
        var token = GrpcApiFixture.CreateToken(Guid.NewGuid().ToString());
        using var browser = fixture.CreateClient(token);
        var created = await CreateTripWithProfileAsync(browser, NewTrip());
        using var nativeClient = fixture.CreateNativeClient(token);

        var fetched = await nativeClient.Trips.GetTripAsync(new GetTripRequest { TripId = created.Trip.Id });

        Assert.Equal(created.Trip, fetched.Trip);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task MalformedTripId_ReturnsInvalidArgument(string tripId)
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken());

        await AssertStatusAsync(StatusCode.InvalidArgument,
            () => client.Trips.GetTripAsync(new GetTripRequest { TripId = tripId }).ResponseAsync);
    }

    [Fact]
    public async Task UnknownTrip_ReturnsNotFound()
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken());

        await AssertStatusAsync(StatusCode.NotFound,
            () => client.Trips.GetTripAsync(new GetTripRequest { TripId = Guid.NewGuid().ToString() }).ResponseAsync);
    }

    [Fact]
    public async Task CancelledRequest_PropagatesToDatabaseOperation()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TripApplicationService>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetAsync(Guid.NewGuid(), cancellation.Token));
    }

    [Theory]
    [InlineData("missing-timestamp")]
    [InlineData("timestamp-out-of-range")]
    [InlineData("timestamp-invalid-nanos")]
    [InlineData("past-departure")]
    [InlineData("too-distant-departure")]
    [InlineData("zero-seats")]
    [InlineData("too-many-seats")]
    [InlineData("blank-origin")]
    [InlineData("long-origin")]
    [InlineData("control-character")]
    [InlineData("same-origin-destination")]
    [InlineData("empty-request-id")]
    public async Task InvalidCreateInput_ReturnsInvalidArgument(string scenario)
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken());
        var request = NewTrip();
        switch (scenario)
        {
            case "missing-timestamp": request.DepartureAt = null; break;
            case "timestamp-out-of-range": request.DepartureAt = new Timestamp { Seconds = 253402300800 }; break;
            case "timestamp-invalid-nanos": request.DepartureAt = new Timestamp { Seconds = 1, Nanos = -1 }; break;
            case "past-departure": request.DepartureAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-1)); break;
            case "too-distant-departure": request.DepartureAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(400)); break;
            case "zero-seats": request.SeatsTotal = 0; break;
            case "too-many-seats": request.SeatsTotal = 9; break;
            case "blank-origin": request.Origin = " "; break;
            case "long-origin": request.Origin = new string('x', 501); break;
            case "control-character": request.Origin = "Home\nInjected"; break;
            case "same-origin-destination": request.Destination = request.Origin; break;
            case "empty-request-id": request.RequestId = Guid.Empty.ToString(); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        await AssertStatusAsync(StatusCode.InvalidArgument, () => CreateTripWithProfileAsync(client, request));
    }

    [Fact]
    public async Task Locations_AreTrimmedBeforePersistence()
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken());
        var request = NewTrip();
        request.Origin = "  Home  ";
        request.Destination = " Office ";

        var created = await CreateTripWithProfileAsync(client, request);

        Assert.Equal("Home", created.Trip.Origin);
        Assert.Equal("Office", created.Trip.Destination);
    }

    [Fact]
    public async Task Search_UsesDeterministicBoundedPagination()
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));
        var firstRequest = NewTrip();
        var requests = Enumerable.Range(0, 3).Select(_ =>
        {
            var request = firstRequest.Clone();
            request.RequestId = Guid.NewGuid().ToString();
            return request;
        }).ToArray();
        var created = new List<Trip>();
        foreach (var request in requests)
        {
            created.Add((await CreateTripWithProfileAsync(client, request)).Trip);
        }

        var query = SearchFor(firstRequest);
        query.PageSize = 2;
        var firstPage = await client.Trips.SearchTripsAsync(query);
        Assert.Equal(2, firstPage.Trips.Count);
        Assert.True(firstPage.HasNextOffset);
        Assert.Equal(2, firstPage.NextOffset);
        query.Offset = firstPage.NextOffset;
        var secondPage = await client.Trips.SearchTripsAsync(query);

        Assert.Single(secondPage.Trips);
        Assert.False(secondPage.HasNextOffset);
        var returned = firstPage.Trips.Concat(secondPage.Trips).Select(trip => trip.Id).ToArray();
        Assert.Equal(3, returned.Distinct().Count());
        Assert.Equal(created.Select(trip => trip.Id).Order(StringComparer.Ordinal).ToArray(), returned);
    }

    [Fact]
    public async Task SearchWithoutOptionalFilters_UsesDefaultPageSize()
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken(Guid.NewGuid().ToString()));
        await CreateTripWithProfileAsync(client, NewTrip());
        var request = new SearchTripsRequest
        {
            DepartureFrom = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(1)),
            DepartureUntil = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(3)),
        };

        var response = await client.Trips.SearchTripsAsync(request);

        Assert.InRange(response.Trips.Count, 1, 20);
        Assert.All(response.Trips, trip => Assert.Equal(TripState.Planned, trip.State));
    }

    [Fact]
    public async Task UnexpectedDatabaseFailure_IsSanitizedAndIncludesTraceId()
    {
        using var failingFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddDbContext<CarpoolrDbContext>(options =>
                options.AddInterceptors(new FailingDatabaseCommandInterceptor()))));
        using var client = GrpcApiFixture.CreateClient(failingFactory, GrpcApiFixture.CreateToken());

        var failure = await Assert.ThrowsAsync<RpcException>(() => client.Trips.GetTripAsync(
            new GetTripRequest { TripId = Guid.NewGuid().ToString() }).ResponseAsync);

        Assert.Equal(StatusCode.Internal, failure.StatusCode);
        Assert.Equal("An unexpected server error occurred.", failure.Status.Detail);
        Assert.DoesNotContain("sensitive-database-details", failure.Status.Detail);
        Assert.False(string.IsNullOrWhiteSpace(failure.Trailers.GetValue("trace-id")));
    }

    [Theory]
    [InlineData("too-large-page")]
    [InlineData("negative-offset")]
    [InlineData("excessive-offset")]
    [InlineData("too-long-window")]
    [InlineData("reversed-window")]
    [InlineData("missing-window")]
    public async Task InvalidSearchBounds_ReturnInvalidArgument(string scenario)
    {
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken());
        var request = SearchFor(NewTrip());
        switch (scenario)
        {
            case "too-large-page": request.PageSize = 101; break;
            case "negative-offset": request.Offset = -1; break;
            case "excessive-offset": request.Offset = 10001; break;
            case "too-long-window": request.DepartureUntil = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(40)); break;
            case "reversed-window": request.DepartureUntil = request.DepartureFrom.Clone(); break;
            case "missing-window": request.DepartureFrom = null; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        await AssertStatusAsync(StatusCode.InvalidArgument, () => client.Trips.SearchTripsAsync(request).ResponseAsync);
    }

    [Fact]
    public async Task HealthEndpoint_RemainsAnonymous()
    {
        using var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BrowserPreflight_AllowsConfiguredOriginAndAuthorizationHeader()
    {
        using var client = fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/carpoolr.trips.v1.TripService/GetTrip");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,x-grpc-web");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5173", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task GrpcWebResponse_ExposesGrpcStatusHeadersToBrowser()
    {
        using var client = fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/carpoolr.trips.v1.TripService/GetTrip");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GrpcApiFixture.CreateToken());
        request.Content = new ByteArrayContent([0, 0, 0, 0, 0]);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc-web");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/grpc-web", response.Content.Headers.ContentType?.MediaType);
        var exposedHeaders = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("grpc-status", exposedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grpc-message", exposedHeaders, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnconfiguredBrowserOrigin_ReceivesNoCorsPermission()
    {
        using var client = fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/carpoolr.trips.v1.TripService/GetTrip");
        request.Headers.Add("Origin", "https://untrusted.test");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static async Task<CreateTripResponse> CreateTripWithProfileAsync(
        GrpcTestClient client, CreateTripRequest request)
    {
        var current = await client.Profiles.GetMyProfileAsync(new GetMyProfileRequest());
        if (!current.Profile.IsSaved)
        {
            await client.Profiles.UpdateMyProfileAsync(new UpdateMyProfileRequest
            {
                DisplayName = "Test colleague",
                PickupAddress = "Example Road 1",
                Vehicle = new Vehicle
                {
                    Name = "Test car",
                    PassengerSeats = 8,
                    EnergyKind = EnergyKind.Petrol,
                    ConsumptionPer100Km = "7",
                    UnitPriceEur = "1.8",
                },
            });
        }

        return await client.Trips.CreateTripAsync(request);
    }

    private static CreateTripRequest NewTrip() => new()
    {
        RequestId = Guid.NewGuid().ToString(),
        Origin = $"Home {Guid.NewGuid():N}",
        Destination = "Office",
        DepartureAt = Timestamp.FromDateTimeOffset(MicrosecondPrecision(DateTimeOffset.UtcNow.AddDays(2))),
        SeatsTotal = 3,
        DistanceKm = "25",
    };

    private static SearchTripsRequest SearchFor(CreateTripRequest trip) => new()
    {
        Origin = trip.Origin,
        Destination = trip.Destination,
        DepartureFrom = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(1)),
        DepartureUntil = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(3)),
        PageSize = 20,
    };

    private static async Task AssertStatusAsync<T>(StatusCode expected, Func<Task<T>> call)
    {
        var error = await Assert.ThrowsAsync<RpcException>(async () => await call());
        Assert.Equal(expected, error.StatusCode);
    }

    private static DateTimeOffset MicrosecondPrecision(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % 10, value.Offset);

    private sealed class FailingDatabaseCommandInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("sensitive-database-details");
    }
}
