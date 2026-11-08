using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Carpoolr.Api.Security;

public static class AuthenticationExtensions
{
    public const string ColleaguePolicy = "Colleague";

    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var demoEnabled = configuration.GetValue<bool>("DemoMode:Enabled");
        var browser = BrowserAuthenticationSettings.FromConfiguration(configuration, environment, demoEnabled);
        services.AddSingleton(browser);
        if (demoEnabled)
        {
            if (!environment.IsStaging())
            {
                throw new InvalidOperationException("DemoMode:Enabled is permitted only in Staging.");
            }

            services.AddAuthentication(DemoAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(
                    DemoAuthenticationHandler.SchemeName, _ => { });
        }
        else
        {
            AddJwtAuthentication(services, configuration, environment);
            if (browser.Enabled)
            {
                services.AddBrowserAuthentication(browser, configuration["Authentication:Authority"]!);
            }
        }

        services.AddAuthorization(options =>
        {
            // Protect new endpoints by default; public exceptions are explicit.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(ColleaguePolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    UserIdentity.HasUsableClaims(context.User.FindFirst("iss")?.Value,
                        context.User.FindFirst("sub")?.Value)));
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        return services;
    }

    private static void AddJwtAuthentication(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var authority = configuration["Authentication:Authority"];
        var audience = configuration["Authentication:Audience"];

        // Local signed tokens are configured by dotnet user-jwts in Development.
        // A deployed instance must explicitly name its trusted identity provider.
        if (!environment.IsDevelopment() || !string.IsNullOrWhiteSpace(authority))
        {
            if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
                authorityUri.Scheme != Uri.UriSchemeHttps ||
                string.IsNullOrWhiteSpace(audience))
            {
                throw new InvalidOperationException(
                    "Configure an HTTPS Authentication:Authority and Authentication:Audience.");
            }
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.SaveToken = false;
                options.RequireHttpsMetadata = true;

                if (!string.IsNullOrWhiteSpace(authority))
                {
                    options.Authority = authority;
                    options.Audience = audience;
                }

                // Keep framework-configured signing keys for local user-jwts.
                var validation = options.TokenValidationParameters;
                validation.ValidateIssuer = true;
                validation.ValidateAudience = true;
                validation.ValidateIssuerSigningKey = true;
                validation.ValidateLifetime = true;
                validation.RequireSignedTokens = true;
                validation.RequireExpirationTime = true;
                validation.ClockSkew = TimeSpan.FromSeconds(30);
            });
    }
}
