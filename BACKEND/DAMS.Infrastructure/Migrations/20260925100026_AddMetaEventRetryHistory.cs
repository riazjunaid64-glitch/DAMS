using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMetaEventRetryHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalIntegrationEventRetries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalIntegrationEventId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIntegrationEventRetries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIntegrationEventRetries_ExternalIntegrationEvents_ExternalIntegrationEventId",
                        column: x => x.ExternalIntegrationEventId,
                        principalTable: "ExternalIntegrationEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalIntegrationEventRetries_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationEventRetries_ExternalIntegrationEventId_RequestedAt",
                table: "ExternalIntegrationEventRetries",
                columns: new[] { "ExternalIntegrationEventId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationEventRetries_RequestedByUserId",
                table: "ExternalIntegrationEventRetries",
                column: "RequestedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalIntegrationEventRetries");
        }
    }
}
