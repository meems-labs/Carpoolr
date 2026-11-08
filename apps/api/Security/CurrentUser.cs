namespace Carpoolr.Api.Security;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public UserIdentity GetRequiredIdentity()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var issuer = principal?.FindFirst("iss")?.Value;
        var subject = principal?.FindFirst("sub")?.Value;

        if (principal?.Identity?.IsAuthenticated != true ||
            !UserIdentity.HasUsableClaims(issuer, subject))
        {
            throw new UnauthorizedAccessException("A validated user identity is required.");
        }

        return new UserIdentity(issuer, subject);
    }
}
