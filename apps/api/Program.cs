using Carpoolr.Api.Data;
using Carpoolr.Api.Features.Trips;
using Carpoolr.Api.Features.Billing;
using Carpoolr.Api.Features.Bookings;
using Carpoolr.Api.Features.Dashboard;
using Carpoolr.Api.Features.Profiles;
using Carpoolr.Api.Features.RecurringTrips;
using Carpoolr.Api.Grpc;
using Carpoolr.Api.Health;
using Carpoolr.Api.Infrastructure;
using Carpoolr.Api.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

namespace Carpoolr.Api;

public static partial class Program
{
    public static async Task Main(string[] args)
    {
        var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);
        var bootstrapOnly = args.Contains("--bootstrap-staging-database", StringComparer.Ordinal);
        var builder = WebApplication.CreateBuilder(args
            .Where(value => value is not "--migrate" and not "--bootstrap-staging-database").ToArray());

        if (bootstrapOnly)
        {
            await StagingDatabaseBootstrap.RunAsync(builder.Environment);
            return;
        }

        builder.Services.AddControllers();
        builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<CarpoolrDbContext>(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("Configure ConnectionStrings:Default.");
            options.UseNpgsql(connectionString);
        });
        builder.Services.AddScoped<TripApplicationService>();
        builder.Services.AddScoped<ProfileApplicationService>();
        builder.Services.AddScoped<BookingApplicationService>();
        builder.Services.AddScoped<RecurringTripApplicationService>();
        builder.Services.AddScoped<BillingApplicationService>();
        builder.Services.AddScoped<DashboardApplicationService>();
        builder.Services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>(
            "database",
            tags: ["ready"],
            timeout: TimeSpan.FromSeconds(3));
        builder.Services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaxReceiveMessageSize = 64 * 1024;
            options.MaxSendMessageSize = 1024 * 1024;
            options.Interceptors.Add<GrpcExceptionInterceptor>();
        });

        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .WithMethods("GET", "POST", "OPTIONS")
                    .WithExposedHeaders(
                        "Grpc-Status", "Grpc-Message", "Grpc-Encoding",
                        "Grpc-Accept-Encoding", "Grpc-Status-Details-Bin", "trace-id")));

        var app = builder.Build();

        if (migrateOnly)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<CarpoolrDbContext>();
            await database.Database.MigrateAsync();
            return;
        }

        app.UseRouting();
        app.UseGrpcWeb();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        if (builder.Configuration.GetValue<bool>("DemoMode:Enabled"))
        {
            app.MapGet("/demo/users", () => Results.Ok(new DemoUsersResponse(true, DemoUserCatalog.Users)))
                .AllowAnonymous();
        }

        app.MapControllers();
        app.MapHealthChecks("/readyz", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        }).AllowAnonymous();
        app.MapGrpcService<TripGrpcService>().EnableGrpcWeb();
        app.MapGrpcService<ProfileGrpcService>().EnableGrpcWeb();
        app.MapGrpcService<BookingGrpcService>().EnableGrpcWeb();
        app.MapGrpcService<RecurringTripGrpcService>().EnableGrpcWeb();
        app.MapGrpcService<BillingGrpcService>().EnableGrpcWeb();
        app.MapGrpcService<DashboardGrpcService>().EnableGrpcWeb();

        await app.RunAsync();
    }
}
