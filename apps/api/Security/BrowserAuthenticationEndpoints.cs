using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace Carpoolr.Api.Security;

public static class BrowserAuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapBrowserAuthentication(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/session", (HttpContext context, BrowserAuthenticationSettings settings) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            var authenticated = context.User.Identity?.IsAuthenticated == true &&
                UserIdentity.HasUsableClaims(context.User.FindFirst("iss")?.Value,
                    context.User.FindFirst("sub")?.Value);
            var user = authenticated
                ? new BrowserSessionUser(IdentityKey(context.User), DisplayName(context.User))
                : null;
            var csrfToken = settings.Enabled && authenticated && !BrowserAuthentication.UsesAuthorizationHeader(context)
                ? context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context).RequestToken
                : null;

            return Results.Ok(new BrowserSessionResponse(
                settings.DemoEnabled ? "demo" : settings.Enabled ? "oidc" : "bearer",
                authenticated,
                user,
                csrfToken));
        }).AllowAnonymous();

        endpoints.MapGet("/auth/login", async (HttpContext context, BrowserAuthenticationSettings settings) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!settings.Enabled)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var returnUrl = context.Request.Query["returnUrl"].ToString();
            if (string.IsNullOrEmpty(returnUrl))
            {
                returnUrl = "/";
            }

            if (!IsSafeReturnUrl(returnUrl))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await context.ChallengeAsync(BrowserAuthentication.OidcScheme,
                new AuthenticationProperties { RedirectUri = settings.PublicOrigin + returnUrl });
        }).AllowAnonymous();

        endpoints.MapPost("/auth/logout", async (HttpContext context, BrowserAuthenticationSettings settings) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!settings.Enabled || BrowserAuthentication.UsesAuthorizationHeader(context))
            {
                return Results.NotFound();
            }

            // Local sign-out deliberately keeps sessions in unrelated Authentik applications.
            await context.SignOutAsync(BrowserAuthentication.SessionScheme);
            return Results.NoContent();
        }).RequireAuthorization(AuthenticationExtensions.ColleaguePolicy);

        return endpoints;
    }

    internal static bool IsSafeReturnUrl(string returnUrl) =>
        returnUrl.Length <= 2048 && returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
        !returnUrl.Contains('\\') && !returnUrl.Any(char.IsControl);

    internal static string DisplayName(ClaimsPrincipal principal)
    {
        var name = principal.FindFirst("name")?.Value ?? principal.FindFirst("preferred_username")?.Value;
        return string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl)
            ? "Konto"
            : name;
    }

    internal static string IdentityKey(ClaimsPrincipal principal)
    {
        var issuer = principal.FindFirst("iss")!.Value;
        var subject = principal.FindFirst("sub")!.Value;
        // A length prefix keeps the pair unambiguous even if an issuer contains separators.
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{issuer.Length}:{issuer}{subject}")));
    }
}

public sealed record BrowserSessionResponse(string Mode, bool Authenticated, BrowserSessionUser? User, string? CsrfToken);
public sealed record BrowserSessionUser(string Id, string DisplayName);
