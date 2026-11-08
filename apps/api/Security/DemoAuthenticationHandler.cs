using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Carpoolr.Api.Security;

/// <summary>
/// Simulates a selected identity only in the opt-in staging environment.
/// It provides no authentication security; anyone can select any predefined person.
/// </summary>
public sealed class DemoAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "StagingDemo";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(DemoUserCatalog.HeaderName, out var selectedUsers))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (selectedUsers.Count != 1 ||
            !Guid.TryParseExact(selectedUsers[0], "D", out var selectedId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Select a predefined staging test person."));
        }

        var person = DemoUserCatalog.Users.SingleOrDefault(user => user.Id == selectedId);
        if (person is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Select a predefined staging test person."));
        }

        var identity = new ClaimsIdentity(
        [
            new Claim("iss", DemoUserCatalog.Issuer),
            new Claim("sub", person.Id.ToString("D")),
            new Claim("name", person.DisplayName),
        ], SchemeName, "name", "role");

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = SchemeName;
        return Task.CompletedTask;
    }
}
