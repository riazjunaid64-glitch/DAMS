using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadIntakeHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadIntakeHolds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExternalLeadId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsExternal = table.Column<bool>(type: "bit", nullable: false),
                    CandidateLeadIds = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BookingRequestId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedLeadId = table.Column<int>(type: "int", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadIntakeHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadIntakeHolds_BookingRequests_BookingRequestId",
                        column: x => x.BookingRequestId,
                        principalTable: "BookingRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadIntakeHolds_Leads_ResolvedLeadId",
                        column: x => x.ResolvedLeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadIntakeHolds_BookingRequestId",
                table: "LeadIntakeHolds",
                column: "BookingRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadIntakeHolds_Provider_ExternalLeadId",
                table: "LeadIntakeHolds",
                columns: new[] { "Provider", "ExternalLeadId" },
                unique: true,
                filter: "[Provider] IS NOT NULL AND [ExternalLeadId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeadIntakeHolds_ResolvedLeadId",
                table: "LeadIntakeHolds",
                column: "ResolvedLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadIntakeHolds_Status_ReceivedAt",
                table: "LeadIntakeHolds",
                columns: new[] { "Status", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadIntakeHolds");
        }
    }
}
