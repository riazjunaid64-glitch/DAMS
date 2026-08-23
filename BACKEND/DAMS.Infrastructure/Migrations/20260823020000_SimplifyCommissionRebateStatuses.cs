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

        // Everything the approval ladder needed and nothing else does. ApprovedAmount goes with them:
        // with no approver there is nothing to approve a different amount to, so FinalAmount is the
        // single figure a commission or rebate is worth. Same for the earning conditions, which only
        // ever gated the Earned step, and RequiresApproval, which gated a step that no longer exists.
        private static readonly string[] CommissionColumns =
        [
            "ApprovedAmount", "SubmittedByUserId", "SubmittedByName", "SubmittedAt", "DecisionByUserId",
            "DecisionByName", "DecisionAt", "DecisionReason", "EarnedAt", "PayableAt",
            "EarningCondition", "MinimumCollectionPercent", "EligibilityConditionSnapshot", "RequiresApprovalSnapshot"
        ];

        private static readonly string[] RebateColumns =
        [
            "ApprovedAmount", "SubmittedByUserId", "SubmittedByName", "SubmittedAt",
            "DecisionByUserId", "DecisionByName", "DecisionAt", "DecisionReason"
        ];

        private static readonly string[] RuleColumns =
        [
            "EligibilityCondition", "EarningCondition", "MinimumCollectionPercent", "RequiresApproval"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_BookingCommissions_BookingId_PartnerId", table: "BookingCommissions");
            migrationBuilder.DropIndex(name: "IX_CustomerRebates_BookingId", table: "CustomerRebates");

            Remap(migrationBuilder, CommissionToNew, RebateToNew);

            // The amount checks name ApprovedAmount, so they have to be rewritten before it is dropped.
            migrationBuilder.DropCheckConstraint(name: "CK_BookingCommissions_Amounts", table: "BookingCommissions");
            migrationBuilder.DropCheckConstraint(name: "CK_CustomerRebates_Amounts", table: "CustomerRebates");
            foreach (var column in CommissionColumns)
                migrationBuilder.DropColumn(name: column, table: "BookingCommissions");
            foreach (var column in RebateColumns)
                migrationBuilder.DropColumn(name: column, table: "CustomerRebates");
            foreach (var column in RuleColumns)
                migrationBuilder.DropColumn(name: column, table: "CommissionRules");
            migrationBuilder.AddCheckConstraint(name: "CK_BookingCommissions_Amounts", table: "BookingCommissions",
                sql: "[BasisAmount] > 0 AND [CalculatedAmount] >= 0 AND [FinalAmount] >= 0 AND [AllocationPercentSnapshot] > 0 AND [AllocationPercentSnapshot] <= 100");
            migrationBuilder.AddCheckConstraint(name: "CK_CustomerRebates_Amounts", table: "CustomerRebates",
                sql: "[BasisAmount] > 0 AND [CalculatedAmount] >= 0 AND [FinalAmount] >= 0");

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

            // Restored empty: what these columns held is gone, and there is nothing to reconstruct
            // them from. Only the shape comes back, so the older code can bind against it again.
            migrationBuilder.DropCheckConstraint(name: "CK_BookingCommissions_Amounts", table: "BookingCommissions");
            migrationBuilder.DropCheckConstraint(name: "CK_CustomerRebates_Amounts", table: "CustomerRebates");
            AddBack(migrationBuilder, "BookingCommissions",
                ("ApprovedAmount", "decimal(18,2)", true, null), ("SubmittedByUserId", "int", true, null),
                ("SubmittedByName", "nvarchar(200)", true, null), ("SubmittedAt", "datetime2", true, null),
                ("DecisionByUserId", "int", true, null), ("DecisionByName", "nvarchar(200)", true, null),
                ("DecisionAt", "datetime2", true, null), ("DecisionReason", "nvarchar(2000)", true, null),
                ("EarnedAt", "datetime2", true, null), ("PayableAt", "datetime2", true, null),
                ("EarningCondition", "int", false, "0"), ("MinimumCollectionPercent", "decimal(5,2)", true, null),
                ("EligibilityConditionSnapshot", "nvarchar(1000)", true, null),
                ("RequiresApprovalSnapshot", "bit", false, "1"));
            AddBack(migrationBuilder, "CustomerRebates",
                ("ApprovedAmount", "decimal(18,2)", true, null), ("SubmittedByUserId", "int", true, null),
                ("SubmittedByName", "nvarchar(200)", true, null), ("SubmittedAt", "datetime2", true, null),
                ("DecisionByUserId", "int", true, null), ("DecisionByName", "nvarchar(200)", true, null),
                ("DecisionAt", "datetime2", true, null), ("DecisionReason", "nvarchar(2000)", true, null));
            AddBack(migrationBuilder, "CommissionRules",
                ("EligibilityCondition", "nvarchar(1000)", true, null), ("EarningCondition", "int", false, "0"),
                ("MinimumCollectionPercent", "decimal(5,2)", true, null), ("RequiresApproval", "bit", false, "1"));
            migrationBuilder.AddCheckConstraint(name: "CK_BookingCommissions_Amounts", table: "BookingCommissions",
                sql: "[BasisAmount] > 0 AND [CalculatedAmount] >= 0 AND [FinalAmount] >= 0 AND ([ApprovedAmount] IS NULL OR [ApprovedAmount] >= 0) AND [AllocationPercentSnapshot] > 0 AND [AllocationPercentSnapshot] <= 100");
            migrationBuilder.AddCheckConstraint(name: "CK_CustomerRebates_Amounts", table: "CustomerRebates",
                sql: "[BasisAmount] > 0 AND [CalculatedAmount] >= 0 AND [FinalAmount] >= 0 AND ([ApprovedAmount] IS NULL OR [ApprovedAmount] >= 0)");

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

        private static void AddBack(MigrationBuilder migrationBuilder, string table,
            params (string Name, string Type, bool Nullable, string Default)[] columns)
        {
            foreach (var (name, type, nullable, defaultValue) in columns)
                migrationBuilder.AddColumn<string>(name: name, table: table, type: type, nullable: nullable,
                    defaultValueSql: defaultValue);
        }
    }
}
