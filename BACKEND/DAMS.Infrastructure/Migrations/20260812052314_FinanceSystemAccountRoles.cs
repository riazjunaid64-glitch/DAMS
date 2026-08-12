using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinanceSystemAccountRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SystemRole",
                table: "FinanceAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                ;WITH Candidate AS
                (
                    SELECT TOP (1) [Id]
                    FROM [FinanceAccounts]
                    WHERE [Type] = 5
                      AND ([LedgerCode] = N'11' OR [Name] = N'Tax Payable')
                    ORDER BY CASE WHEN [Name] = N'Tax Payable' THEN 0 ELSE 1 END, [Id]
                )
                UPDATE [FinanceAccounts]
                SET [SystemRole] = 1
                WHERE [Id] IN (SELECT [Id] FROM Candidate);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAccounts_SystemRole",
                table: "FinanceAccounts",
                column: "SystemRole",
                unique: true,
                filter: "[SystemRole] <> 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] >= 0 AND [SystemRole] <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_TaxPayableRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] <> 1 OR [Type] = 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinanceAccounts_SystemRole",
                table: "FinanceAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_TaxPayableRole",
                table: "FinanceAccounts");

            migrationBuilder.DropColumn(
                name: "SystemRole",
                table: "FinanceAccounts");
        }
    }
}
