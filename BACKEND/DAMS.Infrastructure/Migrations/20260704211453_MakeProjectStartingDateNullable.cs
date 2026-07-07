using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeProjectStartingDateNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeSalaries_EmployeeId_PayYear_PayMonth",
                table: "EmployeeSalaries");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_UnitId",
                table: "Bookings");

            migrationBuilder.AlterColumn<DateTime>(
                name: "StartingDate",
                table: "Projects",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaries_EmployeeId_PayYear_PayMonth",
                table: "EmployeeSalaries",
                columns: new[] { "EmployeeId", "PayYear", "PayMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UnitId_Active",
                table: "Bookings",
                column: "UnitId",
                unique: true,
                filter: "[Status] <> 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeSalaries_EmployeeId_PayYear_PayMonth",
                table: "EmployeeSalaries");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_UnitId_Active",
                table: "Bookings");

            migrationBuilder.AlterColumn<DateTime>(
                name: "StartingDate",
                table: "Projects",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaries_EmployeeId_PayYear_PayMonth",
                table: "EmployeeSalaries",
                columns: new[] { "EmployeeId", "PayYear", "PayMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UnitId",
                table: "Bookings",
                column: "UnitId");
        }
    }
}
