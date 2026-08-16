using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCancellationSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "AssetPurchases",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "rowversion",
                oldRowVersion: true,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "BookingCancellationSettlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    CustomerCashReceivedSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RetainedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundDecision = table.Column<int>(type: "int", nullable: false),
                    RefundPayableAccountId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: false),
                    CancelledByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingCancellationSettlements", x => x.Id);
                    table.CheckConstraint("CK_BookingCancellationSettlements_Amounts", "[CustomerCashReceivedSnapshot] >= 0 AND [RefundAmount] >= 0 AND [RetainedAmount] >= 0 AND [RefundAmount] + [RetainedAmount] = [CustomerCashReceivedSnapshot]");
                    table.CheckConstraint("CK_BookingCancellationSettlements_DecisionConsistency", "([RefundAmount] = 0 AND [RefundDecision] = 0 AND [RefundPayableAccountId] IS NULL) OR ([RefundAmount] > 0 AND [RefundDecision] IN (1, 2) AND [RefundPayableAccountId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_BookingCancellationSettlements_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingCancellationSettlements_FinanceAccounts_RefundPayableAccountId",
                        column: x => x.RefundPayableAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingCancellationRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SettlementId = table.Column<int>(type: "int", nullable: false),
                    FinanceAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: false),
                    RecordedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingCancellationRefunds", x => x.Id);
                    table.CheckConstraint("CK_BookingCancellationRefunds_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_BookingCancellationRefunds_BookingCancellationSettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "BookingCancellationSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingCancellationRefunds_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_CustomerRefundPayableRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] <> 2 OR [Type] = 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] >= 0 AND [SystemRole] <= 2");

            migrationBuilder.CreateIndex(
                name: "IX_BookingCancellationRefunds_FinanceAccountId",
                table: "BookingCancellationRefunds",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingCancellationRefunds_IdempotencyKey",
                table: "BookingCancellationRefunds",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingCancellationRefunds_SettlementId",
                table: "BookingCancellationRefunds",
                column: "SettlementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingCancellationSettlements_BookingId",
                table: "BookingCancellationSettlements",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingCancellationSettlements_IdempotencyKey",
                table: "BookingCancellationSettlements",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingCancellationSettlements_RefundPayableAccountId",
                table: "BookingCancellationSettlements",
                column: "RefundPayableAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingCancellationRefunds");

            migrationBuilder.DropTable(
                name: "BookingCancellationSettlements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_CustomerRefundPayableRole",
                table: "FinanceAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "AssetPurchases",
                type: "rowversion",
                rowVersion: true,
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "rowversion",
                oldRowVersion: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] >= 0 AND [SystemRole] <= 1");
        }
    }
}
