using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Data;
using Carpoolr.Api.Grpc;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Carpoolr.Api.Tests;

/// <summary>
/// Runs the complete HTTP/gRPC-Web/JWT pipeline against a dedicated PostgreSQL database.
/// The database is supplied by the caller; this fixture never deletes user databases.
/// </summary>
public sealed class GrpcApiFixture : IAsyncLifetime
{
    public const string Issuer = "https://issuer-a.carpoolr.test";
    public const string OtherIssuer = "https://issuer-b.carpoolr.test";
    public const string Audience = "carpoolr-tests";

    private static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(64));
    private static readonly SemaphoreSlim MigrationLock = new(1, 1);

    public ApiFactory Factory { get; } = new();

    public async Task InitializeAsync()
    {
        // Different test classes create their own fixture against the same database.
        // Serialize migration discovery and execution so a fresh database is migrated once.
        await MigrationLock.WaitAsync();
        try
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<CarpoolrDbContext>();
            await database.Database.MigrateAsync();
        }
        finally
        {
            MigrationLock.Release();
        }
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
    }

    public GrpcTestClient CreateClient(string? token = null) => CreateClient(Factory, token);

    public GrpcTestClient CreateNativeClient(string? token = null) => CreateClient(Factory, token, grpcWeb: false);

    public static GrpcTestClient CreateClient(
        WebApplicationFactory<TripGrpcService> factory,
        string? token,
        bool grpcWeb = true)
    {
        HttpMessageHandler handler = factory.Server.CreateHandler();
        if (grpcWeb)
        {
            handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, handler);
        }

        var httpClient = new HttpClient(handler);
        if (token is not null)
        {
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", token);
        }

        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpClient = httpClient,
            HttpVersion = grpcWeb ? HttpVersion.Version11 : HttpVersion.Version20,
            HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            DisposeHttpClient = true,
        });

        return new GrpcTestClient(channel);
    }

    public static string CreateToken(
        string? subject = "driver",
        string? issuer = Issuer,
        string? audience = Audience,
        bool expired = false,
        bool invalidSignature = false)
    {
        var now = DateTime.UtcNow;
        var claims = subject is null ? Array.Empty<Claim>() : [new Claim("sub", subject)];
        var key = invalidSignature
            ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64))
            : SigningKey;
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore: now.AddHours(-2),
            expires: expired ? now.AddHours(-1) : now.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Any public type in the API assembly identifies its real entry point.
    public sealed class ApiFactory : WebApplicationFactory<TripGrpcService>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connectionString = Environment.GetEnvironmentVariable("CARPOOLR_TEST_POSTGRES")
                ?? "Host=127.0.0.1;Port=55432;Database=carpoolr_tests;Username=postgres;Password=local-tests-only";

            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString }));

            builder.ConfigureTestServices(services =>
            {
                // Replace only trusted test issuer/key configuration; requests still pass through
                // the real JWT validator, authorization policies, and HTTP middleware.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = string.Empty;
                    options.MetadataAddress = string.Empty;
                    options.MapInboundClaims = false;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration { Issuer = Issuer });
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuers = [Issuer, OtherIssuer],
                        ValidateAudience = true,
                        ValidAudience = Audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = SigningKey,
                        ValidateLifetime = true,
                        RequireExpirationTime = true,
                        RequireSignedTokens = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                    };
                });
            });
        }
    }
}

public sealed class GrpcTestClient(GrpcChannel channel) : IDisposable
{
    public TripService.TripServiceClient Trips { get; } = new(channel);
    public ProfileService.ProfileServiceClient Profiles { get; } = new(channel);

    public void Dispose() => channel.Dispose();
}
