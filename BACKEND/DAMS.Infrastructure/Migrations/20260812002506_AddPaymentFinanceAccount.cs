using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentFinanceAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FinanceAccountId",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_FinanceAccountId",
                table: "Payments",
                column: "FinanceAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_FinanceAccounts_FinanceAccountId",
                table: "Payments",
                column: "FinanceAccountId",
                principalTable: "FinanceAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_FinanceAccounts_FinanceAccountId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_FinanceAccountId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "FinanceAccountId",
                table: "Payments");
        }
    }
}
