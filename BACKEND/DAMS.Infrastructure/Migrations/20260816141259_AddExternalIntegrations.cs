using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdAccountExternalId",
                table: "LeadExternalSubmissions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdExternalId",
                table: "LeadExternalSubmissions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdName",
                table: "LeadExternalSubmissions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdSetExternalId",
                table: "LeadExternalSubmissions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdSetName",
                table: "LeadExternalSubmissions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CampaignExternalId",
                table: "LeadExternalSubmissions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CampaignName",
                table: "LeadExternalSubmissions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalFormName",
                table: "LeadExternalSubmissions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExternalIntegrationConnectionId",
                table: "LeadExternalSubmissions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldDataJson",
                table: "LeadExternalSubmissions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PageExternalId",
                table: "LeadExternalSubmissions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PageName",
                table: "LeadExternalSubmissions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "LeadExternalSubmissions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawPayloadJson",
                table: "LeadExternalSubmissions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalIntegrationConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalAccountId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AccessTokenProtected = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GrantedScopesJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConnectedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastValidatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastErrorAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DisconnectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisconnectedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIntegrationConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIntegrationConnections_Users_ConnectedByUserId",
                        column: x => x.ConnectedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIntegrationOAuthStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StateHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnPath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIntegrationOAuthStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIntegrationOAuthStates_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIntegrationResources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalIntegrationConnectionId = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ExternalStatus = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSubscribed = table.Column<bool>(type: "bit", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResourceTokenProtected = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResourceTokenExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIntegrationResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIntegrationResources_ExternalIntegrationConnections_ExternalIntegrationConnectionId",
                        column: x => x.ExternalIntegrationConnectionId,
                        principalTable: "ExternalIntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIntegrationEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalIntegrationConnectionId = table.Column<int>(type: "int", nullable: true),
                    ExternalIntegrationResourceId = table.Column<int>(type: "int", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ResourceExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RawPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeadId = table.Column<int>(type: "int", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIntegrationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIntegrationEvents_ExternalIntegrationConnections_ExternalIntegrationConnectionId",
                        column: x => x.ExternalIntegrationConnectionId,
                        principalTable: "ExternalIntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalIntegrationEvents_ExternalIntegrationResources_ExternalIntegrationResourceId",
                        column: x => x.ExternalIntegrationResourceId,
                        principalTable: "ExternalIntegrationResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalIntegrationEvents_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "LeadSources",
                columns: new[] { "Id", "Code", "CreatedAt", "CustomerSource", "DisplayOrder", "IsActive", "IsSystem", "Name", "UpdatedAt" },
                values: new object[] { 14, "meta", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 4, 14, true, true, "Meta (unspecified)", null });

            migrationBuilder.CreateIndex(
                name: "IX_LeadExternalSubmissions_ExternalIntegrationConnectionId_ReceivedAt",
                table: "LeadExternalSubmissions",
                columns: new[] { "ExternalIntegrationConnectionId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationConnections_ConnectedByUserId",
                table: "ExternalIntegrationConnections",
                column: "ConnectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationConnections_Provider_ExternalAccountId",
                table: "ExternalIntegrationConnections",
                columns: new[] { "Provider", "ExternalAccountId" },
                unique: true,
                filter: "[ExternalAccountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationConnections_Provider_Status",
                table: "ExternalIntegrationConnections",
                columns: new[] { "Provider", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationEvents_ExternalIntegrationConnectionId_ReceivedAt",
                table: "ExternalIntegrationEvents",
                columns: new[] { "ExternalIntegrationConnectionId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationEvents_ExternalIntegrationResourceId",
                table: "ExternalIntegrationEvents",
                column: "ExternalIntegrationResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationEvents_LeadId",
                table: "ExternalIntegrationEvents",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationEvents_Provider_EventKey",
                table: "ExternalIntegrationEvents",
                columns: new[] { "Provider", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationEvents_Status_AvailableAt",
                table: "ExternalIntegrationEvents",
                columns: new[] { "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationOAuthStates_CreatedByUserId",
                table: "ExternalIntegrationOAuthStates",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationOAuthStates_ExpiresAt",
                table: "ExternalIntegrationOAuthStates",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationOAuthStates_StateHash",
                table: "ExternalIntegrationOAuthStates",
                column: "StateHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationResources_ExternalIntegrationConnectionId_ResourceType",
                table: "ExternalIntegrationResources",
                columns: new[] { "ExternalIntegrationConnectionId", "ResourceType" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationResources_ExternalIntegrationConnectionId_ResourceType_ExternalId",
                table: "ExternalIntegrationResources",
                columns: new[] { "ExternalIntegrationConnectionId", "ResourceType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationResources_ParentExternalId",
                table: "ExternalIntegrationResources",
                column: "ParentExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIntegrationResources_Provider_ResourceType_ExternalId",
                table: "ExternalIntegrationResources",
                columns: new[] { "Provider", "ResourceType", "ExternalId" });

            migrationBuilder.AddForeignKey(
                name: "FK_LeadExternalSubmissions_ExternalIntegrationConnections_ExternalIntegrationConnectionId",
                table: "LeadExternalSubmissions",
                column: "ExternalIntegrationConnectionId",
                principalTable: "ExternalIntegrationConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeadExternalSubmissions_ExternalIntegrationConnections_ExternalIntegrationConnectionId",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropTable(
                name: "ExternalIntegrationEvents");

            migrationBuilder.DropTable(
                name: "ExternalIntegrationOAuthStates");

            migrationBuilder.DropTable(
                name: "ExternalIntegrationResources");

            migrationBuilder.DropTable(
                name: "ExternalIntegrationConnections");

            migrationBuilder.DropIndex(
                name: "IX_LeadExternalSubmissions_ExternalIntegrationConnectionId_ReceivedAt",
                table: "LeadExternalSubmissions");

            migrationBuilder.DeleteData(
                table: "LeadSources",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DropColumn(
                name: "AdAccountExternalId",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "AdExternalId",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "AdName",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "AdSetExternalId",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "AdSetName",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "CampaignExternalId",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "CampaignName",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "ExternalFormName",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "ExternalIntegrationConnectionId",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "FieldDataJson",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "PageExternalId",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "PageName",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "LeadExternalSubmissions");

            migrationBuilder.DropColumn(
                name: "RawPayloadJson",
                table: "LeadExternalSubmissions");
        }
    }
}
