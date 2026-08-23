using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// Drops what the approval ladder needed once nothing reads it any more. This is a separate
    /// migration from <c>20260823020000_SimplifyCommissionRebateStatuses</c>, which had already been
    /// applied when these columns left the model — an applied migration is never re-run, so the
    /// drops had to travel on their own. Until this ran, saving a commission or a rule failed
    /// outright: <c>EarningCondition</c> and <c>RequiresApprovalSnapshot</c> are NOT NULL with no
    /// default, and the model no longer supplied them.
    ///
    /// ApprovedAmount goes with them: with no approver there is nothing to approve a different
    /// amount to, so FinalAmount is the single figure a commission or rebate is worth. That is why
    /// both amount check constraints are rewritten rather than merely dropped.
    /// </summary>
    public partial class DropCommissionApprovalColumns : Migration
    {
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
            // The amount checks name ApprovedAmount, so they have to go before it is dropped.
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }

        private static void AddBack(MigrationBuilder migrationBuilder, string table,
            params (string Name, string Type, bool Nullable, string Default)[] columns)
        {
            foreach (var (name, type, nullable, defaultValue) in columns)
                migrationBuilder.AddColumn<string>(name: name, table: table, type: type, nullable: nullable,
                    defaultValueSql: defaultValue);
        }
    }
}
