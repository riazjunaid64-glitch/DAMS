using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMetaEventRecoveryAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastRetriedAt",
                table: "ExternalIntegrationEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastRetriedByUserId",
                table: "ExternalIntegrationEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "ExternalIntegrationEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRetriedAt",
                table: "ExternalIntegrationEvents");

            migrationBuilder.DropColumn(
                name: "LastRetriedByUserId",
                table: "ExternalIntegrationEvents");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "ExternalIntegrationEvents");
        }
    }
}
