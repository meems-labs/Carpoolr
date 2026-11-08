using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Carpoolr.Api.Grpc;
using Carpoolr.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Carpoolr.Api.Tests;

/// <summary>
/// Exercises the actual OIDC middleware against an in-memory signed identity
/// provider, including code redemption, PKCE, nonce, correlation, and cookies.
/// No external Authentik account or database is needed.
/// </summary>
public sealed class BrowserAuthenticationTests
{
    [Fact]
    public async Task AnonymousSession_AdvertisesOidcWithoutPersonalDataOrTokens()
    {
        using var factory = new BrowserFactory();
        using var client = factory.NewClient();

        using var response = await client.GetAsync("/auth/session");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var session = await response.Content.ReadFromJsonAsync<BrowserSessionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.NotNull(session);
        Assert.Equal("oidc", session.Mode);
        Assert.False(session.Authenticated);
        Assert.Null(session.User);
        Assert.Null(session.CsrfToken);
    }

    [Fact]
    public async Task Login_UsesFixedPublicCallbackCodeFlowAndPkceDespiteSpoofedHostHeaders()
    {
        using var factory = new BrowserFactory();
        using var client = factory.NewClient();
        client.DefaultRequestHeaders.Host = "attacker.invalid";
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "attacker.invalid");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");

        using var response = await client.GetAsync("/auth/login?returnUrl=%2Fkosten");
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("auth.carpoolr.test", response.Headers.Location.Host);
        Assert.Equal(BrowserFactory.PublicOrigin + BrowserAuthentication.PublicCallbackPath, query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"]));
        Assert.All(response.Headers.GetValues("Set-Cookie"), cookie =>
        {
            Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData("https://attacker.invalid")]
    [InlineData("//attacker.invalid")]
    [InlineData("/\\attacker.invalid")]
    [InlineData("/bad\r\nLocation:https://attacker.invalid")]
    [InlineData("relative-path")]
    public async Task Login_RejectsUnsafeReturnUrls(string returnUrl)
    {
        using var factory = new BrowserFactory();
        using var client = factory.NewClient();

        using var response = await client.GetAsync("/auth/login?returnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ValidOidcCallback_CreatesMinimalHttpOnlySessionAndBoundCsrfToken()
    {
        using var factory = new BrowserFactory();
        using var client = factory.NewClient();

        using var callback = await factory.LoginAsync(client, "/kosten");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.True(callback.Headers.CacheControl?.NoStore);
        Assert.Equal(BrowserFactory.PublicOrigin + "/kosten", callback.Headers.Location!.AbsoluteUri);
        var sessionCookie = SessionCookie(callback);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", sessionCookie, StringComparison.OrdinalIgnoreCase);

        var cookieOptions = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(BrowserAuthentication.SessionScheme);
        var ticket = cookieOptions.TicketDataFormat.Unprotect(CookieValue(sessionCookie));
        Assert.NotNull(ticket);
        Assert.Equal(["iss", "sub", "name"], ticket.Principal.Claims.Select(claim => claim.Type));
        Assert.Empty(ticket.Properties.GetTokens());
        Assert.False(ticket.Properties.IsPersistent);
        Assert.False(ticket.Properties.AllowRefresh);
        Assert.InRange(ticket.Properties.ExpiresUtc!.Value - ticket.Properties.IssuedUtc!.Value,
            TimeSpan.FromHours(7.99), TimeSpan.FromHours(8.01));
        Assert.False(cookieOptions.SlidingExpiration);

        using var request = GetSessionRequest(CookieHeader(sessionCookie));
        using var response = await client.SendAsync(request);
        var session = await response.Content.ReadFromJsonAsync<BrowserSessionResponse>();
        Assert.True(session!.Authenticated);
        Assert.Equal("Alex", session.User!.DisplayName);
        Assert.Matches("^[a-f0-9]{64}$", session.User.Id);
        Assert.NotEmpty(session.CsrfToken!);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("fake-access-token", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(factory.Provider.PkceValidated);
    }

    [Theory]
    [InlineData("invalid-signature")]
    [InlineData("invalid-nonce")]
    [InlineData("invalid-issuer")]
    [InlineData("oversized-subject")]
    [InlineData("missing-correlation")]
    public async Task InvalidOidcCallback_NeverCreatesSessionOrLeaksProviderErrors(string scenario)
    {
        using var factory = new BrowserFactory();
        factory.Provider.Scenario = scenario;
        using var client = factory.NewClient();

        using var callback = await factory.LoginAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(BrowserFactory.PublicOrigin + "/?authError=login_failed", callback.Headers.Location!.AbsoluteUri);
        Assert.DoesNotContain(callback.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith("__Host-Carpoolr.Session=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CookieRequests_RequireCsrfAndValidLogoutOnlyClearsLocalSession()
    {
        using var factory = new BrowserFactory();
        using var client = factory.NewClient();
        var cookie = factory.Cookie("person-a");
        using var sessionRequest = GetSessionRequest(cookie);
        using var sessionResponse = await client.SendAsync(sessionRequest);
        var session = (await sessionResponse.Content.ReadFromJsonAsync<BrowserSessionResponse>())!;
        var allCookies = cookie + "; " + CookieHeader(sessionResponse.Headers.GetValues("Set-Cookie").Single());

        using var noToken = PostRequest("/auth/logout", allCookies);
        using var denied = await client.SendAsync(noToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var valid = PostRequest("/auth/logout", allCookies, session.CsrfToken);
        using var loggedOut = await client.SendAsync(valid);
        Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);
        Assert.Null(loggedOut.Headers.Location);
        Assert.Contains(loggedOut.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("__Host-Carpoolr.Session=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CookieAuthenticatedGrpcPost_CannotBypassCsrf()
    {
        using var factory = new BrowserFactory();
        using var client = factory.NewClient();
        using var request = PostRequest("/carpoolr.trips.v1.ProfileService/GetMyProfile", factory.Cookie("person"));
        request.Content = new ByteArrayContent([0, 0, 0, 0, 0]);
        request.Content.Headers.ContentType = new("application/grpc-web+proto");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("invalid_csrf_token", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("person-b", BrowserFactory.Issuer)]
    [InlineData("person-a", "https://another-issuer.carpoolr.test")]
    public async Task CsrfToken_IsBoundToBothIssuerAndSubject(string subject, string issuer)
    {
        using var factory = new BrowserFactory();
        using var client = factory.NewClient();
        using var sessionRequest = GetSessionRequest(factory.Cookie("person-a"));
        using var sessionResponse = await client.SendAsync(sessionRequest);
        var session = (await sessionResponse.Content.ReadFromJsonAsync<BrowserSessionResponse>())!;
        var csrfCookie = CookieHeader(sessionResponse.Headers.GetValues("Set-Cookie").Single());
        using var request = PostRequest("/auth/logout", factory.Cookie(subject, issuer) + "; " + csrfCookie,
            session.CsrfToken);

        using var denied = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task InvalidBearer_DoesNotFallBackToValidCookieOrDemoIdentity()
    {
        using var factory = new BrowserFactory();
        using var client = factory.NewClient();
        using var request = GetSessionRequest(factory.Cookie("person-a"));
        request.Headers.Authorization = new("Bearer", "invalid-token");
        request.Headers.Add(DemoUserCatalog.HeaderName, DemoUserCatalog.Users[0].Id.ToString());

        using var response = await client.SendAsync(request);
        var session = await response.Content.ReadFromJsonAsync<BrowserSessionResponse>();

        Assert.False(session!.Authenticated);
        Assert.Null(session.User);
        Assert.Null(session.CsrfToken);
    }

    [Fact]
    public async Task SignedBearer_StillUsesItsOwnIdentityWithoutCookieCsrf()
    {
        using var factory = new BrowserFactory();
        using var client = factory.NewClient();
        using var request = GetSessionRequest(factory.Cookie("person-a"));
        request.Headers.Authorization = new("Bearer", factory.Provider.Token("bearer-person", "api-audience", accessToken: true));

        using var response = await client.SendAsync(request);
        var session = await response.Content.ReadFromJsonAsync<BrowserSessionResponse>();

        Assert.True(session!.Authenticated);
        Assert.Null(session.CsrfToken);
        using var bearerOnly = new HttpRequestMessage(HttpMethod.Get, "/auth/session");
        bearerOnly.Headers.Authorization = request.Headers.Authorization;
        using var bearerResponse = await client.SendAsync(bearerOnly);
        var other = await bearerResponse.Content.ReadFromJsonAsync<BrowserSessionResponse>();
        Assert.Equal(session.User!.Id, other!.User!.Id);
    }

    [Fact]
    public async Task ForgedCookieAndDemoHeader_DoNotAuthenticateBrowserMode()
    {
        using var factory = new BrowserFactory();
        using var client = factory.NewClient();
        using var request = GetSessionRequest("__Host-Carpoolr.Session=forged");
        request.Headers.Add(DemoUserCatalog.HeaderName, DemoUserCatalog.Users[0].Id.ToString());

        using var response = await client.SendAsync(request);
        var session = await response.Content.ReadFromJsonAsync<BrowserSessionResponse>();

        Assert.False(session!.Authenticated);
        using var unauthorized = await client.PostAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Null(unauthorized.Headers.Location);
    }

    [Fact]
    public async Task HttpReverseProxyHop_CanIssueAndValidateSecureAntiforgeryCookies()
    {
        using var factory = new BrowserFactory();
        using var client = factory.NewClient();
        client.BaseAddress = new Uri("http://internal-api");
        using var sessionRequest = GetSessionRequest(factory.Cookie("person-a"));
        sessionRequest.Headers.Add("X-Forwarded-Proto", "http");
        using var sessionResponse = await client.SendAsync(sessionRequest);
        Assert.True(sessionResponse.IsSuccessStatusCode, await sessionResponse.Content.ReadAsStringAsync());
        var session = (await sessionResponse.Content.ReadFromJsonAsync<BrowserSessionResponse>())!;
        var cookies = factory.Cookie("person-a") + "; " + CookieHeader(sessionResponse.Headers.GetValues("Set-Cookie").Single());
        using var logoutRequest = PostRequest("/auth/logout", cookies, session.CsrfToken);

        using var logout = await client.SendAsync(logoutRequest);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.All(sessionResponse.Headers.GetValues("Set-Cookie"), value =>
            Assert.Contains("secure", value, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IdToken_CannotActAsBearerEvenWithTheSameAudienceAndValidSignature()
    {
        using var factory = new BrowserFactory(new Dictionary<string, string?>
        {
            ["Authentication:Audience"] = "browser-client",
        });
        using var client = factory.NewClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Provider.Token("person-a"));

        var rejected = await client.GetFromJsonAsync<BrowserSessionResponse>("/auth/session");

        Assert.False(rejected!.Authenticated);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Provider.Token("person-a", accessToken: true));
        var accepted = await client.GetFromJsonAsync<BrowserSessionResponse>("/auth/session");
        Assert.True(accepted!.Authenticated);
    }

    [Theory]
    [InlineData("Authentication:Browser:PublicOrigin", "http://app.carpoolr.test")]
    [InlineData("Authentication:Browser:PublicOrigin", "https://app.carpoolr.test/path")]
    [InlineData("Authentication:Browser:PublicOrigin", "https://user@app.carpoolr.test")]
    [InlineData("Authentication:Browser:ClientSecret", "")]
    [InlineData("Authentication:Authority", "http://auth.carpoolr.test")]
    [InlineData("DemoMode:Enabled", "true")]
    public void InvalidBrowserSettings_RefuseToStart(string key, string value)
    {
        using var factory = new BrowserFactory(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<InvalidOperationException>(() => factory.NewClient());
    }

    [Fact]
    public void DeployedBrowserSession_RequiresPersistentKeys()
    {
        using var factory = new BrowserFactory(environment: "Staging");

        var error = Assert.Throws<InvalidOperationException>(() => factory.NewClient());

        Assert.Contains("DataProtectionKeysPath", error.Message, StringComparison.Ordinal);
    }

    private static HttpRequestMessage GetSessionRequest(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/session");
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static HttpRequestMessage PostRequest(string path, string cookie, string? csrf = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Cookie", cookie);
        if (csrf is not null)
        {
            request.Headers.Add(BrowserAuthentication.CsrfHeader, csrf);
        }

        return request;
    }

    private static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(value =>
            value.StartsWith("__Host-Carpoolr.Session=", StringComparison.Ordinal));

    private static string CookieHeader(string setCookie) => setCookie.Split(';', 2)[0];
    private static string CookieValue(string setCookie) => CookieHeader(setCookie).Split('=', 2)[1];

    private sealed class BrowserFactory(
        IReadOnlyDictionary<string, string?>? overrides = null,
        string environment = "Development") : WebApplicationFactory<TripGrpcService>
    {
        public const string Issuer = "https://auth.carpoolr.test/application/o/carpoolr/";
        public const string PublicOrigin = "https://app.carpoolr.test";
        public FakeProvider Provider { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            var values = new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["DemoMode:Enabled"] = "false",
                ["Authentication:Authority"] = Issuer,
                ["Authentication:Audience"] = "api-audience",
                ["Authentication:Browser:Enabled"] = "true",
                ["Authentication:Browser:ClientId"] = "browser-client",
                ["Authentication:Browser:ClientSecret"] = "test-client-secret",
                ["Authentication:Browser:PublicOrigin"] = PublicOrigin,
                ["ConnectionStrings:Default"] = "Host=127.0.0.1;Database=unused-auth-tests;Username=unused",
            };
            foreach (var pair in overrides ?? new Dictionary<string, string?>())
            {
                values[pair.Key] = pair.Value;
            }

            foreach (var pair in values)
            {
                builder.UseSetting(pair.Key, pair.Value);
            }
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(values));
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<OpenIdConnectOptions>(BrowserAuthentication.OidcScheme, options =>
                {
                    options.Configuration = Provider.Configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(Provider.Configuration);
                    options.Backchannel = new HttpClient(Provider);
                });
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(Provider.Configuration));
            });
        }

        public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(PublicOrigin),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        public string Cookie(string subject, string issuer = Issuer)
        {
            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(BrowserAuthentication.SessionScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("iss", issuer),
                new Claim("sub", subject),
                new Claim("name", "Alex"),
            ], BrowserAuthentication.SessionScheme, "name", "role"));
            var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
            }, BrowserAuthentication.SessionScheme);
            return "__Host-Carpoolr.Session=" + options.TicketDataFormat.Protect(ticket);
        }

        public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string returnUrl = "/")
        {
            using var login = await client.GetAsync("/auth/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
            Assert.True(login.StatusCode == HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());
            var query = QueryHelpers.ParseQuery(login.Headers.Location!.Query);
            Provider.Nonce = query["nonce"].ToString();
            Provider.Challenge = query["code_challenge"].ToString();
            using var callback = new HttpRequestMessage(HttpMethod.Get,
                BrowserAuthentication.CallbackPath + "?code=test-code&state=" + Uri.EscapeDataString(query["state"]!));
            if (Provider.Scenario != "missing-correlation")
            {
                callback.Headers.Add("Cookie", string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(CookieHeader)));
            }

            return await client.SendAsync(callback);
        }
    }

    private sealed class FakeProvider : HttpMessageHandler
    {
        private readonly RsaSecurityKey signingKey = new(RSA.Create(2048)) { KeyId = "test-key" };
        public string Scenario { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public string Challenge { get; set; } = string.Empty;
        public bool PkceValidated { get; private set; }

        public OpenIdConnectConfiguration Configuration
        {
            get
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    Issuer = BrowserFactory.Issuer,
                    AuthorizationEndpoint = "https://auth.carpoolr.test/authorize",
                    TokenEndpoint = "https://auth.carpoolr.test/token",
                };
                configuration.SigningKeys.Add(signingKey);
                return configuration;
            }
        }

        public string Token(string subject, string audience = "browser-client", bool accessToken = false)
        {
            var key = Scenario == "invalid-signature" ? new RsaSecurityKey(RSA.Create(2048)) : signingKey;
            var claims = new List<Claim>
            {
                new("sub", Scenario == "oversized-subject" ? new string('x', 256) : subject),
                new("name", "Alex"),
                new("email", "private@example.invalid"),
                new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new("nonce", Scenario == "invalid-nonce" ? "invalid-nonce" : Nonce),
            };
            if (accessToken)
            {
                claims.Add(new Claim("azp", "browser-client"));
                claims.Add(new Claim("scope", "openid profile"));
            }
            var token = new JwtSecurityToken(
                Scenario == "invalid-issuer" ? "https://attacker.invalid" : BrowserFactory.Issuer,
                audience,
                claims,
                DateTime.UtcNow.AddMinutes(-1),
                DateTime.UtcNow.AddMinutes(10),
                new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://auth.carpoolr.test/token", request.RequestUri!.AbsoluteUri);
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("test-code", form["code"]);
            Assert.Equal("test-client-secret", form["client_secret"]);
            Assert.Equal(BrowserFactory.PublicOrigin + BrowserAuthentication.PublicCallbackPath, form["redirect_uri"]);
            PkceValidated = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]!))) == Challenge;
            Assert.True(PkceValidated);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    token_type = "Bearer",
                    access_token = "fake-access-token-not-to-be-retained",
                    id_token = Token("person-a"),
                    expires_in = 600,
                }), Encoding.UTF8, "application/json"),
            };
        }
    }
}
