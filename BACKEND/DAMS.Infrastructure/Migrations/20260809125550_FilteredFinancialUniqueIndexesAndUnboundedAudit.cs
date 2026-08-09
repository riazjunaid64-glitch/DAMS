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

            // A database may now contain multiple closed historical records per booking/partner and
            // audit text longer than 2,000 characters. Restoring the old unique constraints or
            // narrowing the audit columns would either fail or destroy production history. Keep the
            // widened columns and restore broad, non-unique lookup indexes so an application rollback
            // remains usable without discarding records. Re-applying Up safely replaces these indexes.
            migrationBuilder.CreateIndex(
                name: "IX_CustomerRebates_BookingId",
                table: "CustomerRebates",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingCommissions_BookingId_PartnerId",
                table: "BookingCommissions",
                columns: new[] { "BookingId", "PartnerId" });
        }
    }
}
