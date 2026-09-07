using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// Recognise a partner commission when it is AGREED, not when it is paid.
    /// <para>
    /// Until now the formal statements derived the commission expense from payouts less reversals.
    /// That put an agreed-in-June, paid-in-September commission entirely in September, and — worse —
    /// reported a commission already agreed but not yet paid as neither a cost nor a liability: the
    /// P&amp;L was silent about the charge and the Balance Sheet was silent about the payable.
    /// </para>
    /// <para>
    /// <c>CommissionAccruals</c> is that missing half — a dated, signed, append-only ledger of what
    /// the company owes partners. The accrual raises Commission Expense and Commission Payable; the
    /// payout now settles the payable and has no P&amp;L effect at all. Both entries are double-sided,
    /// so the Balance Sheet balances exactly as before.
    /// </para>
    /// <para>
    /// The backfill reconstructs the payable for commissions that already exist. It is safe to date
    /// those at the commission's own creation day EXCEPT where that falls before a committed opening
    /// balance, which those rows are clamped to: the Commission Payable account is created by this
    /// migration, so its opening balance is zero by construction and the whole payable has to be
    /// built from accruals — but a row dated inside a closed period would be counted in the payable
    /// while the P&amp;L window excluded it, and the sheet would go out by exactly that amount.
    /// </para>
    /// <para>
    /// Before any of that: <see cref="RestoreLostApprovalOverrides"/> fixes the pre-simplification
    /// records whose <c>FinalAmount</c> is not what they are really worth, because an earlier
    /// migration dropped the approval override that had settled them at a different figure. It
    /// reads what actually settled (net of reversals) for a closed record and the audited approval
    /// decision for one still open. The backfill below reads <c>FinalAmount</c> as gospel, so this
    /// runs first. WHEN each movement happened is a separate question, and the backfill answers it
    /// from the dated log rather than from anything decided here.
    /// </para>
    /// </summary>
    public partial class AddCommissionAccruals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The account statements below adopt an existing account by name or create one. An
            // account holding that name with the wrong type, or already claimed by another system
            // role, defeats both halves at once and would leave the migration "successful" with
            // the payable having nowhere to sit — a Balance Sheet that silently stops balancing.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [FinanceAccounts]
                           WHERE [Name] = N'Commission Payable'
                             AND ([Type] <> 5 OR [SystemRole] NOT IN (0, 5)))
                    THROW 51010, N'An account named "Commission Payable" already exists but is not an unclaimed Liability account, so it cannot take the Commission Payable role. Retype it to Liability and clear its system role, or rename it, then re-run.', 1;
                """);

            // An account this migration is about to adopt must be EMPTY. From here the payable is
            // derived per commission — accrual raises it, payout clears it — so an aggregate opening
            // figure entered by the accountant for the same unpaid commissions would be counted a
            // second time by the backfill below, and nothing would ever clear it: pay the commission
            // in full and the payout settles only its own accrual, leaving the opening amount owed
            // for ever on a balance sheet that still balances. Refusing is the only safe answer;
            // silently zeroing the accountant's figure is not this migration's decision to make.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [FinanceAccounts]
                           WHERE ([SystemRole] = 5 OR [Name] = N'Commission Payable')
                             AND [OpeningBalance] <> 0)
                    THROW 51012, N'The Commission Payable account already carries an opening balance. From this upgrade the balance is derived from the commissions themselves, so that figure would be counted twice and could never be cleared by paying the commissions it represents. Clear it through the opening-balance correction process — entering the commissions it stands for as commission records — and re-run.', 1;
                IF EXISTS (
                    SELECT 1 FROM [OpeningBalanceEntries] e
                    JOIN [FinanceAccounts] a ON a.[Id] = e.[FinanceAccountId]
                    WHERE ([a].[SystemRole] = 5 OR [a].[Name] = N'Commission Payable')
                      AND (e.[DebitAmount] <> 0 OR e.[CreditAmount] <> 0))
                    THROW 51012, N'A committed or draft opening-balance set holds an amount against Commission Payable. From this upgrade the balance is derived from the commissions themselves, so that figure would be counted twice and could never be cleared. Correct the opening-balance set — entering the commissions it stands for as commission records — and re-run.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] >= 0 AND [SystemRole] <= 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_CommissionPayableRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] <> 5 OR [Type] = 5");

            migrationBuilder.CreateTable(
                name: "CommissionAccruals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CommissionId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AccruedOn = table.Column<DateTime>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    RecordedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionAccruals", x => x.Id);
                    // Signed on purpose: a release and a downward correction are negative. Only a
                    // row that moves nothing is refused.
                    table.CheckConstraint("CK_CommissionAccruals_NonZero", "[Amount] <> 0");
                    table.ForeignKey(
                        name: "FK_CommissionAccruals_BookingCommissions_CommissionId",
                        column: x => x.CommissionId,
                        principalTable: "BookingCommissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionAccruals_CommissionId_AccruedOn",
                table: "CommissionAccruals",
                columns: new[] { "CommissionId", "AccruedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionAccruals_AccruedOn",
                table: "CommissionAccruals",
                column: "AccruedOn");

            // ── The payable's account ────────────────────────────────────────────────────────
            // Adopt an account of that name if someone already made one, create it otherwise. No
            // ledger code: the numeric codes in this chart are the client's real ERP codes and this
            // account has no counterpart there; inventing one would put a code DAMS made up into
            // the Trial Balance as though the accountant had issued it.
            // Skipped on an empty chart — a brand-new database gets its accounts from chart setup,
            // which assigns the role itself and would otherwise collide on the name.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [SystemRole] = 5)
                    UPDATE TOP (1) [FinanceAccounts]
                    SET [SystemRole] = 5, [UpdatedAt] = SYSUTCDATETIME()
                    WHERE [SystemRole] = 0 AND [Type] = 5 AND [Name] = N'Commission Payable';
                """);
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [SystemRole] = 5)
                   AND NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [Name] = N'Commission Payable')
                   AND EXISTS (SELECT 1 FROM [FinanceAccounts])
                    INSERT INTO [FinanceAccounts]
                        ([Name], [Type], [AccountHolderName], [OpeningBalance], [LedgerCode], [DisplayOrder],
                         [SystemRole], [BankOrWalletName], [Description], [IsActive], [CreatedAt], [UpdatedAt])
                    SELECT N'Commission Payable', 5,
                        COALESCE(
                            (SELECT TOP (1) [AccountHolderName] FROM [FinanceAccounts] WHERE [SystemRole] = 1),
                            (SELECT TOP (1) [AccountHolderName] FROM [FinanceAccounts] ORDER BY [DisplayOrder], [Id])),
                        0, NULL, 517, 5, NULL, NULL, 1, SYSUTCDATETIME(), SYSUTCDATETIME();
                """);

            // Must run BEFORE the backfill: the backfill trusts FinalAmount as the true total, and
            // for a narrow set of pre-simplification records that column is wrong.
            migrationBuilder.Sql(RestoreLostApprovalOverrides);

            // The helper exists only for the reconstruction below and is dropped straight after, so
            // no application code can come to depend on a function this migration owns.
            migrationBuilder.Sql(DateHelper);
            migrationBuilder.Sql(Backfill);
            migrationBuilder.Sql("DROP FUNCTION dbo.CommissionAccrualBackfillDate;");

            // Postcondition: if a chart exists, the role must be held. Anything else means the
            // payable this migration exists to create has no account to sit in.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [FinanceAccounts])
                   AND NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [SystemRole] = 5)
                    THROW 51011, N'Migration finished without establishing the Commission Payable system account. Commissions owed to partners would have no account to sit in and the balance sheet would not balance. Inspect [FinanceAccounts] for name or type collisions and re-run.', 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Release the role before the constraint that permits it is dropped, or the narrowed
            // CK_FinanceAccounts_SystemRole cannot be re-added. The account itself is left in place:
            // deleting a chart account is never something a schema rollback should decide.
            migrationBuilder.Sql(
                "UPDATE [FinanceAccounts] SET [SystemRole] = 0, [UpdatedAt] = SYSUTCDATETIME() WHERE [SystemRole] = 5;");

            migrationBuilder.DropTable(name: "CommissionAccruals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_CommissionPayableRole",
                table: "FinanceAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] >= 0 AND [SystemRole] <= 4");
        }

        /// <summary>
        /// Restores the amount a pre-simplification approval override actually settled a commission
        /// or rebate at, where that differs from <c>FinalAmount</c>.
        /// <para>
        /// <c>20260823030000_DropCommissionApprovalColumns</c> dropped <c>ApprovedAmount</c> without
        /// folding its value into <c>FinalAmount</c> first. Before that, a payout's target was
        /// <c>ApprovedAmount ?? FinalAmount</c> — an approver could settle a commission at a figure
        /// other than the one it was calculated at — so a record an approver adjusted has carried
        /// the WRONG figure in <c>FinalAmount</c> ever since, invisibly, because nothing cross-checked
        /// it against what was actually paid. <see cref="Backfill"/> trusts <c>FinalAmount</c> as the
        /// true total, so this has to run first or it reconstructs the payable from the same wrong
        /// number: a fully paid commission would accrue a permanent non-zero balance — remaining if
        /// the override was lower than the calculated figure, negative if it was higher.
        /// </para>
        /// <para>Two independent pieces of evidence are read, and which one governs depends on the
        /// state of the record:</para>
        /// <list type="number">
        /// <item><b>What actually settled</b>, for a record in a terminal state. A commission only
        /// reaches Paid, and a rebate only reaches Applied or Paid, when its payouts or disbursements
        /// covered its outstanding target exactly, and neither status ever accepts another movement
        /// afterwards — so the money that moved IS the target. It is counted NET OF REVERSALS, the
        /// same figure the application's own <c>NetPaid</c>/<c>NetDisbursed</c> report: a commission
        /// paid 100,000, reversed 20,000 and re-paid 20,000 has settled 100,000, and summing the
        /// payout rows alone would restore it as a 120,000 obligation that can never be cleared.</item>
        /// <item><b>What the last approval decided</b>, for a record still open. A part payment says
        /// nothing about the target, and this is where the earlier version of this correction gave
        /// up — but the decision itself was audited: the approval wrote
        /// <c>NewAmount = ApprovedAmount ?? FinalAmount</c> against <c>PreviousAmount</c>, so the
        /// override that was dropped from the column survives in the log with the date it was made.
        /// An approved-at-80,000 commission with 30,000 paid is left owing 50,000, not 70,000.</item>
        /// </list>
        /// <para>
        /// An approval is ignored where a later EDIT recalculated the amount, because that path set
        /// <c>ApprovedAmount = null</c> as it went: <c>FinalAmount</c> is then already correct. The
        /// reconstructed target is also never allowed BELOW what has already gone out — a record
        /// whose payouts exceed its own approval cannot be explained from here, and inventing a
        /// negative payable for it would be worse than leaving it alone.
        /// </para>
        /// <para>
        /// Applied as a signed adjustment, not a silent overwrite of <c>FinalAmount</c>, so the
        /// correction is self-documenting. Idempotent on its own terms: once corrected, the target
        /// matches and the <c>WHERE</c> clause no longer selects the row.
        /// </para>
        /// <para>
        /// This decides only what the record is WORTH. When the movement happened is not its
        /// business: <see cref="Backfill"/> reads that from the dated log itself, so the two cannot
        /// drift apart and an approval this correction ignores is still reported in its own period.
        /// </para>
        /// </summary>
        private const string RestoreLostApprovalOverrides = """
            UPDATE c
            SET c.[AdjustmentAmount] = c.[AdjustmentAmount] + (t.[Target] - c.[FinalAmount]),
                c.[FinalAmount] = t.[Target],
                c.[AdjustmentReason] = LEFT(
                    (CASE WHEN c.[AdjustmentReason] IS NULL THEN N'' ELSE c.[AdjustmentReason] + N' ' END)
                    + N'Data correction (commission accrual migration, 2026-09-06): restored the '
                    + CASE WHEN t.[Source] = 'settled' THEN N'amount actually paid, net of reversals'
                           ELSE N'amount the last approval settled this at' END
                    + N', lost when the pre-approval-ladder ApprovedAmount column was dropped without migrating its value.',
                    2000),
                c.[UpdatedAt] = SYSUTCDATETIME()
            FROM [BookingCommissions] c
            CROSS APPLY (
                -- Two independent aggregates, never a join: a payout carrying two partial reversals
                -- would be duplicated by the fan-out and its own amount counted twice.
                SELECT [HasPayouts] = CASE WHEN EXISTS (
                           SELECT 1 FROM [CommissionPayouts] p WHERE p.[CommissionId] = c.[Id])
                           THEN 1 ELSE 0 END,
                       [Net] = ISNULL((SELECT SUM(p.[Amount]) FROM [CommissionPayouts] p
                                       WHERE p.[CommissionId] = c.[Id]), 0)
                             - ISNULL((SELECT SUM(rv.[Amount]) FROM [CommissionPayoutReversals] rv
                                       JOIN [CommissionPayouts] p2 ON p2.[Id] = rv.[PayoutId]
                                       WHERE p2.[CommissionId] = c.[Id]), 0)
            ) settled
            OUTER APPLY (
                SELECT TOP (1) a.[NewAmount], a.[PreviousAmount], a.[OccurredAt],
                       a.[PerformedByUserId], a.[PerformedByName]
                FROM [FinancialWorkflowAuditEntries] a
                WHERE a.[CommissionId] = c.[Id] AND a.[Action] = 12 -- CommissionApproved
                  AND a.[NewAmount] IS NOT NULL AND a.[PreviousAmount] IS NOT NULL
                  AND a.[NewAmount] <> a.[PreviousAmount]
                  -- An edit after the decision recalculated the amount and cleared the override with
                  -- it, so FinalAmount already says what the record is worth.
                  AND NOT EXISTS (
                      SELECT 1 FROM [FinancialWorkflowAuditEntries] e
                      WHERE e.[CommissionId] = c.[Id] AND e.[Action] = 10 -- CommissionAdjusted
                        AND e.[NewCommissionStatus] IS NOT NULL -- only the edit path passes a status pair
                        AND (e.[OccurredAt] > a.[OccurredAt]
                             OR (e.[OccurredAt] = a.[OccurredAt] AND e.[Id] > a.[Id])))
                ORDER BY a.[OccurredAt] DESC, a.[Id] DESC
            ) approval
            CROSS APPLY (
                SELECT [Target] = CASE
                    WHEN c.[Status] = 1 AND settled.[HasPayouts] = 1 AND settled.[Net] > 0
                        THEN settled.[Net] -- Paid: the one commission status a payout can never follow.
                    WHEN approval.[NewAmount] > 0 AND approval.[NewAmount] >= settled.[Net]
                        THEN approval.[NewAmount]
                    ELSE NULL
                END,
                -- Which evidence the target came from, so the correction says the true thing even
                -- for a record whose status and payouts disagree.
                [Source] = CASE
                    WHEN c.[Status] = 1 AND settled.[HasPayouts] = 1 AND settled.[Net] > 0
                        THEN 'settled' ELSE 'approval'
                END
            ) t
            WHERE t.[Target] IS NOT NULL AND t.[Target] <> c.[FinalAmount];

            -- Rebates take the same two-source correction.
            UPDATE r
            SET r.[AdjustmentAmount] = r.[AdjustmentAmount] + (t.[Target] - r.[FinalAmount]),
                r.[FinalAmount] = t.[Target],
                r.[AdjustmentReason] = LEFT(
                    (CASE WHEN r.[AdjustmentReason] IS NULL THEN N'' ELSE r.[AdjustmentReason] + N' ' END)
                    + N'Data correction (commission accrual migration, 2026-09-06): restored the '
                    + CASE WHEN t.[Source] = 'settled' THEN N'amount actually disbursed, net of reversals'
                           ELSE N'amount the last approval settled this at' END
                    + N', lost when the pre-approval-ladder ApprovedAmount column was dropped without migrating its value.',
                    2000),
                r.[UpdatedAt] = SYSUTCDATETIME()
            FROM [CustomerRebates] r
            CROSS APPLY (
                SELECT [HasDisbursements] = CASE WHEN EXISTS (
                           SELECT 1 FROM [RebateDisbursements] d WHERE d.[RebateId] = r.[Id])
                           THEN 1 ELSE 0 END,
                       [Net] = ISNULL((SELECT SUM(d.[Amount]) FROM [RebateDisbursements] d
                                       WHERE d.[RebateId] = r.[Id]), 0)
                             - ISNULL((SELECT SUM(rv.[Amount]) FROM [RebateDisbursementReversals] rv
                                       JOIN [RebateDisbursements] d2 ON d2.[Id] = rv.[DisbursementId]
                                       WHERE d2.[RebateId] = r.[Id]), 0)
            ) settled
            OUTER APPLY (
                SELECT TOP (1) a.[NewAmount], a.[PreviousAmount]
                FROM [FinancialWorkflowAuditEntries] a
                WHERE a.[RebateId] = r.[Id] AND a.[Action] = 24 -- RebateApproved
                  AND a.[NewAmount] IS NOT NULL AND a.[PreviousAmount] IS NOT NULL
                  AND a.[NewAmount] <> a.[PreviousAmount]
                  AND NOT EXISTS (
                      SELECT 1 FROM [FinancialWorkflowAuditEntries] e
                      WHERE e.[RebateId] = r.[Id] AND e.[Action] = 35 -- RebateAdjusted
                        AND e.[NewRebateStatus] IS NOT NULL
                        AND (e.[OccurredAt] > a.[OccurredAt]
                             OR (e.[OccurredAt] = a.[OccurredAt] AND e.[Id] > a.[Id])))
                ORDER BY a.[OccurredAt] DESC, a.[Id] DESC
            ) approval
            CROSS APPLY (
                SELECT [Target] = CASE
                    WHEN r.[Status] IN (1, 2) AND settled.[HasDisbursements] = 1 AND settled.[Net] > 0
                        THEN settled.[Net] -- Applied, Paid: a rebate's two fully-disbursed states.
                    WHEN approval.[NewAmount] > 0 AND approval.[NewAmount] >= settled.[Net]
                        THEN approval.[NewAmount]
                    ELSE NULL
                END,
                [Source] = CASE
                    WHEN r.[Status] IN (1, 2) AND settled.[HasDisbursements] = 1 AND settled.[Net] > 0
                        THEN 'settled' ELSE 'approval'
                END
            ) t
            WHERE t.[Target] IS NOT NULL AND t.[Target] <> r.[FinalAmount];
            """;

        /// <summary>
        /// Reconstructs each commission's obligation history from the dated audit log, so the periods
        /// that have already been reported keep reporting what happened in them.
        /// <para>
        /// Stamping today's <c>FinalAmount</c> at the creation date would have been wrong in exactly
        /// the two ways that matter: a commission agreed at 100,000 in August and raised to 130,000
        /// in September would show 130,000 of August expense and no September movement, and one
        /// agreed in August and cancelled in September would show no August expense at all. Current
        /// balances would still reconcile while both periods were wrong.
        /// </para>
        /// <para>
        /// The history is ONE ordered walk of the log, not a set of per-event-type rules. Creation,
        /// an approval and an edit all record what the commission was worth immediately after them
        /// — creation and edit write the new <c>FinalAmount</c>, an approval wrote
        /// <c>ApprovedAmount ?? FinalAmount</c>, which is what a payout was then measured against —
        /// so differencing consecutive events gives each movement, and the first is the recognition.
        /// </para>
        /// <para>
        /// Reading an edit's own <c>PreviousAmount</c> instead is what made an approval vanish: the
        /// edit had cleared <c>ApprovedAmount</c>, so that figure is the PRE-override one. A July
        /// approval followed by an August edit left July reporting the uncut amount and August
        /// moving by the wrong step, with today's total still reconciling exactly.
        /// </para>
        /// <para>Then two sources that are not movements in the log:</para>
        /// <list type="bullet">
        /// <item><b>No audit at all</b> — today's <c>FinalAmount</c>, recognised on the creation day.
        /// It already contains every movement the record made, so none are replayed on top.</item>
        /// <item><b>Release</b> — the whole accrued balance, reversed on the first date the record
        /// moved to Cancelled / ReversalRequired / Reversed.</item>
        /// </list>
        /// <para>
        /// A ReversalRequired commission with money already paid therefore lands as a NEGATIVE
        /// payable, which is what it is: the sale was void, the commission is not a cost, and what
        /// was paid is recoverable from the partner. The workspace reports it as "Recovery due".
        /// </para>
        /// <para>
        /// Every date is a Pakistan business date and is clamped up to a committed opening-balance
        /// date: everything before that is already inside the accountant's figures, and the Balance
        /// Sheet's retained-profit window starts there while the payable is all-time, so a
        /// pre-baseline row would put the sheet out by its own amount.
        /// </para>
        /// <para>
        /// Where the log cannot explain the record — a commission with no <c>CommissionCreated</c>
        /// row, or one whose audited movements do not add up to its current <c>FinalAmount</c> — the
        /// remainder is written as a single dated cutover correction rather than silently presented
        /// as history. Today's payable is therefore exact whatever the log contains.
        /// </para>
        /// <para>The whole backfill is skipped if the table already holds anything, which is what
        /// makes it re-runnable.</para>
        /// </summary>
        private const string Backfill = """
            IF NOT EXISTS (SELECT 1 FROM [CommissionAccruals])
            BEGIN
                DECLARE @Baseline date = (
                    SELECT MAX([AsAtDate]) FROM [OpeningBalanceSets] WHERE [CommittedAt] IS NOT NULL);
                DECLARE @Today date = CAST(DATEADD(hour, 5, SYSUTCDATETIME()) AS date);
                DECLARE @Note nvarchar(200) =
                    N'Reconstructed when commission accrual accounting was introduced.';

                -- 1. The obligation's own history, walked in date order.
                --
                -- Every amount-bearing event records what the commission was worth immediately
                -- AFTER it: creation and an edit both write the new FinalAmount, and an approval
                -- wrote `ApprovedAmount ?? FinalAmount` — the figure a payout was measured against
                -- from that day. So each event's movement is simply the step from the one before
                -- it, and no event has to know what kind its neighbours were.
                --
                -- Reading an edit's own PreviousAmount instead is what silently lost an approval:
                -- the edit cleared ApprovedAmount as it went, so its PreviousAmount is the
                -- PRE-override figure. An approval that cut a commission in July, followed by an
                -- August edit, left July still reporting the uncut amount and August moving by the
                -- wrong step — while today's total reconciled exactly, which is what kept it
                -- hidden. Ordering the events and differencing them cannot express that mistake.
                WITH [Events] AS (
                    SELECT a.[CommissionId], a.[NewAmount], a.[OccurredAt], a.[Id],
                           a.[PerformedByUserId], a.[PerformedByName]
                    FROM [FinancialWorkflowAuditEntries] a
                    WHERE a.[CommissionId] IS NOT NULL AND a.[NewAmount] IS NOT NULL
                      AND (a.[Action] IN (8, 12) -- CommissionCreated, CommissionApproved
                           -- An EDIT, not the adjustment recorded as part of the original entry:
                           -- only the edit path passes a status pair.
                           OR (a.[Action] = 10 AND a.[NewCommissionStatus] IS NOT NULL))
                      AND EXISTS (SELECT 1 FROM [BookingCommissions] c WHERE c.[Id] = a.[CommissionId])
                      -- Only where the log holds the record's opening amount. Without it the
                      -- fallback below owns the whole figure, and replaying movements on top of a
                      -- FinalAmount that already contains them would double-count every one.
                      AND EXISTS (
                          SELECT 1 FROM [FinancialWorkflowAuditEntries] o
                          WHERE o.[CommissionId] = a.[CommissionId] AND o.[Action] = 8
                            AND o.[NewAmount] IS NOT NULL)
                ), [Movements] AS (
                    SELECT e.*,
                           [Previous] = LAG(e.[NewAmount]) OVER (
                               PARTITION BY e.[CommissionId] ORDER BY e.[OccurredAt], e.[Id])
                    FROM [Events] e
                )
                INSERT INTO [CommissionAccruals]
                    ([CommissionId], [Amount], [AccruedOn], [Kind], [Reason],
                     [RecordedByUserId], [RecordedByName], [RecordedAt])
                SELECT m.[CommissionId], m.[NewAmount] - ISNULL(m.[Previous], 0),
                       dbo.CommissionAccrualBackfillDate(m.[OccurredAt], @Baseline),
                       -- The first event raises the obligation; every later one corrects it.
                       CASE WHEN m.[Previous] IS NULL THEN 0 ELSE 1 END,
                       @Note, m.[PerformedByUserId], m.[PerformedByName], m.[OccurredAt]
                FROM [Movements] m
                WHERE m.[NewAmount] - ISNULL(m.[Previous], 0) <> 0;

                -- 2. Records the log cannot open: today's amount, on the day they were created.
                -- FinalAmount already contains every movement they ever made, so no history is
                -- replayed for them — the whole figure is recognised at once.
                INSERT INTO [CommissionAccruals]
                    ([CommissionId], [Amount], [AccruedOn], [Kind], [Reason],
                     [RecordedByUserId], [RecordedByName], [RecordedAt])
                SELECT c.[Id], c.[FinalAmount],
                       dbo.CommissionAccrualBackfillDate(c.[CreatedAt], @Baseline),
                       0, @Note, c.[CreatedByUserId], c.[CreatedByName], c.[CreatedAt]
                FROM [BookingCommissions] c
                WHERE c.[FinalAmount] <> 0
                  AND NOT EXISTS (
                      SELECT 1 FROM [FinancialWorkflowAuditEntries] o
                      WHERE o.[CommissionId] = c.[Id] AND o.[Action] = 8
                        AND o.[NewAmount] IS NOT NULL);

                -- 3. Cutover correction, only where the log does not add up to the record. Dated
                -- today rather than dressed up as history.
                INSERT INTO [CommissionAccruals]
                    ([CommissionId], [Amount], [AccruedOn], [Kind], [Reason],
                     [RecordedByUserId], [RecordedByName], [RecordedAt])
                SELECT c.[Id], c.[FinalAmount] - ISNULL(m.[Accrued], 0), @Today, 1,
                       N'Cutover correction: the audit history did not account for this commission in full.',
                       c.[CreatedByUserId], c.[CreatedByName], SYSUTCDATETIME()
                FROM [BookingCommissions] c
                OUTER APPLY (
                    SELECT [Accrued] = SUM(x.[Amount]) FROM [CommissionAccruals] x
                    WHERE x.[CommissionId] = c.[Id]
                ) m
                WHERE c.[FinalAmount] - ISNULL(m.[Accrued], 0) <> 0;

                -- 4. Release: a closed record owes nothing from the day it closed. Never dated before
                -- the movements it reverses, so no intermediate period can show a negative payable.
                INSERT INTO [CommissionAccruals]
                    ([CommissionId], [Amount], [AccruedOn], [Kind], [Reason],
                     [RecordedByUserId], [RecordedByName], [RecordedAt])
                SELECT c.[Id], -m.[Accrued],
                       -- A record with no closure audit falls back to today; either way the release
                       -- is never dated before the movements it reverses, so no intermediate period
                       -- can show a payable that was released before it was raised.
                       CASE WHEN ISNULL(closed.[ClosedOn], @Today) > m.[LastOn]
                            THEN ISNULL(closed.[ClosedOn], @Today) ELSE m.[LastOn] END,
                       2, ISNULL(c.[CancellationOrReversalReason], @Note),
                       NULL, NULL,
                       -- UpdatedAt is nullable and a legacy row may have neither, so the audit
                       -- instant falls back through it to now rather than to a NULL insert.
                       COALESCE(closed.[OccurredAt], c.[UpdatedAt], SYSUTCDATETIME())
                FROM [BookingCommissions] c
                CROSS APPLY (
                    SELECT [Accrued] = SUM(x.[Amount]), [LastOn] = MAX(x.[AccruedOn])
                    FROM [CommissionAccruals] x WHERE x.[CommissionId] = c.[Id]
                ) m
                OUTER APPLY (
                    SELECT TOP (1) a.[OccurredAt],
                           [ClosedOn] = dbo.CommissionAccrualBackfillDate(a.[OccurredAt], @Baseline)
                    FROM [FinancialWorkflowAuditEntries] a
                    WHERE a.[CommissionId] = c.[Id] AND a.[NewCommissionStatus] IN (2, 3, 4)
                    ORDER BY a.[OccurredAt], a.[Id]
                ) closed
                WHERE c.[Status] IN (2, 3, 4) AND m.[Accrued] IS NOT NULL AND m.[Accrued] <> 0;
            END
            """;

        /// <summary>
        /// One place for "the Pakistan business date this happened on, never before the committed
        /// opening balances" — used four times by the backfill and dropped again as soon as it is
        /// done, so nothing outside this migration depends on it.
        /// </summary>
        private const string DateHelper = """
            CREATE FUNCTION dbo.CommissionAccrualBackfillDate(@Utc datetime2, @Baseline date)
            RETURNS date
            AS
            BEGIN
                DECLARE @Business date = CAST(DATEADD(hour, 5, @Utc) AS date);
                RETURN CASE WHEN @Baseline IS NOT NULL AND @Business < @Baseline
                            THEN @Baseline ELSE @Business END;
            END
            """;
    }
}
