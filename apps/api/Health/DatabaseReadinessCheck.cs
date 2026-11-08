using Carpoolr.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Carpoolr.Api.Health;

/// <summary>
/// Verifies that this API version can use its database without changing its schema.
/// </summary>
public sealed class DatabaseReadinessCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseReadinessCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<CarpoolrDbContext>();

            if (!await database.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("The database is not ready.");
            }

            var pendingMigrations = await database.Database.GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Any())
            {
                return HealthCheckResult.Unhealthy("The database schema is not ready.");
            }

            // Connectivity and migration history alone cannot detect a missing table or
            // column. Bounded queries verify mapped columns in every application table,
            // including the profile's owned vehicle, even when all tables are empty.
            await database.Trips.AsNoTracking().OrderBy(trip => trip.Id).Take(1).ToListAsync(cancellationToken);
            await database.Profiles.AsNoTracking().OrderBy(profile => profile.Id).Take(1).ToListAsync(cancellationToken);
            await database.Bookings.AsNoTracking().OrderBy(booking => booking.Id).Take(1).ToListAsync(cancellationToken);
            await database.Series.AsNoTracking().OrderBy(series => series.Id).Take(1).ToListAsync(cancellationToken);
            await database.Bills.AsNoTracking().OrderBy(bill => bill.TripId).Take(1).ToListAsync(cancellationToken);
            await database.BillShares.AsNoTracking().OrderBy(share => share.TripId).ThenBy(share => share.ProfileId)
                .Take(1).ToListAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The database readiness check timed out or was cancelled.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Database readiness check failed.");
            return HealthCheckResult.Unhealthy("The database is not ready.");
        }
    }
}
