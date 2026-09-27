using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecordUnmatchedUnitTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnitTypeMigrationNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UnitType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NotedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitTypeMigrationNotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnitTypeMigrationNotes_UnitType",
                table: "UnitTypeMigrationNotes",
                column: "UnitType",
                unique: true);

            // Repeat the rewrite so a database that already applied the earlier
            // case-insensitive filter still gets 1 BED and studio corrected.
            // Unmatched types are stored here; EF does not show PRINT from Migrate().
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

                INSERT INTO UnitTypeMigrationNotes (UnitType, NotedAt)
                SELECT DISTINCT UnitType, SYSUTCDATETIME()
                FROM Units
                WHERE UnitType COLLATE Latin1_General_CS_AS
                    NOT IN (N'Studio', N'1 Bed', N'2 Bed', N'3 Bed', N'Parking space');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnitTypeMigrationNotes");
        }
    }
}
