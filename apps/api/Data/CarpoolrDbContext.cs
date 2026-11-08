using Carpoolr.Api.Features.Trips;
using Carpoolr.Api.Features.Profiles;
using Carpoolr.Api.Features.Bookings;
using Carpoolr.Api.Features.Billing;
using Carpoolr.Api.Features.RecurringTrips;
using Microsoft.EntityFrameworkCore;

namespace Carpoolr.Api.Data;

public sealed class CarpoolrDbContext(DbContextOptions<CarpoolrDbContext> options) : DbContext(options)
{
    public const string IdempotencyIndexName = "IX_Trips_OwnerIssuer_OwnerSubject_RequestId";

    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<UserProfile> Profiles => Set<UserProfile>();
    public DbSet<TripBooking> Bookings => Set<TripBooking>();
    public DbSet<RecurringSeries> Series => Set<RecurringSeries>();
    public DbSet<TripBill> Bills => Set<TripBill>();
    public DbSet<BillShare> BillShares => Set<BillShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var trip = modelBuilder.Entity<Trip>();

        trip.ToTable("Trips", table =>
        {
            table.HasCheckConstraint("CK_Trips_SeatsTotal", "\"SeatsTotal\" BETWEEN 1 AND 8");
            table.HasCheckConstraint("CK_Trips_State", "\"State\" IN (1, 2, 3)");
            table.HasCheckConstraint("CK_Trips_Origin", "length(trim(\"Origin\")) > 0");
            table.HasCheckConstraint("CK_Trips_Destination", "length(trim(\"Destination\")) > 0");
            table.HasCheckConstraint("CK_Trips_DepartureAt", "\"DepartureAt\" > \"CreatedAt\"");
        });

        trip.HasKey(value => value.Id);
        trip.Property(value => value.Id).ValueGeneratedNever();
        trip.Property(value => value.RequestId).IsRequired();
        trip.Property(value => value.OwnerIssuer).HasMaxLength(512).IsRequired();
        trip.Property(value => value.OwnerSubject).HasMaxLength(255).IsRequired();
        trip.Property(value => value.Origin).HasMaxLength(500).IsRequired();
        trip.Property(value => value.Destination).HasMaxLength(500).IsRequired();
        trip.Property(value => value.State).HasConversion<int>();

        trip.HasIndex(value => new { value.OwnerIssuer, value.OwnerSubject, value.RequestId })
            .IsUnique()
            .HasDatabaseName(IdempotencyIndexName);

        trip.HasIndex(value => new { value.State, value.DepartureAt, value.Id });
        trip.HasIndex(value => new { value.SeriesId, value.DepartureAt }).IsUnique()
            .HasFilter("\"SeriesId\" IS NOT NULL");
        trip.Property(value => value.DistanceKm).HasPrecision(12, 4);
        trip.Property(value => value.ConsumptionPer100Km).HasPrecision(12, 4);
        trip.Property(value => value.UnitPriceEur).HasPrecision(12, 4);
        trip.Property(value => value.VehicleName).HasMaxLength(100);
        trip.HasOne(value => value.DriverProfile).WithMany().HasForeignKey(value => value.DriverProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        trip.HasOne<RecurringSeries>().WithMany().HasForeignKey(value => value.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);

        var profile = modelBuilder.Entity<UserProfile>();
        profile.ToTable("Profiles");
        profile.HasKey(value => value.Id);
        profile.HasIndex(value => new { value.Issuer, value.Subject }).IsUnique();
        profile.Property(value => value.Issuer).HasMaxLength(512);
        profile.Property(value => value.Subject).HasMaxLength(255);
        profile.Property(value => value.DisplayName).HasMaxLength(100);
        profile.Property(value => value.PickupAddress).HasMaxLength(500);
        profile.OwnsOne(value => value.Vehicle, vehicle =>
        {
            vehicle.Property(value => value.Name).HasMaxLength(100);
            vehicle.Property(value => value.ConsumptionPer100Km).HasPrecision(12, 4);
            vehicle.Property(value => value.UnitPriceEur).HasPrecision(12, 4);
        });

        var booking = modelBuilder.Entity<TripBooking>();
        booking.ToTable("Bookings");
        booking.HasKey(value => value.Id);
        booking.HasOne(value => value.Trip).WithMany(value => value.Bookings).HasForeignKey(value => value.TripId)
            .OnDelete(DeleteBehavior.Restrict);
        booking.HasOne(value => value.Profile).WithMany().HasForeignKey(value => value.ProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        booking.HasIndex(value => new { value.ProfileId, value.RequestId }).IsUnique();
        booking.HasIndex(value => new { value.TripId, value.ProfileId }).IsUnique()
            .HasFilter("\"State\" IN (1, 2)");
        booking.HasIndex(value => new { value.TripId, value.State });

        var series = modelBuilder.Entity<RecurringSeries>();
        series.ToTable("RecurringSeries");
        series.HasKey(value => value.Id);
        series.HasIndex(value => new { value.OwnerIssuer, value.OwnerSubject, value.RequestId }).IsUnique();
        series.Property(value => value.OwnerIssuer).HasMaxLength(512);
        series.Property(value => value.OwnerSubject).HasMaxLength(255);
        series.Property(value => value.Origin).HasMaxLength(500);
        series.Property(value => value.Destination).HasMaxLength(500);
        series.Property(value => value.DistanceKm).HasPrecision(12, 4);

        var bill = modelBuilder.Entity<TripBill>();
        bill.ToTable("Bills");
        bill.HasKey(value => value.TripId);
        bill.HasOne(value => value.Trip).WithOne().HasForeignKey<TripBill>(value => value.TripId)
            .OnDelete(DeleteBehavior.Restrict);
        bill.Property(value => value.DistanceKm).HasPrecision(12, 4);
        bill.Property(value => value.ConsumptionPer100Km).HasPrecision(12, 4);
        bill.Property(value => value.UnitPriceEur).HasPrecision(12, 4);

        var share = modelBuilder.Entity<BillShare>();
        share.ToTable("BillShares");
        share.HasKey(value => new { value.TripId, value.ProfileId });
        share.HasOne(value => value.Bill).WithMany(value => value.Shares).HasForeignKey(value => value.TripId)
            .OnDelete(DeleteBehavior.Restrict);
        share.Property(value => value.Issuer).HasMaxLength(512);
        share.Property(value => value.Subject).HasMaxLength(255);
        share.Property(value => value.DisplayName).HasMaxLength(100);
    }
}
