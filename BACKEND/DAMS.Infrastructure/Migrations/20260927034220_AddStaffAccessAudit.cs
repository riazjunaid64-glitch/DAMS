using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffAccessAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffAccessAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PerformedByUserId = table.Column<int>(type: "int", nullable: false),
                    AccessEnabled = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAccessAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAccessAudits_EmployeeId_OccurredAt",
                table: "StaffAccessAudits",
                columns: new[] { "EmployeeId", "OccurredAt" });

            // No WHERE: under a case-insensitive collation '1 BED' equals '1 Bed', so a
            // filter on the canonical spellings would skip the values this rewrite exists
            // to fix. The CASE keeps anything it does not recognise.
            // Unmatched values are recorded by the following migration, not PRINT, because
            // EF does not surface PRINT from database update.
            migrationBuilder.Sql("""
                UPDATE Units
                SET UnitType = CASE LOWER(REPLACE(REPLACE(UnitType, ' ', ''), '-', ''))
                    WHEN 'studio' THEN 'Studio'
                    WHEN '1bed' THEN '1 Bed'
                    WHEN '1bedroom' THEN '1 Bed'
                    WHEN '1bhk' THEN '1 Bed'
                    WHEN '2bed' THEN '2 Bed'
                    WHEN '2bedroom' THEN '2 Bed'
                    WHEN '2bhk' THEN '2 Bed'
                    WHEN '3bed' THEN '3 Bed'
                    WHEN '3bedroom' THEN '3 Bed'
                    WHEN '3bhk' THEN '3 Bed'
                    WHEN 'parkingspace' THEN 'Parking space'
                    WHEN 'parking' THEN 'Parking space'
                    ELSE UnitType
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffAccessAudits");
        }
    }
}
