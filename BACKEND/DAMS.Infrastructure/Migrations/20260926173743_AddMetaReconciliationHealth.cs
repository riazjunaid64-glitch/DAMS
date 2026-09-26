using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMetaReconciliationHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReconciledAt",
                table: "ExternalIntegrationConnections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReconciliationError",
                table: "ExternalIntegrationConnections",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReconciliationFailingSince",
                table: "ExternalIntegrationConnections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReconciliationMissedLeads",
                table: "ExternalIntegrationConnections",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReconciledAt",
                table: "ExternalIntegrationConnections");

            migrationBuilder.DropColumn(
                name: "ReconciliationError",
                table: "ExternalIntegrationConnections");

            migrationBuilder.DropColumn(
                name: "ReconciliationFailingSince",
                table: "ExternalIntegrationConnections");

            migrationBuilder.DropColumn(
                name: "ReconciliationMissedLeads",
                table: "ExternalIntegrationConnections");
        }
    }
}
