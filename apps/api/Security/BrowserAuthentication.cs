using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Carpoolr.Api.Security;

public static class BrowserAuthentication
{
    public const string SessionScheme = "CarpoolrSession";
    public const string SelectScheme = "CarpoolrAuthentication";
    public const string OidcScheme = "Authentik";
    public const string CsrfHeader = "X-CSRF-Token";
    public const string CallbackPath = "/auth/signin-oidc";
    public const string PublicCallbackPath = "/api/auth/signin-oidc";

    internal static void AddBrowserAuthentication(
        this IServiceCollection services,
        BrowserAuthenticationSettings settings,
        string authority)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("Carpoolr");
        if (!string.IsNullOrWhiteSpace(settings.DataProtectionKeysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(settings.DataProtectionKeysPath));
        }

        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeader;
            options.Cookie.Name = "__Host-Carpoolr.Csrf";
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddSingleton<IAntiforgeryAdditionalDataProvider, BrowserAntiforgeryIdentity>();
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var previous = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async context =>
            {
                await previous(context);
                // Authentik uses the client ID as audience for ID and access
                // tokens. Its access tokens additionally identify the authorized
                // party and granted scopes. An ID token must not authorize RPCs.
                if (context.Principal?.FindFirst("azp")?.Value != settings.ClientId ||
                    string.IsNullOrWhiteSpace(context.Principal.FindFirst("scope")?.Value))
                {
                    context.Fail("An Authentik access token for this client is required.");
                }
            };
        });

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = SelectScheme;
                options.DefaultAuthenticateScheme = SelectScheme;
                options.DefaultChallengeScheme = SelectScheme;
            })
            .AddPolicyScheme(SelectScheme, null, options =>
                options.ForwardDefaultSelector = context => UsesAuthorizationHeader(context)
                    ? JwtBearerDefaults.AuthenticationScheme
                    : SessionScheme)
            .AddCookie(SessionScheme, options =>
            {
                options.Cookie.Name = "__Host-Carpoolr.Session";
                options.Cookie.Path = "/";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = false;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = context =>
                {
                    if (!UserIdentity.HasUsableClaims(context.Principal?.FindFirst("iss")?.Value,
                            context.Principal?.FindFirst("sub")?.Value))
                    {
                        context.RejectPrincipal();
                    }

                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(OidcScheme, options =>
            {
                options.Authority = authority;
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.SignInScheme = SessionScheme;
                options.CallbackPath = CallbackPath;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                // The default claim actions remove "iss" after TokenValidated;
                // this application deliberately keeps the validated issuer.
                options.ClaimActions.Clear();
                options.UseTokenLifetime = false;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidIssuer = authority;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidAudience = settings.ClientId;
                options.TokenValidationParameters.ValidateLifetime = true;
                options.TokenValidationParameters.RequireSignedTokens = true;
                options.TokenValidationParameters.RequireExpirationTime = true;
                options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);

                // nginx strips /api before forwarding. The browser must send the
                // correlation and nonce cookies to the public callback path.
                options.CorrelationCookie.Path = "/api/auth";
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.Path = "/api/auth";
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.NonceCookie.SameSite = SameSiteMode.Lax;

                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    context.ProtocolMessage.RedirectUri = settings.PublicOrigin + PublicCallbackPath;
                    return Task.CompletedTask;
                };
                options.Events.OnTokenValidated = context =>
                {
                    var issuer = context.SecurityToken.Issuer;
                    var subject = context.Principal?.FindFirst("sub")?.Value;
                    if (!UserIdentity.HasUsableClaims(issuer, subject))
                    {
                        context.Fail("The identity does not have a usable issuer and subject.");
                        return Task.CompletedTask;
                    }

                    var name = BrowserAuthenticationEndpoints.DisplayName(context.Principal!);
                    // Keep only the identity needed by this app. OAuth tokens,
                    // unrelated claims, and email addresses are not stored in the cookie.
                    context.Principal = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("iss", issuer, ClaimValueTypes.String, issuer),
                        new Claim("sub", subject, ClaimValueTypes.String, issuer),
                        new Claim("name", name, ClaimValueTypes.String, issuer),
                    ], SessionScheme, "name", "role"));
                    context.Properties ??= new AuthenticationProperties();
                    context.Properties.IsPersistent = false;
                    context.Properties.AllowRefresh = false;
                    return Task.CompletedTask;
                };
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect(settings.PublicOrigin + "/?authError=login_failed");
                    return Task.CompletedTask;
                };
                options.Events.OnAccessDenied = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect(settings.PublicOrigin + "/?authError=access_denied");
                    return Task.CompletedTask;
                };
            });
    }

    internal static bool UsesAuthorizationHeader(HttpContext context) =>
        !string.IsNullOrEmpty(context.Request.Headers.Authorization);
}
