using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadAlertChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadAlertChecks",
                columns: table => new
                {
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    FirstContactAssignmentCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InactivityCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadAlertChecks", x => x.LeadId);
                    table.ForeignKey(
                        name: "FK_LeadAlertChecks_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadAlertChecks");
        }
    }
}
