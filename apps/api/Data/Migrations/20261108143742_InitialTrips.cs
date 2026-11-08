using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carpoolr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerIssuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Origin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Destination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DepartureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SeatsTotal = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                    table.CheckConstraint("CK_Trips_DepartureAt", "\"DepartureAt\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_Trips_Destination", "length(trim(\"Destination\")) > 0");
                    table.CheckConstraint("CK_Trips_Origin", "length(trim(\"Origin\")) > 0");
                    table.CheckConstraint("CK_Trips_SeatsTotal", "\"SeatsTotal\" BETWEEN 1 AND 8");
                    table.CheckConstraint("CK_Trips_State", "\"State\" IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_OwnerIssuer_OwnerSubject_RequestId",
                table: "Trips",
                columns: new[] { "OwnerIssuer", "OwnerSubject", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_State_DepartureAt_Id",
                table: "Trips",
                columns: new[] { "State", "DepartureAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Trips");
        }
    }
}
