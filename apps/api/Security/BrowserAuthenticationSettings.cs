namespace Carpoolr.Api.Security;

/// <summary>
/// An explicit browser origin avoids trusting client-controlled forwarded headers
/// when constructing an OIDC callback behind the /api reverse proxy.
/// </summary>
public sealed record BrowserAuthenticationSettings(
    bool Enabled,
    bool DemoEnabled,
    string ClientId,
    string ClientSecret,
    string PublicOrigin,
    string? DataProtectionKeysPath)
{
    public static BrowserAuthenticationSettings FromConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool demoEnabled)
    {
        var section = configuration.GetSection("Authentication:Browser");
        var enabled = section.GetValue<bool>("Enabled");
        var settings = new BrowserAuthenticationSettings(
            enabled,
            demoEnabled,
            section["ClientId"] ?? string.Empty,
            section["ClientSecret"] ?? string.Empty,
            section["PublicOrigin"]?.TrimEnd('/') ?? string.Empty,
            section["DataProtectionKeysPath"]);

        if (!enabled)
        {
            return settings;
        }

        if (demoEnabled)
        {
            throw new InvalidOperationException(
                "Authentication:Browser:Enabled and DemoMode:Enabled are mutually exclusive.");
        }

        if (!Uri.TryCreate(configuration["Authentication:Authority"], UriKind.Absolute, out var authority) ||
            authority.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(authority.UserInfo) ||
            !string.IsNullOrEmpty(authority.Query) || !string.IsNullOrEmpty(authority.Fragment))
        {
            throw new InvalidOperationException("Configure an HTTPS Authentication:Authority for browser sign-in.");
        }

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "Configure Authentication:Browser:ClientId and Authentication:Browser:ClientSecret.");
        }

        if (!Uri.TryCreate(settings.PublicOrigin, UriKind.Absolute, out var origin) ||
            origin.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(origin.UserInfo) || origin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
        {
            throw new InvalidOperationException(
                "Configure Authentication:Browser:PublicOrigin as an HTTPS origin without a path.");
        }

        if ((!environment.IsDevelopment() && string.IsNullOrWhiteSpace(settings.DataProtectionKeysPath)) ||
            (!string.IsNullOrWhiteSpace(settings.DataProtectionKeysPath) &&
             !Path.IsPathFullyQualified(settings.DataProtectionKeysPath)))
        {
            throw new InvalidOperationException(
                "Configure Authentication:Browser:DataProtectionKeysPath as an absolute persistent directory.");
        }

        return settings;
    }
}
