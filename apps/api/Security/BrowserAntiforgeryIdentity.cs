using Microsoft.AspNetCore.Antiforgery;

namespace Carpoolr.Api.Security;

/// <summary>
/// Bind the request token to both identity components. Subjects alone can be
/// reused by distinct issuers, and display names are neither stable nor unique.
/// </summary>
public sealed class BrowserAntiforgeryIdentity : IAntiforgeryAdditionalDataProvider
{
    public string GetAdditionalData(HttpContext context) =>
        BrowserAuthenticationEndpoints.IdentityKey(context.User);

    public bool ValidateAdditionalData(HttpContext context, string additionalData) =>
        UserIdentity.HasUsableClaims(context.User.FindFirst("iss")?.Value, context.User.FindFirst("sub")?.Value) &&
        string.Equals(GetAdditionalData(context), additionalData, StringComparison.Ordinal);
}
