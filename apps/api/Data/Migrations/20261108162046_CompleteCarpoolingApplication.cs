using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carpoolr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompleteCarpoolingApplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Trips_State",
                table: "Trips");

            migrationBuilder.AlterColumn<string>(
                name: "Origin",
                table: "Trips",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Destination",
                table: "Trips",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<decimal>(
                name: "ConsumptionPer100Km",
                table: "Trips",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DistanceKm",
                table: "Trips",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "DriverProfileId",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EnergyKind",
                table: "Trips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPriceEur",
                table: "Trips",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "VehicleName",
                table: "Trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Bills",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    DistanceKm = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    ConsumptionPer100Km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    UnitPriceEur = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    TotalCents = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bills", x => x.TripId);
                    table.ForeignKey(
                        name: "FK_Bills_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PickupAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Vehicle_Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Vehicle_PassengerSeats = table.Column<int>(type: "integer", nullable: true),
                    Vehicle_EnergyKind = table.Column<int>(type: "integer", nullable: true),
                    Vehicle_ConsumptionPer100Km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    Vehicle_UnitPriceEur = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecurringSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerIssuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Origin = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Destination = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SeatsTotal = table.Column<int>(type: "integer", nullable: false),
                    DistanceKm = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LocalDepartureTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Weekdays = table.Column<int[]>(type: "integer[]", nullable: false),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSeries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillShares",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    IsDriver = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentState = table.Column<int>(type: "integer", nullable: false),
                    LastReportId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillShares", x => new { x.TripId, x.ProfileId });
                    table.ForeignKey(
                        name: "FK_BillShares_Bills_TripId",
                        column: x => x.TripId,
                        principalTable: "Bills",
                        principalColumn: "TripId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookings_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DriverProfileId",
                table: "Trips",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_SeriesId_DepartureAt",
                table: "Trips",
                columns: new[] { "SeriesId", "DepartureAt" },
                unique: true,
                filter: "\"SeriesId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Trips_State",
                table: "Trips",
                sql: "\"State\" IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ProfileId_RequestId",
                table: "Bookings",
                columns: new[] { "ProfileId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_TripId_ProfileId",
                table: "Bookings",
                columns: new[] { "TripId", "ProfileId" },
                unique: true,
                filter: "\"State\" IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_TripId_State",
                table: "Bookings",
                columns: new[] { "TripId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_Issuer_Subject",
                table: "Profiles",
                columns: new[] { "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSeries_OwnerIssuer_OwnerSubject_RequestId",
                table: "RecurringSeries",
                columns: new[] { "OwnerIssuer", "OwnerSubject", "RequestId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_Profiles_DriverProfileId",
                table: "Trips",
                column: "DriverProfileId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_RecurringSeries_SeriesId",
                table: "Trips",
                column: "SeriesId",
                principalTable: "RecurringSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trips_Profiles_DriverProfileId",
                table: "Trips");

            migrationBuilder.DropForeignKey(
                name: "FK_Trips_RecurringSeries_SeriesId",
                table: "Trips");

            migrationBuilder.DropTable(
                name: "BillShares");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "RecurringSeries");

            migrationBuilder.DropTable(
                name: "Bills");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropIndex(
                name: "IX_Trips_DriverProfileId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_SeriesId_DepartureAt",
                table: "Trips");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Trips_State",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ConsumptionPer100Km",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DistanceKm",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DriverProfileId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "EnergyKind",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "UnitPriceEur",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "VehicleName",
                table: "Trips");

            migrationBuilder.AlterColumn<string>(
                name: "Origin",
                table: "Trips",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Destination",
                table: "Trips",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Trips_State",
                table: "Trips",
                sql: "\"State\" IN (1, 2)");
        }
    }
}
