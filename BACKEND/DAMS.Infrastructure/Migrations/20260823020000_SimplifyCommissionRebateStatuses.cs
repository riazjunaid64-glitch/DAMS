using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// Collapses the commission and rebate approval ladder into a single open state. Commissions
    /// become Pending(0)/Paid(1)/Cancelled(2)/ReversalRequired(3)/Reversed(4); rebates become
    /// Pending(0)/Applied(1)/Paid(2)/Cancelled(3)/ReversalRequired(4)/Reversed(5). Draft, submitted,
    /// approved, earned, rejected and the two part-paid states no longer exist: how much has been
    /// paid or applied is read from the payout and disbursement rows, never from the status.
    ///
    /// Every existing row is remapped in one statement per table, so the old and new numbering never
    /// overlap mid-update. The two filtered unique indexes are rebuilt because their filters spell
    /// out the closed statuses by number.
    ///
    /// The columns the approval ladder used are dropped separately, in
    /// <c>20260823030000_DropCommissionApprovalColumns</c>, because this migration had already been
    /// applied by the time they were removed from the model.
    /// </summary>
    public partial class SimplifyCommissionRebateStatuses : Migration
    {
        // Old BookingCommissionStatus: Draft 0, PendingApproval 1, Approved 2, Earned 3, Payable 4,
        // PartiallyPaid 5, Paid 6, Rejected 7, Cancelled 8, ReversalRequired 9, Reversed 10.
        private const string CommissionToNew = @"
            CASE [{0}]
                WHEN 6 THEN 1
                WHEN 7 THEN 2
                WHEN 8 THEN 2
                WHEN 9 THEN 3
                WHEN 10 THEN 4
                ELSE 0
            END";

        // Old CustomerRebateStatus: Draft 0, PendingApproval 1, Approved 2, PartiallyApplied 3,
        // Applied 4, Paid 5, Rejected 6, Cancelled 7, Reversed 8, ReversalRequired 9.
        private const string RebateToNew = @"
            CASE [{0}]
                WHEN 4 THEN 1
                WHEN 5 THEN 2
                WHEN 6 THEN 3
                WHEN 7 THEN 3
                WHEN 8 THEN 5
                WHEN 9 THEN 4
                ELSE 0
            END";

        // Reversing this cannot restore what was collapsed — a Pending commission could have been a
        // draft, an approval in flight, or a part-paid one. It maps back to the nearest equivalent so
        // the schema is valid again: Pending becomes Payable, and cancelled stays cancelled.
        private const string CommissionToOld = @"
            CASE [{0}]
                WHEN 1 THEN 6
                WHEN 2 THEN 8
                WHEN 3 THEN 9
                WHEN 4 THEN 10
                ELSE 4
            END";

        private const string RebateToOld = @"
            CASE [{0}]
                WHEN 1 THEN 4
                WHEN 2 THEN 5
                WHEN 3 THEN 7
                WHEN 4 THEN 9
                WHEN 5 THEN 8
                ELSE 2
            END";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_BookingCommissions_BookingId_PartnerId", table: "BookingCommissions");
            migrationBuilder.DropIndex(name: "IX_CustomerRebates_BookingId", table: "CustomerRebates");

            Remap(migrationBuilder, CommissionToNew, RebateToNew);

            migrationBuilder.CreateIndex(
                name: "IX_BookingCommissions_BookingId_PartnerId",
                table: "BookingCommissions",
                columns: ["BookingId", "PartnerId"],
                unique: true,
                filter: "[Status] <> 2 AND [Status] <> 4");
            migrationBuilder.CreateIndex(
                name: "IX_CustomerRebates_BookingId",
                table: "CustomerRebates",
                column: "BookingId",
                unique: true,
                filter: "[Status] <> 3 AND [Status] <> 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_BookingCommissions_BookingId_PartnerId", table: "BookingCommissions");
            migrationBuilder.DropIndex(name: "IX_CustomerRebates_BookingId", table: "CustomerRebates");

            Remap(migrationBuilder, CommissionToOld, RebateToOld);

            migrationBuilder.CreateIndex(
                name: "IX_BookingCommissions_BookingId_PartnerId",
                table: "BookingCommissions",
                columns: ["BookingId", "PartnerId"],
                unique: true,
                filter: "[Status] <> 7 AND [Status] <> 8 AND [Status] <> 10");
            migrationBuilder.CreateIndex(
                name: "IX_CustomerRebates_BookingId",
                table: "CustomerRebates",
                column: "BookingId",
                unique: true,
                filter: "[Status] <> 6 AND [Status] <> 7 AND [Status] <> 8");
        }

        // The audit table stores the same two enums in its before/after columns, so history reads
        // back under the new names too. Nullable there: only status-change rows carry a value.
        private static void Remap(MigrationBuilder migrationBuilder, string commissionCase, string rebateCase)
        {
            migrationBuilder.Sql($"UPDATE [BookingCommissions] SET [Status] = {Col(commissionCase, "Status")};");
            migrationBuilder.Sql($"UPDATE [CustomerRebates] SET [Status] = {Col(rebateCase, "Status")};");
            migrationBuilder.Sql($@"
                UPDATE [FinancialWorkflowAuditEntries]
                SET [PreviousCommissionStatus] = {Col(commissionCase, "PreviousCommissionStatus")}
                WHERE [PreviousCommissionStatus] IS NOT NULL;");
            migrationBuilder.Sql($@"
                UPDATE [FinancialWorkflowAuditEntries]
                SET [NewCommissionStatus] = {Col(commissionCase, "NewCommissionStatus")}
                WHERE [NewCommissionStatus] IS NOT NULL;");
            migrationBuilder.Sql($@"
                UPDATE [FinancialWorkflowAuditEntries]
                SET [PreviousRebateStatus] = {Col(rebateCase, "PreviousRebateStatus")}
                WHERE [PreviousRebateStatus] IS NOT NULL;");
            migrationBuilder.Sql($@"
                UPDATE [FinancialWorkflowAuditEntries]
                SET [NewRebateStatus] = {Col(rebateCase, "NewRebateStatus")}
                WHERE [NewRebateStatus] IS NOT NULL;");
        }

        private static string Col(string template, string column) => string.Format(template, column);
    }
}
