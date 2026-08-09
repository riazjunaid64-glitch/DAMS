using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FilteredFinancialUniqueIndexesAndUnboundedAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerRebates_BookingId",
                table: "CustomerRebates");

            migrationBuilder.DropIndex(
                name: "IX_BookingCommissions_BookingId_PartnerId",
                table: "BookingCommissions");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "FinancialWorkflowAuditEntries",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "CustomerDocumentAuditEntries",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRebates_BookingId",
                table: "CustomerRebates",
                column: "BookingId",
                unique: true,
                filter: "[Status] <> 6 AND [Status] <> 7 AND [Status] <> 8");

            migrationBuilder.CreateIndex(
                name: "IX_BookingCommissions_BookingId_PartnerId",
                table: "BookingCommissions",
                columns: new[] { "BookingId", "PartnerId" },
                unique: true,
                filter: "[Status] <> 7 AND [Status] <> 8 AND [Status] <> 10");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerRebates_BookingId",
                table: "CustomerRebates");

            migrationBuilder.DropIndex(
                name: "IX_BookingCommissions_BookingId_PartnerId",
                table: "BookingCommissions");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "FinancialWorkflowAuditEntries",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "CustomerDocumentAuditEntries",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRebates_BookingId",
                table: "CustomerRebates",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingCommissions_BookingId_PartnerId",
                table: "BookingCommissions",
                columns: new[] { "BookingId", "PartnerId" },
                unique: true);
        }
    }
}
