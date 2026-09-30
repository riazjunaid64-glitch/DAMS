using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAndRefundProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FinancialEvidence_ExactlyOneOwner",
                table: "FinancialEvidence");

            migrationBuilder.AddColumn<int>(
                name: "CancellationRefundId",
                table: "FinancialEvidence",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerPaymentId",
                table: "FinancialEvidence",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvidence_CancellationRefundId",
                table: "FinancialEvidence",
                column: "CancellationRefundId",
                unique: true,
                filter: "[CancellationRefundId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvidence_CustomerPaymentId",
                table: "FinancialEvidence",
                column: "CustomerPaymentId",
                unique: true,
                filter: "[CustomerPaymentId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinancialEvidence_ExactlyOneOwner",
                table: "FinancialEvidence",
                sql: "(CASE WHEN [CommissionId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [PayoutId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [RebateId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [RebateDisbursementId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [CustomerPaymentId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [CancellationRefundId] IS NULL THEN 0 ELSE 1 END) = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialEvidence_BookingCancellationRefunds_CancellationRefundId",
                table: "FinancialEvidence",
                column: "CancellationRefundId",
                principalTable: "BookingCancellationRefunds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialEvidence_Payments_CustomerPaymentId",
                table: "FinancialEvidence",
                column: "CustomerPaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinancialEvidence_BookingCancellationRefunds_CancellationRefundId",
                table: "FinancialEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_FinancialEvidence_Payments_CustomerPaymentId",
                table: "FinancialEvidence");

            migrationBuilder.DropIndex(
                name: "IX_FinancialEvidence_CancellationRefundId",
                table: "FinancialEvidence");

            migrationBuilder.DropIndex(
                name: "IX_FinancialEvidence_CustomerPaymentId",
                table: "FinancialEvidence");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinancialEvidence_ExactlyOneOwner",
                table: "FinancialEvidence");

            migrationBuilder.DropColumn(
                name: "CancellationRefundId",
                table: "FinancialEvidence");

            migrationBuilder.DropColumn(
                name: "CustomerPaymentId",
                table: "FinancialEvidence");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinancialEvidence_ExactlyOneOwner",
                table: "FinancialEvidence",
                sql: "(CASE WHEN [CommissionId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [PayoutId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [RebateId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [RebateDisbursementId] IS NULL THEN 0 ELSE 1 END) = 1");
        }
    }
}
