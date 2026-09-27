using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RequeueTransientMetaLeadFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FailureWasTransient",
                table: "ExternalIntegrationEvents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RequeueCount",
                table: "ExternalIntegrationEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationEvents_ExternalIntegrationConnectionId_Status_ProcessedAt",
                table: "ExternalIntegrationEvents",
                columns: new[] { "ExternalIntegrationConnectionId", "Status", "ProcessedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExternalIntegrationEvents_ExternalIntegrationConnectionId_Status_ProcessedAt",
                table: "ExternalIntegrationEvents");

            migrationBuilder.DropColumn(
                name: "FailureWasTransient",
                table: "ExternalIntegrationEvents");

            migrationBuilder.DropColumn(
                name: "RequeueCount",
                table: "ExternalIntegrationEvents");
        }
    }
}
