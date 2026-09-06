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

            migrationBuilder.Sql(Backfill);

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
        /// One Recognition row per commission the company still owes something on — Pending (0) or
        /// Paid (1). Cancelled (2), ReversalRequired (3) and Reversed (4) are closed records that owe
        /// nothing, so they get no rows at all: an accrual and its release in the same breath would
        /// net to zero in the payable anyway, and there is no dated record of WHEN they closed, so
        /// inventing two dates would only risk putting the pair in different periods.
        /// <para>
        /// A ReversalRequired commission with money already paid therefore lands as a NEGATIVE
        /// payable, which is what it is: the sale was cancelled, the commission is not a cost, and
        /// the amount already paid is recoverable from the partner. The workspace already reports
        /// that figure as "Recovery due".
        /// </para>
        /// <para>
        /// The date is the commission's own creation day, converted from UTC to the Pakistan
        /// business date the rest of the finance module uses, and never earlier than a committed
        /// opening-balance date — everything before that is already inside the accountant's figures
        /// and the P&amp;L window starts there. Re-runnable: nothing is inserted for a commission that
        /// already has an accrual row.
        /// </para>
        /// </summary>
        private const string Backfill = """
            DECLARE @Baseline date = (
                SELECT MAX([AsAtDate]) FROM [OpeningBalanceSets] WHERE [CommittedAt] IS NOT NULL);

            INSERT INTO [CommissionAccruals]
                ([CommissionId], [Amount], [AccruedOn], [Kind], [Reason], [RecordedByUserId], [RecordedByName], [RecordedAt])
            SELECT
                c.[Id],
                c.[FinalAmount],
                CASE
                    WHEN @Baseline IS NOT NULL AND CAST(DATEADD(hour, 5, c.[CreatedAt]) AS date) < @Baseline
                        THEN @Baseline
                    ELSE CAST(DATEADD(hour, 5, c.[CreatedAt]) AS date)
                END,
                0,
                N'Backfilled when commission accrual accounting was introduced.',
                c.[CreatedByUserId],
                c.[CreatedByName],
                c.[CreatedAt]
            FROM [BookingCommissions] c
            WHERE c.[Status] IN (0, 1)
              AND c.[FinalAmount] <> 0
              AND NOT EXISTS (SELECT 1 FROM [CommissionAccruals] a WHERE a.[CommissionId] = c.[Id]);
            """;
    }
}
