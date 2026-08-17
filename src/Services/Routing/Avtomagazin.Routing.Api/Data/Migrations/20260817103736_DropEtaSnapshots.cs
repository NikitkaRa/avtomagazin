using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avtomagazin.Routing.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropEtaSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EtaSnapshots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EtaSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DistanceKm = table.Column<double>(type: "double precision", nullable: true),
                    EstimatedArrivalUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MinutesUntilArrival = table.Column<int>(type: "integer", nullable: false),
                    RouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementName = table.Column<string>(type: "text", nullable: false),
                    StopId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtaSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EtaSnapshots_StopId_EstimatedArrivalUtc",
                table: "EtaSnapshots",
                columns: new[] { "StopId", "EstimatedArrivalUtc" });
        }
    }
}
