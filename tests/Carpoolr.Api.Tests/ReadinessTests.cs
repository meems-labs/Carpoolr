using System.Data.Common;
using System.Net;
using Carpoolr.Api.Data;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Carpoolr.Api.Tests;

public sealed class ReadinessTests(GrpcApiFixture fixture) : IClassFixture<GrpcApiFixture>
{
    [Fact]
    public async Task MigratedDatabase_IsReadyWithoutAuthentication()
    {
        using var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Trips")]
    [InlineData("Profiles")]
    [InlineData("Bookings")]
    [InlineData("RecurringSeries")]
    [InlineData("Bills")]
    [InlineData("BillShares")]
    public async Task MissingApplicationTable_IsNotReadyWithoutDisclosingFailureDetails(string table)
    {
        using var failingFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddDbContext<CarpoolrDbContext>(options =>
                options.AddInterceptors(new MissingTripSchemaInterceptor(table)))));
        using var client = failingFactory.CreateClient();

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DatabaseFailure_DoesNotAffectLiveness()
    {
        using var failingFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddDbContext<CarpoolrDbContext>(options =>
                options.AddInterceptors(new MissingTripSchemaInterceptor()))));
        using var client = failingFactory.CreateClient();

        var readiness = await client.GetAsync("/readyz");
        var liveness = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await liveness.Content.ReadAsStringAsync());
    }

    private sealed class MissingTripSchemaInterceptor(string table = "Trips") : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains($"\"{table}\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("sensitive-schema-information");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
