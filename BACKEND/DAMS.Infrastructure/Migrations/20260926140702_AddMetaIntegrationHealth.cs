using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMetaIntegrationHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExternalIntegrationEvents_ExternalIntegrationResourceId",
                table: "ExternalIntegrationEvents");

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncRejectedAt",
                table: "ExternalIntegrationConnections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationEvents_ExternalIntegrationResourceId_ReceivedAt",
                table: "ExternalIntegrationEvents",
                columns: new[] { "ExternalIntegrationResourceId", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExternalIntegrationEvents_ExternalIntegrationResourceId_ReceivedAt",
                table: "ExternalIntegrationEvents");

            migrationBuilder.DropColumn(
                name: "SyncRejectedAt",
                table: "ExternalIntegrationConnections");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationEvents_ExternalIntegrationResourceId",
                table: "ExternalIntegrationEvents",
                column: "ExternalIntegrationResourceId");
        }
    }
}
