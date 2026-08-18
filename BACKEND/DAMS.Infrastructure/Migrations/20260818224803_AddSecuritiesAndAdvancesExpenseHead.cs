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
            // Guarded rather than a plain InsertData. Id 59 is a gap in the original seeded range and
            // user-created heads take identity values above it, so a collision is unlikely — but a
            // hand-repaired or partially restored database could hold either that id or that code,
            // and a migration must not take a deployment down over one reference row. Nothing at
            // runtime looks this head up by id, so skipping it is safe; failing is not.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [ExpenseCategories] WHERE [Id] = 59 OR [Code] = N'securities_advances')
                BEGIN
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
