using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMovementAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAttachments_ExactlyOneOwner",
                table: "FinanceAttachments");

            migrationBuilder.AddColumn<int>(
                name: "CapitalTransactionId",
                table: "FinanceAttachments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoanTransactionId",
                table: "FinanceAttachments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StaffCashTransferId",
                table: "FinanceAttachments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAttachments_CapitalTransactionId",
                table: "FinanceAttachments",
                column: "CapitalTransactionId",
                unique: true,
                filter: "[CapitalTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAttachments_LoanTransactionId",
                table: "FinanceAttachments",
                column: "LoanTransactionId",
                unique: true,
                filter: "[LoanTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAttachments_StaffCashTransferId",
                table: "FinanceAttachments",
                column: "StaffCashTransferId",
                unique: true,
                filter: "[StaffCashTransferId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAttachments_ExactlyOneOwner",
                table: "FinanceAttachments",
                sql: "(CASE WHEN [ManualRevenueId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [ExpenseId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [AssetPurchaseId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LoanTransactionId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [CapitalTransactionId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [StaffCashTransferId] IS NULL THEN 0 ELSE 1 END) = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_FinanceAttachments_CapitalTransactions_CapitalTransactionId",
                table: "FinanceAttachments",
                column: "CapitalTransactionId",
                principalTable: "CapitalTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FinanceAttachments_LoanTransactions_LoanTransactionId",
                table: "FinanceAttachments",
                column: "LoanTransactionId",
                principalTable: "LoanTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FinanceAttachments_StaffCashTransfers_StaffCashTransferId",
                table: "FinanceAttachments",
                column: "StaffCashTransferId",
                principalTable: "StaffCashTransfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinanceAttachments_CapitalTransactions_CapitalTransactionId",
                table: "FinanceAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_FinanceAttachments_LoanTransactions_LoanTransactionId",
                table: "FinanceAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_FinanceAttachments_StaffCashTransfers_StaffCashTransferId",
                table: "FinanceAttachments");

            migrationBuilder.DropIndex(
                name: "IX_FinanceAttachments_CapitalTransactionId",
                table: "FinanceAttachments");

            migrationBuilder.DropIndex(
                name: "IX_FinanceAttachments_LoanTransactionId",
                table: "FinanceAttachments");

            migrationBuilder.DropIndex(
                name: "IX_FinanceAttachments_StaffCashTransferId",
                table: "FinanceAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAttachments_ExactlyOneOwner",
                table: "FinanceAttachments");

            migrationBuilder.DropColumn(
                name: "CapitalTransactionId",
                table: "FinanceAttachments");

            migrationBuilder.DropColumn(
                name: "LoanTransactionId",
                table: "FinanceAttachments");

            migrationBuilder.DropColumn(
                name: "StaffCashTransferId",
                table: "FinanceAttachments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAttachments_ExactlyOneOwner",
                table: "FinanceAttachments",
                sql: "(CASE WHEN [ManualRevenueId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [ExpenseId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [AssetPurchaseId] IS NULL THEN 0 ELSE 1 END) = 1");
        }
    }
}
