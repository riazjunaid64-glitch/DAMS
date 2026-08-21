using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// The last mutable finance record without a concurrency token. Paying a salary writes a real
    /// Expense, and a salary correction rewrites the amount, the pay date, the payroll period and
    /// that linked expense together — so two admins editing the same record produced one silent
    /// winner, with the loser's figures overwritten in full and both told they had succeeded.
    /// <para>
    /// Safe on live data with no backfill: SQL Server stamps a <c>rowversion</c> on every existing
    /// row the moment the column is added, so no salary is left with an empty token that
    /// <c>EmployeeService.ApplySalaryRowVersion</c> would have to wave through.
    /// </para>
    /// </summary>
    public partial class AddEmployeeSalaryRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "EmployeeSalaries",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "EmployeeSalaries");
        }
    }
}
