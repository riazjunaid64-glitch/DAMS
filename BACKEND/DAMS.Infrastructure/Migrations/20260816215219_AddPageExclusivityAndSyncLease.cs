using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPageExclusivityAndSyncLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SyncLockedBy",
                table: "ExternalIntegrationConnections",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncLockedUntil",
                table: "ExternalIntegrationConnections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_ExternalIntegrationResources_EnabledFacebookPage",
                table: "ExternalIntegrationResources",
                columns: new[] { "Provider", "ResourceType", "ExternalId" },
                unique: true,
                filter: "[Provider] = 'meta' AND [ResourceType] = 'facebook_page' AND [IsEnabled] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ExternalIntegrationResources_EnabledFacebookPage",
                table: "ExternalIntegrationResources");

            migrationBuilder.DropColumn(
                name: "SyncLockedBy",
                table: "ExternalIntegrationConnections");

            migrationBuilder.DropColumn(
                name: "SyncLockedUntil",
                table: "ExternalIntegrationConnections");
        }
    }
}
