using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSecuritiesAndAdvancesExpenseHead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The head new security-deposit / advance spending is recorded against, now that the
            // client has confirmed such payments are a cost when paid rather than a new asset. The
            // inherited "Securities & Advances" RECEIVABLE account is untouched by this: it keeps
            // its ERP opening balance, and nothing here reclassifies it.
            //
            // Guarded rather than a plain InsertData, and the three cases are kept apart on purpose.
            // Already there by code: adopt it and do nothing. Id 59 taken by something else: STOP,
            // loudly — carrying on would leave the client with no head to record securities and
            // advances against, and nothing would ever say so. Neither: insert it.
            //
            // Id 59 is a gap in the original seeded range and user-created heads take identity values
            // above it, so the collision branch should never fire; a hand-repaired or partially
            // restored database is the case it exists for. The id is pinned rather than left to
            // identity because the model seeds this head at 59, and a row whose id disagrees with the
            // model would make a future generated migration edit the wrong row.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [ExpenseCategories] WHERE [Code] = N'securities_advances')
                BEGIN
                    IF EXISTS (SELECT 1 FROM [ExpenseCategories] WHERE [Id] = 59)
                        THROW 51059, N'Cannot seed the Securities & Advances expense head: ExpenseCategories.Id 59 is already used by a different category. Move that row to a new id, or insert the securities_advances head by hand, then re-run this migration.', 1;

                    SET IDENTITY_INSERT [ExpenseCategories] ON;
                    INSERT INTO [ExpenseCategories]
                        ([Id], [AnnualThreshold], [Code], [CreatedAt], [CreatedByUserId], [Description],
                         [DisplayOrder], [FilerRate], [IsActive], [IsWhtApplicable], [Name],
                         [NonFilerRate], [TaxSection], [UpdatedAt])
                    VALUES
                        (59, 0, N'securities_advances', '2026-08-11T00:00:00', NULL,
                         N'Security deposits and advances paid out — recorded as a cost when paid. The historical Securities & Advances balance carried over from the previous ERP stays on its own balance-sheet account and is unaffected.',
                         575, 0, 1, 0, N'Securities & Advances', 0, NULL, NULL);
                    SET IDENTITY_INSERT [ExpenseCategories] OFF;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the row this migration would have created, and only while nothing has been filed
            // against it — deleting a head an expense already points at would either fail on the
            // foreign key or orphan real financial history.
            migrationBuilder.Sql("""
                DELETE FROM [ExpenseCategories]
                WHERE [Id] = 59 AND [Code] = N'securities_advances'
                  AND NOT EXISTS (SELECT 1 FROM [Expenses] WHERE [CategoryId] = 59)
                  AND NOT EXISTS (SELECT 1 FROM [AssetPurchases] WHERE [CategoryId] = 59);
                """);
        }
    }
}
