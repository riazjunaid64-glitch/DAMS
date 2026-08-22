using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinanceStatementsOpeningBalancesAndPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RevenueCategoryId",
                table: "ManualRevenues",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevenueTypeName",
                table: "ManualRevenues",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "FinanceAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LedgerCode",
                table: "FinanceAccounts",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CapitalPartners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Cnic = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Ntn = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ProfitSharePercent = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    FinanceAccountId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    JoinedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExitedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapitalPartners", x => x.Id);
                    table.CheckConstraint("CK_CapitalPartners_Share", "[ProfitSharePercent] >= 0 AND [ProfitSharePercent] <= 100");
                    table.ForeignKey(
                        name: "FK_CapitalPartners_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalanceSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AsAtDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsCommitted = table.Column<bool>(type: "bit", nullable: false),
                    CommittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CommittedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceSets", x => x.Id);
                    table.CheckConstraint("CK_OpeningBalanceSets_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "RevenueCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevenueCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CapitalTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CapitalPartnerId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinanceAccountId = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProfitSharePercentSnapshot = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapitalTransactions", x => x.Id);
                    table.CheckConstraint("CK_CapitalTransactions_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_CapitalTransactions_CashSide", "([Type] IN (2, 3) AND [FinanceAccountId] IS NOT NULL) OR ([Type] NOT IN (2, 3) AND [FinanceAccountId] IS NULL)");
                    table.CheckConstraint("CK_CapitalTransactions_ProfitSnapshot", "([Type] = 4 AND [ProfitSharePercentSnapshot] IS NOT NULL AND [ProfitSharePercentSnapshot] >= 0 AND [ProfitSharePercentSnapshot] <= 100) OR ([Type] <> 4 AND [ProfitSharePercentSnapshot] IS NULL)");
                    table.CheckConstraint("CK_CapitalTransactions_Type", "[Type] >= 1 AND [Type] <= 5");
                    table.ForeignKey(
                        name: "FK_CapitalTransactions_CapitalPartners_CapitalPartnerId",
                        column: x => x.CapitalPartnerId,
                        principalTable: "CapitalPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapitalTransactions_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalanceAuditEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OpeningBalanceSetId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceAuditEntries_OpeningBalanceSets_OpeningBalanceSetId",
                        column: x => x.OpeningBalanceSetId,
                        principalTable: "OpeningBalanceSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalanceEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OpeningBalanceSetId = table.Column<int>(type: "int", nullable: false),
                    FinanceAccountId = table.Column<int>(type: "int", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceEntries", x => x.Id);
                    table.CheckConstraint("CK_OpeningBalanceEntry_NonNegative", "[DebitAmount] >= 0 AND [CreditAmount] >= 0");
                    table.CheckConstraint("CK_OpeningBalanceEntry_OneSide", "[DebitAmount] = 0 OR [CreditAmount] = 0");
                    table.ForeignKey(
                        name: "FK_OpeningBalanceEntries_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceEntries_OpeningBalanceSets_OpeningBalanceSetId",
                        column: x => x.OpeningBalanceSetId,
                        principalTable: "OpeningBalanceSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "RevenueCategories",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedByUserId", "Description", "DisplayOrder", "IsActive", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "transfer_charges", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 10, true, "Transfer Charges", null },
                    { 2, "development_charges", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 20, true, "Development Charges", null },
                    { 3, "possession_charges", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 30, true, "Possession Charges", null },
                    { 4, "membership_charges", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 40, true, "Membership Charges", null },
                    { 5, "documentation_charges", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 50, true, "Documentation Charges", null },
                    { 6, "noc_ndc_charges", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 60, true, "NOC / NDC Charges", null },
                    { 7, "utility_connection_charges", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 70, true, "Utility Connection Charges", null },
                    { 8, "parking_charges", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 80, true, "Parking Charges", null },
                    { 9, "late_payment_surcharge", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 90, true, "Late Payment Surcharge", null },
                    { 10, "cancellation_forfeiture", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 100, true, "Cancellation / Forfeiture", null },
                    { 11, "rental_income", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 110, true, "Rental Income", null },
                    { 12, "commission_income", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 120, true, "Commission Income", null },
                    { 13, "bank_profit_interest", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 130, true, "Bank Profit / Interest", null },
                    { 14, "other_income", new DateTime(2026, 8, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 140, true, "Other Income", null }
                });

            // Snapshot every historic label first. Only a byte-for-byte category-name match is
            // classified automatically; case and whitespace variants become inactive review
            // categories with a hash suffix, so no production value is silently merged or lost.
            migrationBuilder.Sql("""
                UPDATE [ManualRevenues]
                SET [RevenueTypeName] = [RevenueType];

                UPDATE r
                SET [RevenueCategoryId] = c.[Id]
                FROM [ManualRevenues] r
                INNER JOIN [RevenueCategories] c
                    ON r.[RevenueType] COLLATE Latin1_General_100_BIN2 = c.[Name] COLLATE Latin1_General_100_BIN2
                   AND DATALENGTH(r.[RevenueType]) = DATALENGTH(c.[Name]);

                INSERT INTO [RevenueCategories]
                    ([Name], [Code], [Description], [DisplayOrder], [IsActive], [CreatedByUserId], [CreatedAt], [UpdatedAt])
                SELECT
                    LEFT(N'Legacy — ' + x.[RevenueType], 139) + N' [' + LEFT(x.[Hash], 8) + N']',
                    N'legacy_' + x.[Hash],
                    N'Created automatically from an unmatched historic RevenueType. Review and reclassify manually if appropriate.',
                    2147483647, 0, NULL, SYSUTCDATETIME(), NULL
                FROM
                (
                    SELECT DISTINCT r.[RevenueType] COLLATE Latin1_General_100_BIN2 AS [RevenueType],
                        CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max), r.[RevenueType] COLLATE Latin1_General_100_BIN2)), 2) AS [Hash]
                    FROM [ManualRevenues] r
                    WHERE r.[RevenueCategoryId] IS NULL
                ) x
                WHERE NOT EXISTS
                (
                    SELECT 1 FROM [RevenueCategories] c
                    WHERE c.[Code] = N'legacy_' + x.[Hash]
                );

                UPDATE r
                SET [RevenueCategoryId] = c.[Id]
                FROM [ManualRevenues] r
                CROSS APPLY
                (
                    SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max), r.[RevenueType] COLLATE Latin1_General_100_BIN2)), 2) AS [Hash]
                ) h
                INNER JOIN [RevenueCategories] c ON c.[Code] = N'legacy_' + h.[Hash]
                WHERE r.[RevenueCategoryId] IS NULL;
                """);

            // Production setup preserves balances and user-entered descriptions. A known name
            // with the wrong type aborts loudly because silently linking it would corrupt every
            // statement; matching existing rows only receive missing reconciliation metadata.
            migrationBuilder.Sql("""
                DECLARE @Chart TABLE
                (
                    [Name] nvarchar(120) NOT NULL,
                    [Type] int NOT NULL,
                    [LedgerCode] nvarchar(30) NULL,
                    [DisplayOrder] int NOT NULL,
                    [Holder] nvarchar(150) NOT NULL
                );
                INSERT INTO @Chart ([Name], [Type], [LedgerCode], [DisplayOrder], [Holder]) VALUES
                    (N'HBL Bank', 2, N'4', 10, N'Seven Ventures'),
                    (N'HBL Bank 79488961-03', 2, N'4', 20, N'Seven Ventures'),
                    (N'Bank Alfalah 1010591891', 2, N'5', 30, N'Seven Ventures'),
                    (N'Bank Alfalah 91010840084', 2, N'31', 40, N'Seven Ventures'),
                    (N'Bank Alfalah 91010841873', 2, N'31', 50, N'Seven Ventures'),
                    (N'Alfalah Saving', 2, NULL, 60, N'Seven Ventures'),
                    (N'Cash Account', 1, N'6', 70, N'Seven Ventures'),
                    (N'Cost of Plot', 7, N'3', 200, N'Seven Ventures'),
                    (N'Office Equipment', 7, N'7', 210, N'Seven Ventures'),
                    (N'Office Furniture & Fixture', 7, N'23', 220, N'Seven Ventures'),
                    (N'Mobile Phone & SIM Cards', 7, N'24', 230, N'Seven Ventures'),
                    (N'Floria Building — Work in Progress', 9, N'26', 300, N'Seven Ventures'),
                    (N'Work in Progress — Site Office', 9, N'29', 310, N'Seven Ventures'),
                    (N'Securities & Advances', 8, N'19', 400, N'Seven Ventures'),
                    (N'WHT TAX', 8, N'37', 410, N'Seven Ventures'),
                    (N'Customer General Account / Customer Deposits', 5, N'1', 500, N'Seven Ventures'),
                    (N'Tax Payable', 5, N'11', 510, N'Seven Ventures'),
                    (N'Loan A/C', 5, N'32', 520, N'Seven Ventures'),
                    (N'M. Shahid Omer Capital', 6, NULL, 600, N'M. Shahid Omer'),
                    (N'Yasir Arfat Anjum Capital', 6, NULL, 610, N'Yasir Arfat Anjum'),
                    (N'Nadeem Akhtar Satti Capital', 6, NULL, 620, N'Nadeem Akhtar Satti'),
                    (N'Imtiaz Raheem Capital', 6, NULL, 630, N'Imtiaz Raheem'),
                    (N'Muhammad Tayyab Khan Capital', 6, NULL, 640, N'Muhammad Tayyab Khan'),
                    (N'Muhammad Afzal Capital', 6, NULL, 650, N'Muhammad Afzal'),
                    (N'Syed Iqbal Mian Capital', 6, NULL, 660, N'Syed Iqbal Mian'),
                    (N'Ayub Satti Capital', 6, NULL, 670, N'Ayub Satti'),
                    (N'Shabbir Hussain Capital', 6, NULL, 680, N'Shabbir Hussain');

                IF EXISTS
                (
                    SELECT 1 FROM [FinanceAccounts] a
                    INNER JOIN @Chart c ON a.[Name] = c.[Name]
                    WHERE a.[Type] <> c.[Type]
                )
                    THROW 51000, 'A verified chart account already exists with the wrong account type. Correct it before applying this migration.', 1;

                UPDATE a
                SET a.[LedgerCode] = COALESCE(a.[LedgerCode], c.[LedgerCode]),
                    a.[DisplayOrder] = CASE WHEN a.[DisplayOrder] = 0 THEN c.[DisplayOrder] ELSE a.[DisplayOrder] END
                FROM [FinanceAccounts] a
                INNER JOIN @Chart c ON a.[Name] = c.[Name] AND a.[Type] = c.[Type];

                INSERT INTO [FinanceAccounts]
                    ([Name], [Type], [AccountHolderName], [OpeningBalance], [LedgerCode], [DisplayOrder],
                     [BankOrWalletName], [Description], [IsActive], [CreatedAt], [UpdatedAt])
                SELECT c.[Name], c.[Type], c.[Holder], 0, c.[LedgerCode], c.[DisplayOrder],
                       NULL, N'Seeded from the verified Seven Ventures chart of accounts.', 1, SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM @Chart c
                WHERE NOT EXISTS (SELECT 1 FROM [FinanceAccounts] a WHERE a.[Name] = c.[Name]);

                DECLARE @Partners TABLE ([Name] nvarchar(200) NOT NULL, [AccountName] nvarchar(120) NOT NULL);
                INSERT INTO @Partners VALUES
                    (N'M. Shahid Omer', N'M. Shahid Omer Capital'),
                    (N'Yasir Arfat Anjum', N'Yasir Arfat Anjum Capital'),
                    (N'Nadeem Akhtar Satti', N'Nadeem Akhtar Satti Capital'),
                    (N'Imtiaz Raheem', N'Imtiaz Raheem Capital'),
                    (N'Muhammad Tayyab Khan', N'Muhammad Tayyab Khan Capital'),
                    (N'Muhammad Afzal', N'Muhammad Afzal Capital'),
                    (N'Syed Iqbal Mian', N'Syed Iqbal Mian Capital'),
                    (N'Ayub Satti', N'Ayub Satti Capital'),
                    (N'Shabbir Hussain', N'Shabbir Hussain Capital');

                INSERT INTO [CapitalPartners]
                    ([Name], [Cnic], [Ntn], [ProfitSharePercent], [FinanceAccountId], [IsActive],
                     [JoinedDate], [ExitedDate], [CreatedAt])
                SELECT p.[Name], NULL, NULL, 11.1111, a.[Id], 1, NULL, NULL, SYSUTCDATETIME()
                FROM @Partners p
                INNER JOIN [FinanceAccounts] a ON a.[Name] = p.[AccountName] AND a.[Type] = 6
                WHERE NOT EXISTS (SELECT 1 FROM [CapitalPartners] cp WHERE cp.[Name] = p.[Name]);

                UPDATE cp
                SET cp.[FinanceAccountId] = a.[Id]
                FROM [CapitalPartners] cp
                INNER JOIN @Partners p ON p.[Name] = cp.[Name]
                INNER JOIN [FinanceAccounts] a ON a.[Name] = p.[AccountName] AND a.[Type] = 6
                WHERE cp.[FinanceAccountId] IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ManualRevenues_RevenueCategoryId",
                table: "ManualRevenues",
                column: "RevenueCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAccounts_LedgerCode",
                table: "FinanceAccounts",
                column: "LedgerCode");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAccounts_Type_DisplayOrder",
                table: "FinanceAccounts",
                columns: new[] { "Type", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CapitalPartners_FinanceAccountId",
                table: "CapitalPartners",
                column: "FinanceAccountId",
                unique: true,
                filter: "[FinanceAccountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CapitalPartners_Name",
                table: "CapitalPartners",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapitalTransactions_CapitalPartnerId_Date",
                table: "CapitalTransactions",
                columns: new[] { "CapitalPartnerId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_CapitalTransactions_FinanceAccountId",
                table: "CapitalTransactions",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceAuditEntries_OpeningBalanceSetId_OccurredAt",
                table: "OpeningBalanceAuditEntries",
                columns: new[] { "OpeningBalanceSetId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceEntries_FinanceAccountId",
                table: "OpeningBalanceEntries",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceEntries_OpeningBalanceSetId_FinanceAccountId",
                table: "OpeningBalanceEntries",
                columns: new[] { "OpeningBalanceSetId", "FinanceAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceSets_AsAtDate",
                table: "OpeningBalanceSets",
                column: "AsAtDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevenueCategories_Code",
                table: "RevenueCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevenueCategories_IsActive_DisplayOrder",
                table: "RevenueCategories",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RevenueCategories_Name",
                table: "RevenueCategories",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ManualRevenues_RevenueCategories_RevenueCategoryId",
                table: "ManualRevenues",
                column: "RevenueCategoryId",
                principalTable: "RevenueCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ManualRevenues_RevenueCategories_RevenueCategoryId",
                table: "ManualRevenues");

            migrationBuilder.DropTable(
                name: "CapitalTransactions");

            migrationBuilder.DropTable(
                name: "OpeningBalanceAuditEntries");

            migrationBuilder.DropTable(
                name: "OpeningBalanceEntries");

            migrationBuilder.DropTable(
                name: "RevenueCategories");

            migrationBuilder.DropTable(
                name: "CapitalPartners");

            migrationBuilder.DropTable(
                name: "OpeningBalanceSets");

            migrationBuilder.DropIndex(
                name: "IX_ManualRevenues_RevenueCategoryId",
                table: "ManualRevenues");

            migrationBuilder.DropIndex(
                name: "IX_FinanceAccounts_LedgerCode",
                table: "FinanceAccounts");

            migrationBuilder.DropIndex(
                name: "IX_FinanceAccounts_Type_DisplayOrder",
                table: "FinanceAccounts");

            migrationBuilder.DropColumn(
                name: "RevenueCategoryId",
                table: "ManualRevenues");

            migrationBuilder.DropColumn(
                name: "RevenueTypeName",
                table: "ManualRevenues");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "FinanceAccounts");

            migrationBuilder.DropColumn(
                name: "LedgerCode",
                table: "FinanceAccounts");
        }
    }
}
