using Carpoolr.Api.Grpc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Carpoolr.Api.Tests;

public sealed class AuthenticationConfigurationTests
{
    [Fact]
    public void ProductionWithoutIdentityProviderConfiguration_RefusesToStart()
    {
        using var factory = new WebApplicationFactory<TripGrpcService>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Authentication:Authority"] = null,
                    ["Authentication:Audience"] = null,
                }));
        });

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Authentication:Authority", error.Message);
    }
}
