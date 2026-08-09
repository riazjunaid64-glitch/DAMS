using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CommissionRuleRevisionsAndAuditImmutability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CommissionRuleRevisionId",
                table: "FinancialWorkflowAuditEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuleRevisionId",
                table: "BookingCommissions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommissionRuleRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleId = table.Column<int>(type: "int", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreviousSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SnapshotHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ChangeReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true),
                    ChangedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionRuleRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissionRuleRevisions_CommissionRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "CommissionRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Seed an immutable baseline for every pre-existing rule. Existing commissions retain a
            // null revision reference because their historical rule state cannot be reconstructed
            // honestly; commissions calculated after this deployment always reference a revision.
            migrationBuilder.Sql(
                """
                INSERT INTO [CommissionRuleRevisions]
                    ([RuleId], [RevisionNumber], [SnapshotJson], [PreviousSnapshotJson], [SnapshotHash],
                     [ChangeReason], [ChangedByUserId], [ChangedByName], [ChangedAt])
                SELECT r.[Id], 1, snapshot.[SnapshotJson], NULL,
                       CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max), snapshot.[SnapshotJson])), 2),
                       N'Baseline revision created during production migration.', NULL, N'System migration', SYSUTCDATETIME()
                FROM [CommissionRules] r
                CROSS APPLY
                (
                    SELECT
                        CASE WHEN r.[BookingId] IS NULL THEN NULL ELSE CONVERT(varchar(20), r.[BookingId]) END AS [BookingId],
                        CASE r.[BookingSource] WHEN 0 THEN N'Website' WHEN 1 THEN N'WalkIn' WHEN 2 THEN N'Phone'
                            WHEN 3 THEN N'Referral' WHEN 4 THEN N'Other' END AS [BookingSource],
                        CASE r.[CalculationBasis] WHEN 0 THEN N'AgreedSalePrice' WHEN 1 THEN N'NetSalePriceAfterDiscount'
                            WHEN 2 THEN N'BookingAmountReceived' WHEN 3 THEN N'AmountActuallyCollected'
                            WHEN 4 THEN N'ManuallyApprovedAmount' END AS [CalculationBasis],
                        CASE r.[CalculationType] WHEN 0 THEN N'Percentage' WHEN 1 THEN N'FixedAmount' END AS [CalculationType],
                        r.[Description] AS [Description],
                        CASE r.[EarningCondition] WHEN 0 THEN N'ManualMilestone' WHEN 1 THEN N'BookingAmountFullyReceived'
                            WHEN 2 THEN N'MinimumCollectionPercentage' WHEN 3 THEN N'FirstInstallmentReceived'
                            WHEN 4 THEN N'SaleCompleted' END AS [EarningCondition],
                        CONVERT(char(10), r.[EffectiveFrom], 23) AS [EffectiveFrom],
                        CASE WHEN r.[EffectiveTo] IS NULL THEN NULL ELSE CONVERT(char(10), r.[EffectiveTo], 23) END AS [EffectiveTo],
                        r.[EligibilityCondition] AS [EligibilityCondition],
                        CASE WHEN r.[FixedAmount] IS NULL THEN NULL ELSE CONVERT(varchar(50), r.[FixedAmount]) END AS [FixedAmount],
                        CASE WHEN r.[IsActive] = 1 THEN N'True' ELSE N'False' END AS [IsActive],
                        CASE WHEN r.[MaximumCommission] IS NULL THEN NULL ELSE CONVERT(varchar(50), r.[MaximumCommission]) END AS [MaximumCommission],
                        CASE WHEN r.[MinimumCollectionPercent] IS NULL THEN NULL ELSE CONVERT(varchar(50), r.[MinimumCollectionPercent]) END AS [MinimumCollectionPercent],
                        CASE WHEN r.[MinimumCommission] IS NULL THEN NULL ELSE CONVERT(varchar(50), r.[MinimumCommission]) END AS [MinimumCommission],
                        r.[Name] AS [Name], r.[Notes] AS [Notes],
                        CASE WHEN r.[PartnerId] IS NULL THEN NULL ELSE CONVERT(varchar(20), r.[PartnerId]) END AS [PartnerId],
                        r.[PartnerType] AS [PartnerType],
                        CASE WHEN r.[PercentageRate] IS NULL THEN NULL ELSE CONVERT(varchar(50), r.[PercentageRate]) END AS [PercentageRate],
                        CONVERT(varchar(20), r.[Priority]) AS [Priority],
                        CASE WHEN r.[ProjectId] IS NULL THEN NULL ELSE CONVERT(varchar(20), r.[ProjectId]) END AS [ProjectId],
                        CASE WHEN r.[RequiresApproval] = 1 THEN N'True' ELSE N'False' END AS [RequiresApproval],
                        r.[UnitCategory] AS [UnitCategory]
                    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
                ) snapshot([SnapshotJson]);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialWorkflowAuditEntries_CommissionRuleRevisionId_OccurredAt",
                table: "FinancialWorkflowAuditEntries",
                columns: new[] { "CommissionRuleRevisionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingCommissions_RuleRevisionId",
                table: "BookingCommissions",
                column: "RuleRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRuleRevisions_RuleId_RevisionNumber",
                table: "CommissionRuleRevisions",
                columns: new[] { "RuleId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRuleRevisions_SnapshotHash",
                table: "CommissionRuleRevisions",
                column: "SnapshotHash");

            migrationBuilder.AddForeignKey(
                name: "FK_BookingCommissions_CommissionRuleRevisions_RuleRevisionId",
                table: "BookingCommissions",
                column: "RuleRevisionId",
                principalTable: "CommissionRuleRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialWorkflowAuditEntries_CommissionRuleRevisions_CommissionRuleRevisionId",
                table: "FinancialWorkflowAuditEntries",
                column: "CommissionRuleRevisionId",
                principalTable: "CommissionRuleRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingCommissions_CommissionRuleRevisions_RuleRevisionId",
                table: "BookingCommissions");

            migrationBuilder.DropForeignKey(
                name: "FK_FinancialWorkflowAuditEntries_CommissionRuleRevisions_CommissionRuleRevisionId",
                table: "FinancialWorkflowAuditEntries");

            migrationBuilder.DropTable(
                name: "CommissionRuleRevisions");

            migrationBuilder.DropIndex(
                name: "IX_FinancialWorkflowAuditEntries_CommissionRuleRevisionId_OccurredAt",
                table: "FinancialWorkflowAuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_BookingCommissions_RuleRevisionId",
                table: "BookingCommissions");

            migrationBuilder.DropColumn(
                name: "CommissionRuleRevisionId",
                table: "FinancialWorkflowAuditEntries");

            migrationBuilder.DropColumn(
                name: "RuleRevisionId",
                table: "BookingCommissions");
        }
    }
}
