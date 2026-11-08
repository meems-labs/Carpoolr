using System.Security.Claims;
using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Security;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Carpoolr.Api.Tests;

public sealed class IdentityBoundsTests(GrpcApiFixture fixture) : IClassFixture<GrpcApiFixture>
{
    [Theory]
    [InlineData("ascii-too-long")]
    [InlineData("utf8-too-long")]
    [InlineData("control-character")]
    public async Task SignedSubjectOutsideStorageBounds_CannotUpdateProfile(string scenario)
    {
        var subject = scenario switch
        {
            "ascii-too-long" => new string('x', 256),
            "utf8-too-long" => new string('ä', 128),
            "control-character" => "person\nother-person",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        using var client = fixture.CreateClient(GrpcApiFixture.CreateToken(subject));

        var failure = await Assert.ThrowsAsync<RpcException>(() => client.Profiles.UpdateMyProfileAsync(
            new UpdateMyProfileRequest { DisplayName = "Name", PickupAddress = "Address 1" }).ResponseAsync);

        Assert.Equal(StatusCode.PermissionDenied, failure.StatusCode);
    }

    [Fact]
    public async Task ColleaguePolicy_RejectsIssuerOutsideUtf8Bounds()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("iss", new string('ä', 257)),
            new Claim("sub", "person"),
        ], "test"));

        var result = await authorization.AuthorizeAsync(principal, null, AuthenticationExtensions.ColleaguePolicy);

        Assert.False(result.Succeeded);
    }
}
