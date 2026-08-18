using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingSaleRecognitionAndCustomerAccountRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Preflight: refuse the deployment rather than land a wrong ledger ─────────────
            //
            // Everything below either recognises a sale or establishes the account its money sits
            // in. Both can be defeated by production data this migration cannot repair on its own,
            // and both fail QUIETLY when they are: an unrecognised sale keeps its cash as a
            // customer deposit, and the balance sheet still balances — Bank up, Deposits up,
            // Revenue nil. Nothing is out of balance, so no diagnostic fires and nobody is told.
            //
            // So the checks run first and throw. A migration that cannot produce a correct ledger
            // must stop the deployment, not complete and leave someone to notice next quarter.
            migrationBuilder.Sql("""
                DECLARE @undatable int = (
                    SELECT COUNT(*) FROM [Bookings] b
                    WHERE b.[Status] IN (2, 3)
                      AND COALESCE(b.[PossessionDate], b.[CompletionDate]) IS NULL);
                DECLARE @negative int = (
                    SELECT COUNT(*) FROM [Bookings] b
                    WHERE b.[Status] IN (2, 3)
                      AND (b.[AgreedSalePrice] - b.[DiscountAmount]) < 0);
                IF @undatable > 0 OR @negative > 0
                BEGIN
                    DECLARE @msg nvarchar(2048) = CONCAT(
                        N'Revenue recognition cannot be backfilled: ', @undatable,
                        N' possessed/completed booking(s) have neither a possession nor a completion date, and ',
                        @negative,
                        N' have a discount larger than the agreed sale price. Each one would keep its cash as a customer deposit and report no revenue at all, on a balance sheet that still balances. ',
                        N'Fix them first: SELECT [Id], [BookingReference], [Status], [PossessionDate], [CompletionDate], [AgreedSalePrice], [DiscountAmount] FROM [Bookings] WHERE [Status] IN (2,3) AND (COALESCE([PossessionDate],[CompletionDate]) IS NULL OR ([AgreedSalePrice] - [DiscountAmount]) < 0);');
                    THROW 51000, @msg, 1;
                END
                """);

            // The two account statements further down adopt an existing account by name, or create
            // one. An account holding that name with the wrong type or another system role defeats
            // both halves at once — the UPDATE matches nothing, and the INSERT is blocked by the
            // name it was meant to claim — leaving the migration "successful" with no account to
            // post to.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [FinanceAccounts]
                           WHERE [Name] = N'Customer General Account / Customer Deposits'
                             AND ([Type] <> 5 OR [SystemRole] NOT IN (0, 3)))
                    THROW 51001, N'An account named "Customer General Account / Customer Deposits" already exists but is not an unclaimed Liability account, so it cannot take the Customer Deposits role. Retype it to Liability and clear its system role, or rename it, then re-run.', 1;
                IF EXISTS (SELECT 1 FROM [FinanceAccounts]
                           WHERE [Name] = N'Customer Receivables'
                             AND ([Type] <> 8 OR [SystemRole] NOT IN (0, 4)))
                    THROW 51001, N'An account named "Customer Receivables" already exists but is not an unclaimed Receivable account, so it cannot take the Customer Receivables role. Retype it to Receivable and clear its system role, or rename it, then re-run.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts");

            migrationBuilder.CreateTable(
                name: "BookingSaleRecognitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    RecognitionDate = table.Column<DateTime>(type: "date", nullable: false),
                    NetSaleValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RecognizedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecognizedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingSaleRecognitions", x => x.Id);
                    table.CheckConstraint("CK_BookingSaleRecognitions_NetSaleValue", "[NetSaleValue] >= 0");
                    table.ForeignKey(
                        name: "FK_BookingSaleRecognitions_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_CustomerDepositsRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] <> 3 OR [Type] = 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_CustomerReceivablesRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] <> 4 OR [Type] = 8");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] >= 0 AND [SystemRole] <= 4");

            migrationBuilder.CreateIndex(
                name: "IX_BookingSaleRecognitions_BookingId",
                table: "BookingSaleRecognitions",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingSaleRecognitions_RecognitionDate",
                table: "BookingSaleRecognitions",
                column: "RecognitionDate");

            // ── Backfill: sales that were already recognised before this table existed ────────
            //
            // Every booking that reached PossessionGiven (2) or SaleCompleted (3) became revenue
            // at some point in the past. Without a row here it would either vanish from income
            // entirely or — worse — be re-dated to whenever the report happens to be run.
            //
            // The date is taken from the record itself, never from "now":
            //   • PossessionDate where possession actually happened;
            //   • CompletionDate for legacy rows that completed without possession (a path this
            //     change closes for new work, but which existing data took).
            //
            // Two kinds of row cannot be recognised from the record alone:
            //   • a recognised-status booking with neither date — there is nothing to date it by,
            //     and inventing one would put revenue in a period it never belonged to. Both dates
            //     are written unconditionally by the application, so this can only come from
            //     imported or hand-edited data.
            //   • a booking whose discount exceeds its agreed price, which has no meaningful net
            //     sale value.
            // Neither is skipped: the preflight at the top of this migration has already refused
            // the deployment if any exist. The WHERE clause below still spells the conditions out
            // so this statement is correct on its own terms rather than only in company.
            //
            // NOT EXISTS makes it re-runnable; the unique index on BookingId makes it safe anyway.
            migrationBuilder.Sql("""
                INSERT INTO [BookingSaleRecognitions]
                    ([BookingId], [RecognitionDate], [NetSaleValue], [RecognizedAt], [RecognizedByUserId])
                SELECT
                    b.[Id],
                    CAST(COALESCE(b.[PossessionDate], b.[CompletionDate]) AS date),
                    b.[AgreedSalePrice] - b.[DiscountAmount],
                    COALESCE(b.[PossessionDate], b.[CompletionDate]),
                    NULL
                FROM [Bookings] b
                WHERE b.[Status] IN (2, 3)
                  AND COALESCE(b.[PossessionDate], b.[CompletionDate]) IS NOT NULL
                  AND (b.[AgreedSalePrice] - b.[DiscountAmount]) >= 0
                  AND NOT EXISTS (
                      SELECT 1 FROM [BookingSaleRecognitions] r WHERE r.[BookingId] = b.[Id]);
                """);

            // ── System accounts for the deposit liability and the receivable asset ────────────
            //
            // Attach, do not duplicate. The client's chart already carries the deposit account
            // under its ERP name, so it is adopted in place rather than shadowed by a second one.
            // The filtered unique index on SystemRole means at most one account can hold a role,
            // so every statement below is guarded by "no account holds it yet".
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [SystemRole] = 3)
                    UPDATE TOP (1) [FinanceAccounts]
                    SET [SystemRole] = 3, [UpdatedAt] = SYSUTCDATETIME()
                    WHERE [SystemRole] = 0 AND [Type] = 5
                      AND [Name] = N'Customer General Account / Customer Deposits';
                """);

            // Nothing named that in this database — create the liability so customer money has
            // somewhere to sit. Skipped entirely on an empty chart (a brand-new database gets its
            // accounts from the chart setup instead, which would otherwise collide on the name).
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [SystemRole] = 3)
                   AND NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [Name] = N'Customer General Account / Customer Deposits')
                   AND EXISTS (SELECT 1 FROM [FinanceAccounts])
                    INSERT INTO [FinanceAccounts]
                        ([Name], [Type], [AccountHolderName], [OpeningBalance], [LedgerCode], [DisplayOrder],
                         [SystemRole], [BankOrWalletName], [Description], [IsActive], [CreatedAt], [UpdatedAt])
                    SELECT N'Customer General Account / Customer Deposits', 5,
                        COALESCE(
                            (SELECT TOP (1) [AccountHolderName] FROM [FinanceAccounts] WHERE [SystemRole] = 1),
                            (SELECT TOP (1) [AccountHolderName] FROM [FinanceAccounts] ORDER BY [DisplayOrder], [Id])),
                        0, N'1', 500, 3, NULL, NULL, 1, SYSUTCDATETIME(), SYSUTCDATETIME();
                """);

            // The receivable has no counterpart in the client's imported chart, so it is adopted
            // by name if someone already made one, and created otherwise. No ledger code: there is
            // no legitimate ERP code for it, and a made-up one would be worse than none.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [SystemRole] = 4)
                    UPDATE TOP (1) [FinanceAccounts]
                    SET [SystemRole] = 4, [UpdatedAt] = SYSUTCDATETIME()
                    WHERE [SystemRole] = 0 AND [Type] = 8 AND [Name] = N'Customer Receivables';
                """);
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [SystemRole] = 4)
                   AND NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [Name] = N'Customer Receivables')
                   AND EXISTS (SELECT 1 FROM [FinanceAccounts])
                    INSERT INTO [FinanceAccounts]
                        ([Name], [Type], [AccountHolderName], [OpeningBalance], [LedgerCode], [DisplayOrder],
                         [SystemRole], [BankOrWalletName], [Description], [IsActive], [CreatedAt], [UpdatedAt])
                    SELECT N'Customer Receivables', 8,
                        COALESCE(
                            (SELECT TOP (1) [AccountHolderName] FROM [FinanceAccounts] WHERE [SystemRole] = 1),
                            (SELECT TOP (1) [AccountHolderName] FROM [FinanceAccounts] ORDER BY [DisplayOrder], [Id])),
                        0, NULL, 420, 4, NULL, NULL, 1, SYSUTCDATETIME(), SYSUTCDATETIME();
                """);

            // Postcondition. The preflight covers the failure modes that are known; this covers
            // the ones that are not. If a chart exists at all, both roles must be held by the time
            // this migration finishes — anything else means customer money has no account to sit
            // in, which is a broken ledger no matter which guard let it through.
            // An empty chart is exempt: a brand-new database gets its accounts from chart setup,
            // which assigns both roles itself.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [FinanceAccounts])
                   AND (NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [SystemRole] = 3)
                        OR NOT EXISTS (SELECT 1 FROM [FinanceAccounts] WHERE [SystemRole] = 4))
                    THROW 51002, N'Migration finished without establishing both customer system accounts (Customer Deposits and Customer Receivables). Customer money would have no account to sit in and the balance sheet would not balance. Inspect [FinanceAccounts] for name or type collisions and re-run.', 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Release the two roles before the constraint that permits them is dropped, or the
            // narrowed CK_FinanceAccounts_SystemRole cannot be re-added. The accounts themselves
            // are left in place: they may already carry balances, and deleting a chart account is
            // never something a schema rollback should decide.
            migrationBuilder.Sql(
                "UPDATE [FinanceAccounts] SET [SystemRole] = 0, [UpdatedAt] = SYSUTCDATETIME() WHERE [SystemRole] IN (3, 4);");

            migrationBuilder.DropTable(
                name: "BookingSaleRecognitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_CustomerDepositsRole",
                table: "FinanceAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_CustomerReceivablesRole",
                table: "FinanceAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAccounts_SystemRole",
                table: "FinanceAccounts",
                sql: "[SystemRole] >= 0 AND [SystemRole] <= 2");
        }
    }
}
