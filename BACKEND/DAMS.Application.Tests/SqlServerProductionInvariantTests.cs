using DAMS.Api.Filters;
using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.DTOs.CustomerDocumentDtos;
using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.DTOs.WhtDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data.Common;
using System.IO.Compression;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DAMS_SQLSERVER_TEST_CONNECTION")))
            Skip = "Set DAMS_SQLSERVER_TEST_CONNECTION to run the real SQL Server invariant suite.";
    }
}

public sealed class SqlServerProductionInvariantTests
{
    private static readonly FinancialWorkflowActor Actor = new(901, "SQL Concurrency Admin");

    /// <summary>The smallest byte sequence the upload validator accepts as a PDF.</summary>
    private static readonly byte[] ProbePdf =
        System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF");

    [SqlServerFact]
    public async Task MigrationsTransactionsAndConcurrentPayouts_PreserveProductionInvariants()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int bookingId;
        int partnerId;
        int accountId;
        await using (var db = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "SQL invariants", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project, UnitNumber = "SQL-01", UnitType = "Apartment",
                Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan
            };
            var customer = new Customer
            {
                FullName = "SQL Customer", Phone = "03009999999", Status = CustomerStatus.Active
            };
            var booking = new Booking
            {
                BookingReference = $"SQL-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = 1_000_000m,
                BookingAmountRequired = 100_000m, BookingAmountReceived = 100_000m,
                BookingDate = DateTime.UtcNow
            };
            var partner = new ThirdPartyPartner
            {
                Name = "SQL Broker", PartnerType = "Broker", InternalCode = "SQL-BROKER",
                IsActive = true, BankName = "Test Bank", AccountTitle = "SQL Broker", AccountNumber = "001"
            };
            var account = new FinanceAccount
            {
                Name = "SQL Operations", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true
            };
            db.AddRange(project, unit, customer, booking, partner, account);
            await db.SaveChangesAsync();
            bookingId = booking.Id; partnerId = partner.Id; accountId = account.Id;

            db.BookingCommissions.AddRange(
                Commission(bookingId, partnerId, BookingCommissionStatus.Cancelled, 10m),
                Commission(bookingId, partnerId, BookingCommissionStatus.Rejected, 20m));
            db.CustomerRebates.AddRange(
                Rebate(bookingId, customer.Id, CustomerRebateStatus.Cancelled, 10m),
                Rebate(bookingId, customer.Id, CustomerRebateStatus.Rejected, 20m));
            await db.SaveChangesAsync();
        }

        // Downgrading across the filtered-index migration must preserve terminal replacements and
        // must not attempt to shrink immutable audit text. Re-applying must remain possible.
        await using (var db = new AppDbContext(options))
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260808173938_AddCommissionRebateManagement");
            Assert.Equal(2, await ScalarAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [BookingCommissions] WHERE [BookingId] = @id", bookingId));
            Assert.Equal(2, await ScalarAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [CustomerRebates] WHERE [BookingId] = @id", bookingId));
            await migrator.MigrateAsync();
        }

        // Category creation and bulk assignment are a single real transaction. A second-save
        // failure must leave neither a half-created category nor partial requirements.
        await using (var failing = new AppDbContext(Options(database.ConnectionString,
                         new FailDocumentAssignmentInterceptor())))
        {
            var documents = new CustomerDocumentService(failing, new NullPrivateStorage(),
                NullLogger<CustomerDocumentService>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => documents.CreateCategoryAsync(
                new CreateCustomerDocumentCategoryDto
                {
                    Name = "Atomic category", Code = "atomic_sql", IsRequiredByDefault = true,
                    AllowedFileTypes = [".pdf"], MaxFileSizeBytes = 1024 * 1024,
                    AssignmentMode = CustomerDocumentAssignmentMode.AllActiveCustomers
                }, new CustomerDocumentActor(Actor.UserId, Actor.DisplayName)));
        }
        await using (var verify = new AppDbContext(options))
        {
            Assert.False(await verify.CustomerDocumentCategories.AnyAsync(c => c.Code == "atomic_sql"));
            Assert.False(await verify.CustomerDocumentRequirements.AnyAsync(r => r.Category != null
                && r.Category.Code == "atomic_sql"));

            verify.BookingCommissions.Add(Commission(bookingId, partnerId, BookingCommissionStatus.Payable, 100m));
            await verify.SaveChangesAsync();
        }

        int commissionId;
        string token;
        await using (var db = new AppDbContext(options))
        {
            var commission = await db.BookingCommissions.SingleAsync(c => c.BookingId == bookingId
                && c.Status == BookingCommissionStatus.Payable);
            commissionId = commission.Id;
            token = Convert.ToBase64String(commission.RowVersion);
        }

        async Task<Exception?> PayAsync(string key)
        {
            try
            {
                await using var context = new AppDbContext(options);
                var service = new CommissionRebateService(context, new FinanceAccountService(context),
                    new NullPrivateStorage());
                await service.RecordPayoutAsync(bookingId, commissionId, new RecordCommissionPayoutDto
                {
                    FinanceAccountId = accountId, Amount = 75m, PaymentDate = DateTime.UtcNow.Date,
                    PaymentMethod = PaymentMethod.Cash, IdempotencyKey = key,
                    CommissionConcurrencyToken = token
                }, Actor);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        var outcomes = await Task.WhenAll(PayAsync("parallel-payout-a"), PayAsync("parallel-payout-b"));
        Assert.Single(outcomes, error => error == null);
        Assert.Single(outcomes, error => error != null);
        await using (var db = new AppDbContext(options))
        {
            Assert.Equal(75m, await db.CommissionPayouts.Where(p => p.CommissionId == commissionId)
                .SumAsync(p => p.Amount));
            Assert.Equal(BookingCommissionStatus.PartiallyPaid,
                (await db.BookingCommissions.FindAsync(commissionId))!.Status);
        }
    }

    [SqlServerFact]
    public async Task FinanceMigrationAndReports_RunOnTheRealSqlServerProvider()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260812002506_AddPaymentFinanceAccount");
        }

        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                INSERT INTO [FinanceAccounts]
                    ([Name], [Type], [AccountHolderName], [OpeningBalance], [BankOrWalletName],
                     [Description], [IsActive], [CreatedAt], [UpdatedAt])
                VALUES
                    (N'SQL Finance Legacy Bank', 2, N'DAMS', 0, NULL, NULL, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                DECLARE @AccountId int = CONVERT(int, SCOPE_IDENTITY());
                INSERT INTO [ManualRevenues]
                    ([ProjectId], [FinanceAccountId], [Amount], [RevenueType], [Description],
                     [Reference], [Date], [CreatedByUserId], [CreatedAt])
                VALUES
                    (NULL, @AccountId, 100, N'other income ', NULL, N'legacy-sql',
                     '2026-08-01', NULL, SYSUTCDATETIME());
                SELECT @AccountId;
                """, connection);
            _ = Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var legacy = await db.ManualRevenues.Include(r => r.RevenueCategory)
                .SingleAsync(r => r.Reference == "legacy-sql");
            Assert.Equal("other income ", legacy.RevenueTypeName);
            Assert.StartsWith("legacy_", legacy.RevenueCategory!.Code);
            Assert.False(legacy.RevenueCategory.IsActive);

            var accounts = new FinanceAccountService(db);
            var finance = new FinanceService(db, new NullPrivateStorage(), accounts,
                new WhtService(db, accounts), NullLogger<FinanceService>.Instance);
            var pnl = await finance.GetProfitAndLossAsync(null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
            Assert.Equal(100m, pnl.TotalIncome);
            Assert.Equal("Unclassified", Assert.Single(pnl.IncomeLines).Name);
            var trial = await finance.GetTrialBalanceAsync(null, new DateTime(2026, 8, 31), 0);
            Assert.True(Assert.Single(trial.ColumnBalanced));
            var sheet = await finance.GetBalanceSheetAsync(null, new DateTime(2026, 8, 31));
            Assert.True(sheet.IsBalanced);
            Assert.Equal(100m, sheet.TotalAssets);
            var export = await finance.ExportProfitAndLossAsync(null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
            using var archive = new ZipArchive(new MemoryStream(export.Content), ZipArchiveMode.Read);
            Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
        }
    }

    /// <summary>
    /// Deposits, recognition and receivables against the REAL provider.
    /// <para>
    /// These balances are derived, not stored, and every one of them is built from a query that
    /// reaches through a booking into its optional sale recognition. The in-memory provider will
    /// happily evaluate such a thing in C# and report a perfect balance sheet that SQL Server
    /// cannot produce at all — so the whole path (recognition-date comparisons, the conditional
    /// dating of a credit, the derived ledger rows unioned into the account detail) is exercised
    /// here with real rows, on real SQL.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task CustomerDepositsRecognitionAndReceivables_TranslateAndReconcile_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        // The migration's own backfill is what puts these two roles on the chart — the deposit
        // liability adopted in place under its existing ERP name, the receivable created because
        // the client's chart has no counterpart for it. If that step regressed, this is where it
        // shows up: there would be nothing to find.
        var deposits = await db.FinanceAccounts.SingleAsync(a => a.SystemRole == FinanceSystemAccountRole.CustomerDeposits);
        var receivables = await db.FinanceAccounts.SingleAsync(a => a.SystemRole == FinanceSystemAccountRole.CustomerReceivables);
        Assert.Equal("Customer General Account / Customer Deposits", deposits.Name);
        Assert.Equal(FinanceAccountType.Liability, deposits.Type);
        Assert.Equal("Customer Receivables", receivables.Name);
        Assert.Equal(FinanceAccountType.Receivable, receivables.Type);
        Assert.Null(receivables.LedgerCode); // no legitimate ERP code exists, so none is invented

        var bank = new FinanceAccount { Name = "SQL Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var project = new Project { ProjectName = "SQL Recognition", Location = "Karachi", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "R-1", UnitType = "Apartment", Price = 5_000_000m, Status = UnitStatus.OnPaymentPlan };
        var customer = new Customer { FullName = "SQL Buyer", Phone = "03007778888", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = "BK-SQL-REC", Customer = customer, Unit = unit, Source = CustomerSource.Referral,
            Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = 5_000_000m, DiscountAmount = 0m,
            BookingDate = new DateTime(2026, 1, 1)
        };
        db.AddRange(bank, project, unit, customer, booking);
        await db.SaveChangesAsync();

        db.Payments.Add(new Payment
        {
            BookingId = booking.Id, FinanceAccountId = bank.Id, Amount = 3_000_000m,
            Type = PaymentType.Installment, PaymentMethod = PaymentMethod.BankTransfer,
            PaidAt = new DateTime(2026, 1, 15)
        });
        await db.SaveChangesAsync();

        var accounts = new FinanceAccountService(db);
        var finance = new FinanceService(db, new NullPrivateStorage(), accounts,
            new WhtService(db, accounts), NullLogger<FinanceService>.Instance);

        // Before possession: a liability, not income.
        Assert.Equal(3_000_000m, (await accounts.GetByIdAsync(deposits.Id)).CurrentBalance);
        Assert.Equal(0m, (await finance.GetProfitAndLossAsync(null, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31))).TotalIncome);
        Assert.True((await finance.GetBalanceSheetAsync(null, new DateTime(2026, 1, 31))).IsBalanced);

        await new BookingService(db, new CustomerService(db), accounts)
            .GivePossessionAsync(booking.Id, new DateTime(2026, 2, 15), 1);

        // After possession: revenue in full, deposit cleared, the rest a receivable.
        var pnl = await finance.GetProfitAndLossAsync(null, new DateTime(2026, 1, 1), new DateTime(2026, 2, 28));
        Assert.Equal(5_000_000m, Assert.Single(pnl.IncomeLines, l => l.Name == "Unit Sales").Amount);
        Assert.Equal(0m, (await accounts.GetByIdAsync(deposits.Id)).CurrentBalance);
        Assert.Equal(2_000_000m, (await accounts.GetByIdAsync(receivables.Id)).CurrentBalance);

        var sheet = await finance.GetBalanceSheetAsync(null, new DateTime(2026, 2, 28));
        Assert.True(sheet.IsBalanced);
        var trial = await finance.GetTrialBalanceAsync(null, new DateTime(2026, 2, 28), 0);
        Assert.True(Assert.Single(trial.ColumnBalanced));

        // Historical integrity: a report that ends before possession must not see the sale.
        Assert.Equal(0m, (await finance.GetProfitAndLossAsync(null, new DateTime(2026, 1, 1), new DateTime(2026, 2, 14))).TotalIncome);

        // The derived ledgers must add up to the very numbers the Balance Sheet printed.
        var depositLedger = await accounts.GetTransactionsAsync(deposits.Id, 0, 50);
        Assert.Equal(0m, depositLedger.Items.Sum(t => t.Amount));
        var receivableLedger = await accounts.GetTransactionsAsync(receivables.Id, 0, 50);
        Assert.Equal(2_000_000m, receivableLedger.Items.Sum(t => t.Amount));

        var depositsPage = await finance.GetCustomerDepositPageAsync(null, new DateTime(2026, 2, 28), 0, 20);
        Assert.Empty(depositsPage.Items);
        var asAtJanuary = await finance.GetCustomerDepositPageAsync(null, new DateTime(2026, 1, 31), 0, 20);
        Assert.Equal(3_000_000m, Assert.Single(asAtJanuary.Items).DepositBalance);
    }

    /// <summary>
    /// The dashboard aggregation, on the real provider.
    /// <para>
    /// It is here rather than only in memory because of one construct the in-memory provider cannot
    /// vouch for: the non-cash customer credit is grouped by a CASE expression — the later of the
    /// credit's own date and the recognition it reduces — and in-memory LINQ will happily evaluate a
    /// key SQL Server may refuse to translate. A dashboard that throws the moment a client grants a
    /// credit note is not something to discover in production.
    /// </para>
    /// <para>
    /// It also pins the two things the screen promises: the bars total the cards, and the Total
    /// Expenses drill-down totals the Total Expenses card.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task TheDashboardAggregation_TranslatesAndReconciles_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var bank = new FinanceAccount { Name = "SQL Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var equipment = new FinanceAccount { Name = "SQL Equipment", AccountHolderName = "DAMS", Type = FinanceAccountType.FixedAsset, IsActive = true };
        var project = new Project { ProjectName = "SQL Dashboard", Location = "Karachi", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "D-1", UnitType = "Apartment", Price = 5_000_000m, Status = UnitStatus.OnPaymentPlan };
        var customer = new Customer { FullName = "SQL Buyer", Phone = "03007778899", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = "BK-SQL-DASH", Customer = customer, Unit = unit, Source = CustomerSource.Referral,
            Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = 5_000_000m, DiscountAmount = 0m,
            BookingDate = new DateTime(2026, 1, 1)
        };
        db.AddRange(bank, equipment, project, unit, customer, booking);
        await db.SaveChangesAsync();

        var accounts = new FinanceAccountService(db);
        var finance = new FinanceService(db, new NullPrivateStorage(), accounts,
            new WhtService(db, accounts), NullLogger<FinanceService>.Instance);
        await new BookingService(db, new CustomerService(db), accounts)
            .GivePossessionAsync(booking.Id, new DateTime(2026, 2, 15), 1);

        db.Expenses.Add(new Expense
        {
            FinanceAccountId = bank.Id, Category = "Office Rent", Amount = 400_000m,
            Date = new DateTime(2026, 3, 5)
        });
        db.AssetPurchases.Add(new AssetPurchase
        {
            AssetAccountId = equipment.Id, FinanceAccountId = bank.Id, Amount = 600_000m,
            ItemName = "SQL server rack", Category = "Equipment", Date = new DateTime(2026, 3, 10)
        });
        var rebate = new CustomerRebate
        {
            BookingId = booking.Id, CustomerId = customer.Id, BasisAmount = 5_000_000m,
            CalculatedAmount = 100_000m, FinalAmount = 100_000m, Reason = "Goodwill",
            Method = CustomerRebateMethod.CreditNote, Status = CustomerRebateStatus.Approved
        };
        db.Add(rebate);
        await db.SaveChangesAsync();
        // Granted BEFORE possession, so its effective date is the recognition date — the CASE
        // expression the trend has to group by, and the one this test exists for.
        db.RebateDisbursements.Add(new RebateDisbursement
        {
            RebateId = rebate.Id, Method = CustomerRebateMethod.CreditNote, Amount = 100_000m,
            AppliedAt = new DateTime(2026, 1, 20), IdempotencyKey = "sql-credit-1"
        });
        await db.SaveChangesAsync();

        var from = new DateTime(2026, 1, 1);
        var to = new DateTime(2026, 3, 31);
        var dashboard = await finance.GetDashboardAsync(null, from, to);

        Assert.Equal(5_000_000m, dashboard.Summary.TotalRevenue);
        Assert.Equal(1_100_000m, dashboard.Summary.TotalExpenses); // 400k + 600k + 100k credit
        Assert.Equal(3_900_000m, dashboard.Summary.NetProfit);

        // The bars total the cards, and every day of the range is inside exactly one of them.
        Assert.NotEmpty(dashboard.Trend);
        Assert.Equal(from, dashboard.Trend[0].From);
        Assert.Equal(to, dashboard.Trend[^1].To);
        for (var i = 1; i < dashboard.Trend.Count; i++)
            Assert.Equal(dashboard.Trend[i - 1].To.AddDays(1), dashboard.Trend[i].From);
        Assert.Equal(dashboard.Summary.TotalRevenue, dashboard.Trend.Sum(b => b.Revenue));
        Assert.Equal(dashboard.Summary.TotalExpenses, dashboard.Trend.Sum(b => b.Expense));
        // The credit landed in February with the possession, not in January when it was granted.
        Assert.Equal(100_000m, dashboard.Trend.Single(b => b.From <= new DateTime(2026, 2, 15)
            && b.To >= new DateTime(2026, 2, 15)).Expense);

        Assert.Equal(dashboard.Summary.TotalRevenue, dashboard.Distribution.Sum(s => s.Revenue));

        // And the drill-down behind the Total Expenses card totals that card.
        var breakdown = await finance.GetCostBreakdownPageAsync(null, from, to, 0, 100);
        Assert.Equal(dashboard.Summary.TotalExpenses, breakdown.Items.Sum(i => i.Amount));
        Assert.Equal(3, breakdown.Items.Count);

        // One Net Profit: the statement agrees with the card, and the sheet names the difference.
        var pnl = await finance.GetProfitAndLossAsync(null, from, to);
        Assert.Equal(dashboard.Summary.NetProfit, pnl.NetProfit);
        var sheet = await finance.GetBalanceSheetAsync(null, to);
        Assert.True(sheet.IsBalanced);
        Assert.Equal(600_000m, sheet.UnpostedFixedAssetCharge);
        Assert.Equal(pnl.NetProfit, sheet.RetainedProfit - sheet.UnpostedFixedAssetCharge);
    }

    // The migration immediately before revenue recognition. Migrating to it first leaves the
    // database in the exact state a real deployment starts from.
    private const string BeforeRecognition = "20260816215219_AddPageExclusivityAndSyncLease";

    [SqlServerFact]
    public async Task RecognitionMigration_RefusesToDeploy_WhenALegacySaleCannotBeDated()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = new AppDbContext(Options(database.ConnectionString));
        await db.GetService<IMigrator>().MigrateAsync(BeforeRecognition);

        // A completed sale carrying real cash, with neither a possession nor a completion date to
        // recognise it by — the shape imported or hand-edited data takes. Skipping it would leave
        // Bank +500k, Customer Deposits +500k, Revenue nil: perfectly balanced and completely
        // wrong, with no imbalance for any diagnostic to notice.
        var project = new Project { ProjectName = "Legacy", Location = "Multan", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "L-1", UnitType = "Apartment", Price = 500_000m, Status = UnitStatus.Sold };
        var customer = new Customer { FullName = "Legacy Buyer", Phone = "03004445555", Status = CustomerStatus.Active };
        db.AddRange(project, unit, customer, new Booking
        {
            BookingReference = "BK-LEGACY", Customer = customer, Unit = unit, Source = CustomerSource.Referral,
            Status = BookingStatus.SaleCompleted, AgreedSalePrice = 500_000m, DiscountAmount = 0m,
            BookingDate = new DateTime(2025, 3, 1), PossessionDate = null, CompletionDate = null
        });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.MigrateAsync());
        Assert.Contains("Revenue recognition cannot be backfilled", Flatten(error));
        Assert.Contains("BK-LEGACY", await ScalarAsync<string>(database.ConnectionString,
            "SELECT STRING_AGG([BookingReference], ',') FROM [Bookings] WHERE [Status] IN (2,3)"));

        // Fail closed means fail whole: the deployment rolls back rather than half-landing.
        Assert.Equal(0, await ScalarAsync<int>(database.ConnectionString,
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = 'BookingSaleRecognitions'"));

        // Give the sale a date it can be recognised by, and the same deployment now succeeds.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [Bookings] SET [CompletionDate] = '2025-06-15' WHERE [BookingReference] = 'BK-LEGACY'");
        await db.Database.MigrateAsync();
        var recognition = await db.BookingSaleRecognitions.SingleAsync();
        Assert.Equal(new DateTime(2025, 6, 15), recognition.RecognitionDate);
        Assert.Equal(500_000m, recognition.NetSaleValue);
    }

    [SqlServerFact]
    public async Task RecognitionMigration_RefusesToDeploy_WhenAnAccountNameBlocksItsSystemRole()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = new AppDbContext(Options(database.ConnectionString));
        await db.GetService<IMigrator>().MigrateAsync(BeforeRecognition);

        // The chart already carries this name. Retyped to Bank it can no longer hold the deposit
        // role — and it defeats both halves of the account step at once: the adopt-in-place UPDATE
        // matches nothing, and the create-instead INSERT is blocked by the very name it wanted.
        // Without the preflight the migration reports success with no deposit account at all.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [FinanceAccounts] SET [Type] = 2 WHERE [Name] = N'Customer General Account / Customer Deposits'");

        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.MigrateAsync());
        Assert.Contains("cannot take the Customer Deposits role", Flatten(error));
        Assert.Equal(0, await ScalarAsync<int>(database.ConnectionString,
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = 'BookingSaleRecognitions'"));

        // Put the type back and the deployment completes, with both roles established.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [FinanceAccounts] SET [Type] = 5 WHERE [Name] = N'Customer General Account / Customer Deposits'");
        await db.Database.MigrateAsync();
        Assert.Single(await db.FinanceAccounts.Where(a => a.SystemRole == FinanceSystemAccountRole.CustomerDeposits).ToListAsync());
        Assert.Single(await db.FinanceAccounts.Where(a => a.SystemRole == FinanceSystemAccountRole.CustomerReceivables).ToListAsync());
    }

    private static string Flatten(Exception error)
    {
        var text = new System.Text.StringBuilder();
        for (var current = error; current != null; current = current.InnerException)
            text.AppendLine(current.Message);
        return text.ToString();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default! : (T)value;
    }

    [SqlServerFact]
    public async Task LoanQueriesCorrectionsAndReports_RunOnTheRealSqlServerProvider()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var bank = new FinanceAccount
        {
            Name = "SQL Loan Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true
        };
        var liability = new FinanceAccount
        {
            Name = "SQL Loan Liability", AccountHolderName = "DAMS", Type = FinanceAccountType.Liability, IsActive = true
        };
        db.AddRange(bank, liability);
        await db.SaveChangesAsync();
        var accounts = new FinanceAccountService(db);
        var loans = new LoanService(db, accounts);
        var finance = new FinanceService(db, new NullPrivateStorage(), accounts,
            new WhtService(db, accounts), NullLogger<FinanceService>.Instance);
        var loan = await loans.CreateAsync(new SaveLoanDto
        {
            Name = "SQL HBL Term Loan", LenderName = "HBL", FinanceAccountId = liability.Id
        });
        await loans.RecordTransactionAsync(loan.Id, new SaveLoanTransactionDto
        {
            Type = LoanTransactionType.Drawdown, PrincipalAmount = 5_000m,
            Date = new DateTime(2026, 8, 1), FinanceAccountId = bank.Id
        }, 1);
        var repayment = await loans.RecordTransactionAsync(loan.Id, new SaveLoanTransactionDto
        {
            Type = LoanTransactionType.Repayment, PrincipalAmount = 500m, InterestAmount = 50m,
            Date = new DateTime(2026, 8, 10), FinanceAccountId = bank.Id
        }, 1);

        Assert.Equal(4_500m, Assert.Single(await loans.GetAllAsync(false)).CurrentBalance);
        Assert.Equal(2, (await loans.GetStatementAsync(loan.Id, 0, 100)).Items.Count);
        Assert.Equal(4_450m, (await accounts.GetByIdAsync(bank.Id)).CurrentBalance);
        Assert.Equal(4_500m, (await accounts.GetByIdAsync(liability.Id)).CurrentBalance);
        var pnl = await finance.GetProfitAndLossAsync(null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
        Assert.Equal(50m, pnl.TotalExpenses);
        Assert.True((await finance.GetBalanceSheetAsync(null, new DateTime(2026, 8, 31))).IsBalanced);

        await loans.UpdateTransactionAsync(loan.Id, repayment.Id, new SaveLoanTransactionDto
        {
            Type = LoanTransactionType.Repayment, PrincipalAmount = 400m, InterestAmount = 40m,
            Date = new DateTime(2026, 8, 10), FinanceAccountId = bank.Id,
            ConcurrencyToken = repayment.ConcurrencyToken
        }, 2);
        Assert.Equal(4_600m, Assert.Single(await loans.GetAllAsync(false)).CurrentBalance);
        Assert.Equal(40m, (await finance.GetProfitAndLossAsync(null,
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 31))).TotalExpenses);
    }

    // Booking cancellation depends on real SQL Server rowversion semantics (EF's InMemory provider
    // cannot reproduce a genuine OriginalValue mismatch at SaveChanges) and runs the newest
    // migration, so this is the one place that actually proves: (1) the settlement/refund tables,
    // indexes and check constraints migrate cleanly, and (2) a stale RowVersion is translated into
    // the clean business error every other rejection uses, never EF's raw
    // DbUpdateConcurrencyException.
    [SqlServerFact]
    public async Task CancellationSettlement_MigratesAndTranslatesRowVersionConflicts_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int bookingId;
        string staleToken;
        await using (var db = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "SQL cancellation", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project, UnitNumber = "SQL-CANCEL-01", UnitType = "Apartment",
                Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan
            };
            var customer = new Customer { FullName = "SQL Cancel Customer", Phone = "03008888888", Status = CustomerStatus.Active };
            var booking = new Booking
            {
                BookingReference = $"SQL-CXL-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = 1_000_000m,
                BookingAmountRequired = 500_000m, BookingAmountReceived = 500_000m, BookingDate = DateTime.UtcNow
            };
            booking.Payments.Add(new Payment
            {
                Booking = booking, Amount = 500_000m, Type = PaymentType.BookingAmount, PaymentMethod = PaymentMethod.Cash
            });
            db.AddRange(project, unit, customer, booking);
            await db.SaveChangesAsync();
            bookingId = booking.Id;
            staleToken = Convert.ToBase64String(booking.RowVersion);
        }

        // Another admin edits the booking (not its payments) after the cancellation dialog captured
        // its RowVersion. This must be caught by the RowVersion check specifically — the customer
        // cash snapshot is unchanged, so the separate stale-cash check must not be what fires here.
        await using (var db = new AppDbContext(options))
        {
            var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId);
            booking.InternalNotes = "Edited by another admin";
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var service = new BookingService(db, new CustomerService(db), new FinanceAccountService(db));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelBookingAsync(bookingId, new CancelBookingDto
            {
                Reason = "SQL invariant cancellation", ExpectedCustomerCashReceived = 500_000m,
                RefundAmount = 0m, RefundDecision = CancellationRefundDecision.None,
                IdempotencyKey = "sql-cancel-stale", ConcurrencyToken = staleToken
            }, Actor));
            Assert.Contains("Refresh and review", error.Message);
        }

        await using (var verify = new AppDbContext(options))
        {
            Assert.False(await verify.BookingCancellationSettlements.AnyAsync(s => s.BookingId == bookingId));
            Assert.Equal(BookingStatus.PaymentPlanActive, (await verify.Bookings.SingleAsync(b => b.Id == bookingId)).Status);
        }
    }

    /// <summary>
    /// Two admins editing the same expense, and the same for a manual revenue row.
    /// <para>
    /// This has to run on the real provider: <c>rowversion</c> is generated by SQL Server, and the
    /// in-memory store leaves the column empty — so in memory there is no version to go stale and the
    /// test would pass without proving anything. What it defends is a silent lost update: an expense
    /// edit moves the gross cost, the withheld tax and the paying account's balance together, so the
    /// figure that survived used to be whichever save landed second, with no error and no trace.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task TwoAdminsEditingTheSameExpenseOrRevenueRow_LoseTheStaleOne_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int expenseId, revenueId, accountId, headId;
        string staleExpenseToken, staleRevenueToken;
        await using (var db = new AppDbContext(options))
        {
            var account = new FinanceAccount
            {
                Name = "SQL Concurrency Bank", AccountHolderName = "DAMS",
                Type = FinanceAccountType.Bank, OpeningBalance = 1_000_000m, IsActive = true
            };
            db.Add(account);
            await db.SaveChangesAsync();
            accountId = account.Id;
            // A seeded head with no withholding, so the amounts under test are the gross figures.
            headId = await db.ExpenseCategories.Where(c => c.Code == "miscellaneous").Select(c => c.Id).SingleAsync();

            var finance = Finance(db);
            var expense = await finance.CreateExpenseAsync(new CreateExpenseDto
            {
                FinanceAccountId = accountId, Amount = 100_000m, CategoryId = headId,
                Description = "Original", Date = new DateTime(2026, 8, 1)
            }, adminUserId: 1);
            var revenue = await finance.CreateManualRevenueAsync(new CreateManualRevenueDto
            {
                FinanceAccountId = accountId, Amount = 40_000m, RevenueType = "Transfer charges",
                Date = new DateTime(2026, 8, 1)
            }, adminUserId: 1);

            expenseId = expense.Id;
            revenueId = revenue.Id;
            // A real token, not an empty one — the create response has to hand one back or the client
            // has nothing to send and the protection is unreachable.
            staleExpenseToken = expense.ConcurrencyToken;
            staleRevenueToken = revenue.ConcurrencyToken;
            Assert.NotEmpty(staleExpenseToken);
            Assert.NotEmpty(staleRevenueToken);
        }

        // Admin A saves first, moving both figures.
        await using (var db = new AppDbContext(options))
        {
            var finance = Finance(db);
            var expense = await db.Expenses.AsNoTracking().SingleAsync(e => e.Id == expenseId);
            await finance.UpdateExpenseAsync(expenseId, new UpdateExpenseDto
            {
                FinanceAccountId = accountId, Amount = 250_000m, CategoryId = headId,
                Description = "Corrected by admin A", Date = new DateTime(2026, 8, 1),
                ConcurrencyToken = Convert.ToBase64String(expense.RowVersion)
            });
            var revenue = await db.ManualRevenues.AsNoTracking().SingleAsync(r => r.Id == revenueId);
            await finance.UpdateManualRevenueAsync(revenueId, new UpdateManualRevenueDto
            {
                FinanceAccountId = accountId, Amount = 90_000m, RevenueType = "Transfer charges",
                Date = new DateTime(2026, 8, 1), ConcurrencyToken = Convert.ToBase64String(revenue.RowVersion)
            });
        }

        // Admin B, still holding the version from before A's save, is refused on both.
        await using (var db = new AppDbContext(options))
        {
            var finance = Finance(db);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => finance.UpdateExpenseAsync(
                expenseId, new UpdateExpenseDto
                {
                    FinanceAccountId = accountId, Amount = 500_000m, CategoryId = headId,
                    Description = "Overwritten by admin B", Date = new DateTime(2026, 8, 1),
                    ConcurrencyToken = staleExpenseToken
                }));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => finance.UpdateManualRevenueAsync(
                revenueId, new UpdateManualRevenueDto
                {
                    FinanceAccountId = accountId, Amount = 1m, RevenueType = "Transfer charges",
                    Date = new DateTime(2026, 8, 1), ConcurrencyToken = staleRevenueToken
                }));
            // Deleting from a stale copy is refused for the same reason — it is as destructive as
            // editing, and it races the same way.
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => finance.DeleteExpenseAsync(expenseId, staleExpenseToken));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => finance.DeleteManualRevenueAsync(revenueId, staleRevenueToken));
        }

        // Admin A's figures survived intact, and the account balance reflects exactly them.
        await using (var verify = new AppDbContext(options))
        {
            Assert.Equal(250_000m, (await verify.Expenses.SingleAsync(e => e.Id == expenseId)).Amount);
            Assert.Equal("Corrected by admin A", (await verify.Expenses.SingleAsync(e => e.Id == expenseId)).Description);
            Assert.Equal(90_000m, (await verify.ManualRevenues.SingleAsync(r => r.Id == revenueId)).Amount);
            // 1,000,000 opening + 90,000 revenue − 250,000 expense.
            Assert.Equal(840_000m, (await new FinanceAccountService(verify).GetByIdAsync(accountId)).CurrentBalance);
        }
    }

    private static FinanceService Finance(AppDbContext db)
    {
        var accounts = new FinanceAccountService(db);
        return new FinanceService(db, new NullPrivateStorage(), accounts,
            new WhtService(db, accounts), NullLogger<FinanceService>.Instance);
    }

    private static BookingCommission Commission(int bookingId, int partnerId, BookingCommissionStatus status,
        decimal amount) => new()
    {
        BookingId = bookingId, PartnerId = partnerId, PartnerNameSnapshot = "SQL Broker",
        PartnerTypeSnapshot = "Broker", PartnerInternalCodeSnapshot = "SQL-BROKER",
        AllocationPercentSnapshot = 100m, IsManual = true, ManualReason = "SQL invariant seed",
        CalculationType = FinancialCalculationType.FixedAmount, FixedAmount = amount,
        CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount, BasisAmount = 1_000_000m,
        CalculatedAmount = amount, FinalAmount = amount, ApprovedAmount = amount,
        EarningCondition = CommissionEarningCondition.ManualMilestone, Status = status,
        CreatedAt = DateTime.UtcNow, CreatedByName = "SQL seed"
    };

    private static CustomerRebate Rebate(int bookingId, int customerId, CustomerRebateStatus status,
        decimal amount) => new()
    {
        BookingId = bookingId, CustomerId = customerId, CalculationType = FinancialCalculationType.FixedAmount,
        FixedAmount = amount, CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
        BasisAmount = 1_000_000m, CalculatedAmount = amount, FinalAmount = amount, ApprovedAmount = amount,
        Reason = "SQL invariant seed", Method = CustomerRebateMethod.OutstandingBalanceReduction,
        Status = status, CreatedAt = DateTime.UtcNow, CreatedByName = "SQL seed"
    };

    /// <summary>
    /// The external-integration schema on real SQL Server. The in-memory provider enforces
    /// neither nullability nor unique indexes, so the two guarantees this feature actually
    /// leans on have to be proved here.
    /// </summary>
    [SqlServerFact]
    public async Task ExternalIntegrations_MigrateAndEnforceTheirInvariants_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        // A lead with no phone number must be storable, which is the whole point of the
        // MakeLeadPhoneOptional migration.
        await using (var db = new AppDbContext(options))
        {
            var source = await db.LeadSources.FirstAsync(s => s.Code == "meta");
            db.Leads.Add(new Lead
            {
                LeadReference = $"LD-SQL-{Guid.NewGuid():N}"[..20],
                FirstName = "Phoneless",
                LeadSourceId = source.Id,
                Phone = null,
                NormalizedPhone = null,
                ExternalProvider = "meta",
                ExternalLeadId = $"sql-{Guid.NewGuid():N}"
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Leads') AND name = 'Phone' AND is_nullable = 1"));
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Leads') AND name = 'NormalizedPhone' AND is_nullable = 1"));

        // The webhook writes one event per delivery and relies on the database — not on the
        // application's pre-check — to make a provider retry harmless under concurrency.
        await using (var db = new AppDbContext(options))
        {
            db.ExternalIntegrationEvents.Add(new ExternalIntegrationEvent
            {
                Provider = "meta", EventType = "leadgen", EventKey = "1:page-1:lead-1",
                RawPayloadJson = "{}", AvailableAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            db.ExternalIntegrationEvents.Add(new ExternalIntegrationEvent
            {
                Provider = "meta", EventType = "leadgen", EventKey = "1:page-1:lead-1",
                RawPayloadJson = "{}", AvailableAt = DateTime.UtcNow
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Two connections may not claim the same provider account, or a webhook could not
        // resolve unambiguously which one owns the page.
        await using (var db = new AppDbContext(options))
        {
            db.ExternalIntegrationConnections.Add(new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "acct-1", DisplayName = "First"
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            db.ExternalIntegrationConnections.Add(new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "acct-1", DisplayName = "Duplicate"
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    /// <summary>
    /// A production database that already has a custom LeadSource occupying the identity value
    /// the "meta" seed used to hardcode (14) must still be able to apply
    /// AddExternalIntegrations. A fixed Id = 14 InsertData would fail this with a primary key
    /// violation; the migration inserts by Code and lets the identity column pick its own value.
    /// </summary>
    [SqlServerFact]
    public async Task AddExternalIntegrationsMigration_SucceedsEvenWhenACustomLeadSourceAlreadyOccupiesId14()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);

        await using (var db = new AppDbContext(options))
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260816044115_AddBookingCancellationSettlement");
        }

        // The 13 system sources already exist at this point, so the next identity value an
        // admin-created custom source receives is exactly the one the old hardcoded seed
        // collided with.
        await using (var db = new AppDbContext(options))
        {
            db.LeadSources.Add(new LeadSource
            {
                Code = "roadshow",
                Name = "Road Show",
                DisplayOrder = 99,
                IsActive = true,
                IsSystem = false,
                CustomerSource = CustomerSource.Other,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var customSourceId = await ScalarAsync(database.ConnectionString,
            "SELECT [Id] FROM [LeadSources] WHERE [Code] = N'roadshow'");
        Assert.Equal(14, customSourceId);

        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [LeadSources] WHERE [Code] = N'meta'"));
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [LeadSources] WHERE [Code] = N'roadshow' AND [Name] = N'Road Show'"));
    }

    /// <summary>
    /// Two requests can both pass the in-application duplicate pre-check before either commits.
    /// The unique index — not the pre-check — is what makes the collision harmless, and it must
    /// not take an unrelated new event down with it, which is only provable against a provider
    /// that actually enforces the index.
    /// </summary>
    [SqlServerFact]
    public async Task WebhookIntake_SurvivesAConcurrentDuplicate_WithoutLosingTheOtherEventInTheSameDelivery()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        // Simulates a race: another request already committed this exact event before the
        // in-process pre-check in RecordOneAsync could see it.
        int connectionId;
        await using (var db = new AppDbContext(options))
        {
            var connection = new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "race-account", DisplayName = "Race Account"
            };
            db.ExternalIntegrationConnections.Add(connection);
            await db.SaveChangesAsync();
            connectionId = connection.Id;

            db.ExternalIntegrationResources.Add(new ExternalIntegrationResource
            {
                ExternalIntegrationConnectionId = connectionId,
                Provider = "meta", ResourceType = "facebook_page", ExternalId = "page-race",
                IsEnabled = true, IsActive = true
            });
            db.ExternalIntegrationEvents.Add(new ExternalIntegrationEvent
            {
                Provider = "meta", EventType = "leadgen", EventKey = $"{connectionId}:page-race:lead-race",
                RawPayloadJson = "{}", AvailableAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var intake = new DAMS.Application.Services.Integrations.MetaWebhookIntakeService(
                db, NullLogger<DAMS.Application.Services.Integrations.MetaWebhookIntakeService>.Instance);

            var body = """
                {
                  "object": "page",
                  "entry": [{
                    "id": "page-race",
                    "changes": [
                      {"field": "leadgen", "value": {"page_id": "page-race", "leadgen_id": "lead-race", "created_time": 1}},
                      {"field": "leadgen", "value": {"page_id": "page-race", "leadgen_id": "lead-unique", "created_time": 1}}
                    ]
                  }]
                }
                """;

            var recorded = await intake.RecordAsync(body);

            // Only the genuinely new event; the colliding one was already there.
            Assert.Equal(1, recorded);
        }

        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [ExternalIntegrationEvents] WHERE [EventKey] LIKE '%lead-unique%'"));
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [ExternalIntegrationEvents] WHERE [EventKey] LIKE '%lead-race%'"));
    }

    /// <summary>
    /// MetaIntegrationService.SetResourceEnabledAsync's own "is this Page already enabled
    /// elsewhere" check is an AnyAsync query: two requests enabling the same physical Page
    /// through two different connections can both pass it before either has committed. Only a
    /// database-level constraint can actually make the collision impossible, which the
    /// InMemory provider used by the rest of the suite does not enforce — this needs real SQL
    /// Server.
    /// </summary>
    [SqlServerFact]
    public async Task EnablingTheSamePhysicalPageThroughTwoConnections_IsRejectedByTheDatabaseEvenWhenTheAppLevelCheckIsRaced()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var protector = new DAMS.Application.Tests.Integrations.PlaintextSecretProtector();
        var graph = new DAMS.Application.Tests.Integrations.FakeMetaGraphClient();
        var metaOptions = new MetaIntegrationOptions
        {
            AppId = "app", AppSecret = "secret", WebhookVerifyToken = "verify",
            OAuthCallbackUrl = "https://dams.test/callback"
        };

        int firstConnectionId, secondConnectionId, firstResourceId, secondResourceId;
        await using (var db = new AppDbContext(options))
        {
            var first = new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "race-user-a", DisplayName = "A",
                Status = ExternalIntegrationConnectionStatus.Connected,
                AccessTokenProtected = protector.Protect("token-a")
            };
            var second = new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "race-user-b", DisplayName = "B",
                Status = ExternalIntegrationConnectionStatus.Connected,
                AccessTokenProtected = protector.Protect("token-b")
            };
            db.ExternalIntegrationConnections.AddRange(first, second);
            await db.SaveChangesAsync();
            firstConnectionId = first.Id;
            secondConnectionId = second.Id;

            var firstResource = new ExternalIntegrationResource
            {
                ExternalIntegrationConnectionId = first.Id, Provider = "meta", ResourceType = "facebook_page",
                ExternalId = "shared-page-race", IsEnabled = false, IsActive = true
            };
            var secondResource = new ExternalIntegrationResource
            {
                ExternalIntegrationConnectionId = second.Id, Provider = "meta", ResourceType = "facebook_page",
                ExternalId = "shared-page-race", IsEnabled = false, IsActive = true
            };
            db.ExternalIntegrationResources.AddRange(firstResource, secondResource);
            await db.SaveChangesAsync();
            firstResourceId = firstResource.Id;
            secondResourceId = secondResource.Id;
        }

        // The first connection enables the shared Page through the real service — succeeds.
        await using (var db = new AppDbContext(options))
        {
            var sync = new DAMS.Application.Services.Integrations.MetaResourceSyncService(
                db, graph, protector, metaOptions, NullLogger<DAMS.Application.Services.Integrations.MetaResourceSyncService>.Instance);
            var integration = new DAMS.Application.Services.Integrations.MetaIntegrationService(
                db, graph, protector, sync, metaOptions, NullLogger<DAMS.Application.Services.Integrations.MetaIntegrationService>.Instance);

            await integration.SetResourceEnabledAsync(firstConnectionId, firstResourceId, isEnabled: true);
        }

        // A second, independent context now tries the same thing for the other connection. Its
        // own AnyAsync check runs against a database where the first save already committed, so
        // in this ordering it would actually catch the clash on its own — the point of this test
        // is that even if it did NOT (a true race), the database itself refuses the second row.
        await using (var db = new AppDbContext(options))
        {
            var sync = new DAMS.Application.Services.Integrations.MetaResourceSyncService(
                db, graph, protector, metaOptions, NullLogger<DAMS.Application.Services.Integrations.MetaResourceSyncService>.Instance);
            var integration = new DAMS.Application.Services.Integrations.MetaIntegrationService(
                db, graph, protector, sync, metaOptions, NullLogger<DAMS.Application.Services.Integrations.MetaIntegrationService>.Instance);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => integration.SetResourceEnabledAsync(secondConnectionId, secondResourceId, isEnabled: true));
            Assert.Contains("already enabled through another", ex.Message);
        }

        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [ExternalIntegrationResources] " +
            "WHERE [ExternalId] = 'shared-page-race' AND [IsEnabled] = 1"));

        // Bypassing the service's own AnyAsync check entirely — two raw contexts, each loading
        // a different, never-yet-enabled resource for the same physical Page and setting
        // IsEnabled in memory before either has saved — is the strongest version of this proof:
        // nothing but the database itself is left to decide the outcome.
        int rawA, rawB;
        await using (var db = new AppDbContext(options))
        {
            var connA = new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "raw-race-a", DisplayName = "Raw A",
                Status = ExternalIntegrationConnectionStatus.Connected
            };
            var connB = new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "raw-race-b", DisplayName = "Raw B",
                Status = ExternalIntegrationConnectionStatus.Connected
            };
            db.ExternalIntegrationConnections.AddRange(connA, connB);
            await db.SaveChangesAsync();

            var resourceA = new ExternalIntegrationResource
            {
                ExternalIntegrationConnectionId = connA.Id, Provider = "meta", ResourceType = "facebook_page",
                ExternalId = "shared-page-raw-race", IsEnabled = false, IsActive = true
            };
            var resourceB = new ExternalIntegrationResource
            {
                ExternalIntegrationConnectionId = connB.Id, Provider = "meta", ResourceType = "facebook_page",
                ExternalId = "shared-page-raw-race", IsEnabled = false, IsActive = true
            };
            db.ExternalIntegrationResources.AddRange(resourceA, resourceB);
            await db.SaveChangesAsync();
            rawA = resourceA.Id;
            rawB = resourceB.Id;
        }

        await using var winner = new AppDbContext(options);
        await using var loser = new AppDbContext(options);

        var winnerResource = await winner.ExternalIntegrationResources.SingleAsync(r => r.Id == rawA);
        var loserResource = await loser.ExternalIntegrationResources.SingleAsync(r => r.Id == rawB);
        winnerResource.IsEnabled = true;
        loserResource.IsEnabled = true;

        await winner.SaveChangesAsync();

        var raceEx = await Assert.ThrowsAsync<DbUpdateException>(() => loser.SaveChangesAsync());
        var sqlEx = Assert.IsType<SqlException>(raceEx.InnerException);
        Assert.Contains("UX_ExternalIntegrationResources_EnabledFacebookPage", sqlEx.Message);
    }

    /// <summary>
    /// The test above proves the database rejects the losing row, but neither it nor the one
    /// before it ever drives the losing call through SetResourceEnabledAsync itself, so neither
    /// exercises what that method actually does with a real Meta subscribe call in flight when
    /// the race is lost: both connections pass their own "already enabled elsewhere" check
    /// before either commits, both call Meta Subscribe (a physical Page's webhook subscription
    /// is app-to-Page, not connection-to-Page), the database then picks a winner, and the loser's
    /// catch block used to "compensate" by calling Unsubscribe — tearing down the winner's real,
    /// just-established subscription, not its own. This proves that regression stays fixed: the
    /// winner's subscription must still be active after the loser's save collides and fails.
    /// </summary>
    [SqlServerFact]
    public async Task LosingTheEnablePageRace_NeverUnsubscribesTheWinnersRealSubscription()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var protector = new DAMS.Application.Tests.Integrations.PlaintextSecretProtector();
        var graph = new DAMS.Application.Tests.Integrations.FakeMetaGraphClient();
        var metaOptions = new MetaIntegrationOptions
        {
            AppId = "app", AppSecret = "secret", WebhookVerifyToken = "verify",
            OAuthCallbackUrl = "https://dams.test/callback"
        };
        const string pageId = "shared-page-subscribe-race";

        int winnerConnectionId, loserConnectionId, winnerResourceId, loserResourceId;
        await using (var db = new AppDbContext(options))
        {
            var winnerConn = new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "subscribe-race-winner", DisplayName = "Winner",
                Status = ExternalIntegrationConnectionStatus.Connected, AccessTokenProtected = protector.Protect("token-winner")
            };
            var loserConn = new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "subscribe-race-loser", DisplayName = "Loser",
                Status = ExternalIntegrationConnectionStatus.Connected, AccessTokenProtected = protector.Protect("token-loser")
            };
            db.ExternalIntegrationConnections.AddRange(winnerConn, loserConn);
            await db.SaveChangesAsync();
            winnerConnectionId = winnerConn.Id;
            loserConnectionId = loserConn.Id;

            var winnerResource = new ExternalIntegrationResource
            {
                ExternalIntegrationConnectionId = winnerConn.Id, Provider = "meta", ResourceType = ExternalResourceTypes.FacebookPage,
                ExternalId = pageId, IsEnabled = false, IsActive = true
            };
            var loserResource = new ExternalIntegrationResource
            {
                ExternalIntegrationConnectionId = loserConn.Id, Provider = "meta", ResourceType = ExternalResourceTypes.FacebookPage,
                ExternalId = pageId, IsEnabled = false, IsActive = true
            };
            db.ExternalIntegrationResources.AddRange(winnerResource, loserResource);
            await db.SaveChangesAsync();
            winnerResourceId = winnerResource.Id;
            loserResourceId = loserResource.Id;
        }

        // The interceptor plays "the winner" at the exact moment the loser's own SaveChangesAsync
        // is about to run — i.e. after the loser has already passed its own AnyAsync ownership
        // check and already called Meta Subscribe for its own attempt, matching the real race
        // window instead of a convenient ordering.
        var interceptor = new RunFullEnableThroughAnotherConnectionInterceptor(
            database.ConnectionString, graph, protector, metaOptions, winnerConnectionId, winnerResourceId, loserResourceId);

        await using var loserDb = new AppDbContext(Options(database.ConnectionString, interceptor));
        var loserSync = new DAMS.Application.Services.Integrations.MetaResourceSyncService(
            loserDb, graph, protector, metaOptions, NullLogger<DAMS.Application.Services.Integrations.MetaResourceSyncService>.Instance);
        var loserIntegration = new DAMS.Application.Services.Integrations.MetaIntegrationService(
            loserDb, graph, protector, loserSync, metaOptions, NullLogger<DAMS.Application.Services.Integrations.MetaIntegrationService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loserIntegration.SetResourceEnabledAsync(loserConnectionId, loserResourceId, isEnabled: true));
        Assert.Contains("already enabled through another", ex.Message);

        // The point of the whole test: the loser's compensation must not have touched the
        // winner's real subscription.
        Assert.True(graph.IsCurrentlySubscribed(pageId));

        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            $"SELECT COUNT(*) FROM [ExternalIntegrationResources] WHERE [ExternalId] = '{pageId}' AND [IsEnabled] = 1"));
        Assert.Equal(0, await ScalarAsync(database.ConnectionString,
            $"SELECT COUNT(*) FROM [ExternalIntegrationResources] WHERE [Id] = {loserResourceId} AND [IsEnabled] = 1"));
    }

    /// <summary>
    /// Two admins toggling the same Page row at the same moment — two browser tabs, or a click
    /// repeated on a slow connection. Both load the same rowversion, both reach Meta, and the
    /// database rejects the second save with a DbUpdateConcurrencyException, which is itself a
    /// DbUpdateException and so lands in the same catch that used to "compensate" by issuing the
    /// opposite Graph call. That undid the state the winner had just committed rather than the
    /// loser's own: DAMS left claiming a subscription Meta no longer had on a concurrent enable
    /// (a silent lead-delivery outage), and the exact mirror on a concurrent disable. Neither
    /// direction may finish with Meta out of step with the row that actually committed.
    ///
    /// Only a real SQL Server can produce this at all — the InMemory provider the rest of the
    /// suite uses does not maintain rowversions, so the conflict never arises there.
    /// </summary>
    [SqlServerFact]
    public async Task TwoAdminsTogglingTheSamePageRowAtOnce_LeaveMetaMatchingTheRowThatCommitted()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var protector = new DAMS.Application.Tests.Integrations.PlaintextSecretProtector();
        var graph = new DAMS.Application.Tests.Integrations.FakeMetaGraphClient();
        var metaOptions = new MetaIntegrationOptions
        {
            AppId = "app", AppSecret = "secret", WebhookVerifyToken = "verify",
            OAuthCallbackUrl = "https://dams.test/callback"
        };

        int connectionId, enableRaceId, disableRaceId;
        await using (var db = new AppDbContext(options))
        {
            var connection = new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "same-row-race", DisplayName = "Same row",
                Status = ExternalIntegrationConnectionStatus.Connected,
                AccessTokenProtected = protector.Protect("same-row-token")
            };
            db.ExternalIntegrationConnections.Add(connection);
            await db.SaveChangesAsync();
            connectionId = connection.Id;

            var enabling = new ExternalIntegrationResource
            {
                ExternalIntegrationConnectionId = connection.Id, Provider = "meta",
                ResourceType = ExternalResourceTypes.FacebookPage, ExternalId = "same-row-enable",
                IsEnabled = false, IsActive = true, IsSubscribed = false
            };
            var disabling = new ExternalIntegrationResource
            {
                ExternalIntegrationConnectionId = connection.Id, Provider = "meta",
                ResourceType = ExternalResourceTypes.FacebookPage, ExternalId = "same-row-disable",
                IsEnabled = true, IsActive = true, IsSubscribed = true
            };
            db.ExternalIntegrationResources.AddRange(enabling, disabling);
            await db.SaveChangesAsync();
            enableRaceId = enabling.Id;
            disableRaceId = disabling.Id;
        }

        // ── Both admins enable the same row ──────────────────────────────────────────
        await using (var loserDb = new AppDbContext(Options(database.ConnectionString,
            new RunFullToggleThroughASecondContextInterceptor(
                database.ConnectionString, graph, protector, metaOptions, connectionId, enableRaceId, toggleTo: true))))
        {
            var loserSync = new DAMS.Application.Services.Integrations.MetaResourceSyncService(
                loserDb, graph, protector, metaOptions,
                NullLogger<DAMS.Application.Services.Integrations.MetaResourceSyncService>.Instance);
            var loserIntegration = new DAMS.Application.Services.Integrations.MetaIntegrationService(
                loserDb, graph, protector, loserSync, metaOptions,
                NullLogger<DAMS.Application.Services.Integrations.MetaIntegrationService>.Instance);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => loserIntegration.SetResourceEnabledAsync(connectionId, enableRaceId, isEnabled: true));
        }

        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            $"SELECT COUNT(*) FROM [ExternalIntegrationResources] WHERE [Id] = {enableRaceId} AND [IsEnabled] = 1"));
        Assert.True(graph.IsCurrentlySubscribed("same-row-enable"));

        // ── Both admins disable the same row ─────────────────────────────────────────
        await using (var loserDb = new AppDbContext(Options(database.ConnectionString,
            new RunFullToggleThroughASecondContextInterceptor(
                database.ConnectionString, graph, protector, metaOptions, connectionId, disableRaceId, toggleTo: false))))
        {
            var loserSync = new DAMS.Application.Services.Integrations.MetaResourceSyncService(
                loserDb, graph, protector, metaOptions,
                NullLogger<DAMS.Application.Services.Integrations.MetaResourceSyncService>.Instance);
            var loserIntegration = new DAMS.Application.Services.Integrations.MetaIntegrationService(
                loserDb, graph, protector, loserSync, metaOptions,
                NullLogger<DAMS.Application.Services.Integrations.MetaIntegrationService>.Instance);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => loserIntegration.SetResourceEnabledAsync(connectionId, disableRaceId, isEnabled: false));
        }

        Assert.Equal(0, await ScalarAsync(database.ConnectionString,
            $"SELECT COUNT(*) FROM [ExternalIntegrationResources] WHERE [Id] = {disableRaceId} AND [IsEnabled] = 1"));
        Assert.False(graph.IsCurrentlySubscribed("same-row-disable"));
    }

    /// <summary>
    /// MetaIntegrationService.UpsertConnectionAsync reads "does a connection for this account
    /// already exist" and, if not, inserts one — a window a second browser completing OAuth for
    /// the same Meta account at nearly the same moment can land in before either commits. Only
    /// the database's own unique index on (Provider, ExternalAccountId) can actually decide that,
    /// which the InMemory provider used by the rest of the suite does not enforce; this proves
    /// the service recovers from that collision by reloading and updating the winner's row
    /// instead of surfacing a raw DbUpdateException to an admin's browser.
    /// </summary>
    [SqlServerFact]
    public async Task ReconnectingTheSameMetaAccountFromTwoBrowsersAtOnce_RecoversFromTheRaceInsteadOfFailing()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var baseOptions = Options(database.ConnectionString);
        int adminUserId;
        await using (var migrator = new AppDbContext(baseOptions))
        {
            await migrator.Database.MigrateAsync();

            // ExternalIntegrationOAuthState.CreatedByUserId has a real FK to Users, unlike the
            // unenforced CreatedById audit fields elsewhere in this file — a genuine row is
            // needed, not just an arbitrary id.
            var user = new User { FullName = "SQL Race Admin", Email = "sql-race-admin@dams.test", Password = "hash", RoleId = 1 };
            migrator.Users.Add(user);
            await migrator.SaveChangesAsync();
            adminUserId = user.UserId;
        }

        var protector = new DAMS.Application.Tests.Integrations.PlaintextSecretProtector();
        var graph = new DAMS.Application.Tests.Integrations.FakeMetaGraphClient
        {
            Authorization = new DAMS.Application.Services.Integrations.MetaAuthorizationResult
            {
                AccessToken = "race-token", UserId = "race-account", DisplayName = "Race Co",
                GrantedScopes = [.. MetaScopes.All]
            }
        };
        var metaOptions = new MetaIntegrationOptions
        {
            AppId = "app", AppSecret = "secret", WebhookVerifyToken = "verify",
            OAuthCallbackUrl = "https://dams.test/callback"
        };
        var admin = new LeadUserContext { UserId = adminUserId, Role = "Admin", DisplayName = "Admin" };

        var interceptor = new InsertCollidingConnectionInterceptor(database.ConnectionString, "race-account");
        await using var db = new AppDbContext(Options(database.ConnectionString, interceptor));
        var sync = new DAMS.Application.Services.Integrations.MetaResourceSyncService(
            db, graph, protector, metaOptions, NullLogger<DAMS.Application.Services.Integrations.MetaResourceSyncService>.Instance);
        var integration = new DAMS.Application.Services.Integrations.MetaIntegrationService(
            db, graph, protector, sync, metaOptions, NullLogger<DAMS.Application.Services.Integrations.MetaIntegrationService>.Instance);

        var start = await integration.StartConnectAsync(admin, null);
        var state = System.Web.HttpUtility.ParseQueryString(new Uri(start.AuthorizationUrl).Query)["state"]!;

        // At the moment this context's own UpsertConnectionAsync reads for an existing row, none
        // exists — the interceptor lands the "other browser's" insert only once this context's
        // own SaveChanges for its new row actually fires, which reproduces the race
        // deterministically instead of hoping two real threads interleave the right way.
        var redirect = await integration.CompleteCallbackAsync("code-1", state, null);

        Assert.Contains("meta=connected", redirect);
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [ExternalIntegrationConnections] WHERE [ExternalAccountId] = 'race-account'"));
        // Not silently dropped: the surviving row carries this callback's own authorization
        // result, proving it updated the winner's row rather than losing its own data.
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [ExternalIntegrationConnections] " +
            "WHERE [ExternalAccountId] = 'race-account' AND [DisplayName] = N'Race Co'"));
    }

    /// <summary>
    /// The retry half of the idempotency guard, which only a real database can prove.
    /// <para>
    /// The whole mechanism rests on <c>IX_IdempotentRequests_Key</c> being UNIQUE: the filter inserts
    /// its reservation and lets the index decide whether the key was already taken, deliberately
    /// rather than reading first and writing after — a read-then-write loses the race it exists to
    /// win. The EF in-memory provider does not enforce unique indexes, so under it the second insert
    /// simply succeeds and the action runs a second time. Every in-memory assertion about a replay
    /// would pass while production duplicated the payment, which is exactly the wrong way round.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task RepeatingAMoneyRequestKey_ReplaysTheStoredResponse_RatherThanRecordingItAgain()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var migrator = new AppDbContext(options))
            await migrator.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(database.ConnectionString));
        await using var provider = services.BuildServiceProvider();
        var filter = new IdempotentMoneyOperationFilter(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IdempotentMoneyOperationFilter>.Instance);

        const string key = "sql-replay-key";
        var runs = 0;

        // First attempt: reserves the key, runs the action, stores what it returned.
        var first = FilterContexts(provider, key, new { amount = 250_000m });
        await filter.OnActionExecutionAsync(first.Executing, () =>
        {
            runs++;
            return Task.FromResult(Executed(first.ActionContext, new OkObjectResult(new { id = 7, amount = 250_000m })));
        });
        Assert.Equal(1, runs);
        Assert.Null(first.Executing.Result);

        // The retry after a lost response: same key, same arguments. The action must not run again,
        // and the caller must get the record the first attempt created — not an empty body.
        var retry = FilterContexts(provider, key, new { amount = 250_000m });
        await filter.OnActionExecutionAsync(retry.Executing, () =>
        {
            runs++;
            return Task.FromResult(Executed(retry.ActionContext, new OkObjectResult(new { id = 8, amount = 250_000m })));
        });
        Assert.Equal(1, runs);
        var replayed = Assert.IsType<ContentResult>(retry.Executing.Result);
        Assert.Equal(200, replayed.StatusCode);
        Assert.Equal("{\"id\":7,\"amount\":250000}", replayed.Content);

        // Same key, different amount: that is a different intent wearing a used key, and answering it
        // from the earlier record would hide a real second payment. It is refused, not replayed.
        var changed = FilterContexts(provider, key, new { amount = 999_000m });
        await filter.OnActionExecutionAsync(changed.Executing, () =>
        {
            runs++;
            return Task.FromResult(Executed(changed.ActionContext, new OkObjectResult(new { id = 9 })));
        });
        Assert.Equal(1, runs);
        var conflict = Assert.IsType<ConflictObjectResult>(changed.Executing.Result);
        Assert.Equal(409, conflict.StatusCode);

        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            $"SELECT COUNT(*) FROM [IdempotentRequests] WHERE [Key] = '{key}'"));
    }

    /// <summary>
    /// The attachment is part of the submission, so it is part of the key's fingerprint.
    /// <para>
    /// It used to be skipped entirely — uploads were excluded from the hash. That made the file
    /// invisible to the guard: an operator whose save lost its response on the way back, who then
    /// noticed the wrong invoice was attached, swapped the file and pressed Save again, was answered
    /// 200 replayed from the FIRST attempt. The screen reported success, and the record still carried
    /// the wrong document, with nothing anywhere recording that a different file had been submitted.
    /// </para>
    /// <para>
    /// A changed file is now a changed submission (409, reload and enter it again), while a genuine
    /// retry of the same bytes still replays — including under a different file NAME, because the
    /// identity is the content, not what the browser called it.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task RepeatingAMoneyRequestKey_WithADifferentAttachment_IsRefused_NotReplayed()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var migrator = new AppDbContext(Options(database.ConnectionString)))
            await migrator.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(database.ConnectionString));
        await using var provider = services.BuildServiceProvider();
        var filter = new IdempotentMoneyOperationFilter(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IdempotentMoneyOperationFilter>.Instance);

        const string key = "sql-attachment-key";
        var runs = 0;
        var body = new { amount = 250_000m };

        async Task<IActionResult?> AttemptAsync(IFormFile file)
        {
            var attempt = FilterContexts(provider, key, body, file);
            await filter.OnActionExecutionAsync(attempt.Executing, () =>
            {
                runs++;
                return Task.FromResult(Executed(attempt.ActionContext,
                    new OkObjectResult(new { id = 11, amount = 250_000m })));
            });
            return attempt.Executing.Result;
        }

        // First save, with invoice A attached.
        Assert.Null(await AttemptAsync(Upload("invoice-a.pdf", "%PDF-1.4 INVOICE A")));
        Assert.Equal(1, runs);

        // The genuine retry: same key, same amount, same file. Replayed, as it always was.
        var replayed = Assert.IsType<ContentResult>(
            await AttemptAsync(Upload("invoice-a.pdf", "%PDF-1.4 INVOICE A")));
        Assert.Equal(200, replayed.StatusCode);
        Assert.Equal("{\"id\":11,\"amount\":250000}", replayed.Content);
        Assert.Equal(1, runs);

        // Same bytes under a different name is still the same submission — a browser that renames a
        // re-picked file must not turn a retry into a conflict.
        Assert.IsType<ContentResult>(await AttemptAsync(Upload("scan (1).pdf", "%PDF-1.4 INVOICE A")));
        Assert.Equal(1, runs);

        // THE regression: a DIFFERENT file behind the same key. Refused, so the operator is told to
        // reload rather than shown a success for the document they had just replaced.
        var conflict = Assert.IsType<ConflictObjectResult>(
            await AttemptAsync(Upload("invoice-a.pdf", "%PDF-1.4 INVOICE B — CORRECTED")));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(1, runs);

        // Removing the attachment altogether is a changed submission too.
        var withoutFile = FilterContexts(provider, key, body);
        await filter.OnActionExecutionAsync(withoutFile.Executing, () =>
        {
            runs++;
            return Task.FromResult(Executed(withoutFile.ActionContext, new OkObjectResult(new { id = 12 })));
        });
        Assert.IsType<ConflictObjectResult>(withoutFile.Executing.Result);
        Assert.Equal(1, runs);

        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            $"SELECT COUNT(*) FROM [IdempotentRequests] WHERE [Key] = '{key}'"));
    }

    /// <summary>
    /// The cutover preflight, against a real database, because that is the only place its SQL exists.
    /// <para>
    /// It probes fifteen tables for a count and an earliest date, and it runs at exactly one moment in
    /// the system's life — the go-live commit. An in-memory test proves the logic and nothing about
    /// the translation: if any of those probes cannot be turned into SQL, the failure surfaces as an
    /// exception during the client's cutover, which is the worst possible time to find out.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task TheCutoverPreflight_RunsAsRealSql_AndBlocksACommitOverExistingHistory()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        var goLive = new DateTime(2026, 8, 1);
        int bankId, capitalId;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var bank = new FinanceAccount
            {
                Name = "Cutover Bank Probe", AccountHolderName = "Seven Ventures",
                Type = FinanceAccountType.Bank, IsActive = true
            };
            var capital = new FinanceAccount
            {
                Name = "Cutover Capital Probe", AccountHolderName = "Partner One",
                Type = FinanceAccountType.Capital, IsActive = true
            };
            db.FinanceAccounts.AddRange(bank, capital);
            await db.SaveChangesAsync();
            bankId = bank.Id;
            capitalId = capital.Id;
        }

        // Clean database, nothing behind the cutover: every probe must translate and come back empty.
        await using (var db = new AppDbContext(options))
        {
            Assert.Empty(await FinanceDateRules.PreBaselineEventsAsync(db, goLive, default));
            var service = new OpeningBalanceService(db);
            var set = await service.CreateAsync(goLive, 1);
            set = await service.SaveAsync(set.Id, new SaveOpeningBalanceSetDto
            {
                ConcurrencyToken = set.ConcurrencyToken,
                Entries =
                [
                    new() { FinanceAccountId = bankId, DebitAmount = 1_000_000m },
                    new() { FinanceAccountId = capitalId, CreditAmount = 1_000_000m }
                ]
            }, 1);
            var committed = await service.CommitAsync(set.Id, set.ConcurrencyToken, 1);
            Assert.True(committed.IsCommitted);
        }

        // Now put a July expense in — the pilot-month case — and prove the reopened set cannot be
        // recommitted over it. The probe has to find it and name the date.
        await using (var db = new AppDbContext(options))
        {
            db.Expenses.Add(new Expense
            {
                FinanceAccountId = bankId, Amount = 40_000m, Category = "Rent",
                Date = new DateTime(2026, 7, 25)
            });
            await db.SaveChangesAsync();

            var found = Assert.Single(await FinanceDateRules.PreBaselineEventsAsync(db, goLive, default));
            Assert.Equal("expenses", found.Label);
            Assert.Equal(1, found.Count);
            Assert.Equal(new DateTime(2026, 7, 25), found.Earliest);

            var service = new OpeningBalanceService(db);
            var current = await service.GetCurrentAsync();
            var reopened = await service.ReopenAsync(current!.Id, new ReopenOpeningBalanceSetDto
            {
                WarningAccepted = true, ConcurrencyToken = current.ConcurrencyToken
            }, 1);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CommitAsync(reopened.Id, reopened.ConcurrencyToken, 1));
            Assert.Contains("1 expenses", error.Message);
            Assert.Contains("25 Jul 2026", error.Message);
        }
    }

    /// <summary>
    /// Every write that opens its own transaction, against a database configured the way production
    /// is.
    /// <para>
    /// The API registers SQL Server with <c>EnableRetryOnFailure</c>. EF then refuses to execute ANY
    /// operation inside a transaction the caller opened itself unless the whole unit runs through the
    /// retrying strategy: <c>ExecutionStrategy.OnFirstExecution</c> throws "the configured execution
    /// strategy 'SqlServerRetryingExecutionStrategy' does not support user-initiated transactions".
    /// </para>
    /// <para>
    /// Three paths opened one without that wrapper, and every one of them failed outright on the real
    /// database while passing every in-memory test — the in-memory provider is not relational, so the
    /// transaction was skipped and the strategy never involved. An expense entered against a VENDOR
    /// took the vendor threshold lock, which is every expense that can carry withholding tax; a
    /// fixed-asset purchase did the same; and creating, assigning or uploading against a customer
    /// document category took a serialisable one. This is the test that can tell.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task EveryWriteThatOpensItsOwnTransaction_RunsOnARetryConfiguredDatabase()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int accountId, assetAccountId, vendorId, headId, expenseId, purchaseId;
        await using (var db = new AppDbContext(options))
        {
            var account = new FinanceAccount
            {
                Name = "Retry Probe Bank", AccountHolderName = "DAMS",
                Type = FinanceAccountType.Bank, OpeningBalance = 5_000_000m, IsActive = true
            };
            var assetAccount = new FinanceAccount
            {
                Name = "Retry Probe Equipment", AccountHolderName = "DAMS",
                Type = FinanceAccountType.FixedAsset, IsActive = true
            };
            var vendor = new Vendor { Name = "Retry Probe Supplier", IsActive = true, FilerStatus = FilerStatus.Filer };
            db.AddRange(account, assetAccount, vendor);
            await db.SaveChangesAsync();
            accountId = account.Id;
            assetAccountId = assetAccount.Id;
            vendorId = vendor.Id;
            // A seeded head that DOES withhold, so the threshold lock is genuinely taken.
            headId = await db.ExpenseCategories.Where(c => c.IsWhtApplicable && c.IsActive)
                .OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();
        }

        // ── An expense against a vendor: create, edit, delete ──
        await using (var db = new AppDbContext(options))
        {
            var finance = Finance(db);
            var expense = await finance.CreateExpenseAsync(new CreateExpenseDto
            {
                FinanceAccountId = accountId, VendorId = vendorId, CategoryId = headId,
                Amount = 1_000_000m, Description = "Threshold-locked entry", Date = new DateTime(2026, 8, 3)
            }, adminUserId: 1);
            expenseId = expense.Id;
            Assert.True(expense.WhtAmount > 0m, "The head under test has to withhold, or the lock is never taken.");

            var stored = await db.Expenses.AsNoTracking().SingleAsync(e => e.Id == expenseId);
            var edited = await finance.UpdateExpenseAsync(expenseId, new UpdateExpenseDto
            {
                FinanceAccountId = accountId, VendorId = vendorId, CategoryId = headId,
                Amount = 1_200_000m, Description = "Corrected", Date = new DateTime(2026, 8, 3),
                ConcurrencyToken = Convert.ToBase64String(stored.RowVersion)
            });
            Assert.Equal(1_200_000m, edited.Amount);
        }

        // ── A fixed-asset purchase against the same vendor ──
        await using (var db = new AppDbContext(options))
        {
            var purchase = await Finance(db).CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
            {
                FinanceAccountId = accountId, AssetAccountId = assetAccountId, VendorId = vendorId,
                CategoryId = headId, Amount = 400_000m, ItemName = "Site generator",
                Date = new DateTime(2026, 8, 4)
            }, adminUserId: 1);
            purchaseId = purchase.Id;
            Assert.True(purchase.WhtAmount > 0m);
        }

        // ── The deposit-coverage rule, on real SQL and through the serialisable window ──
        await using (var db = new AppDbContext(options))
        {
            var accounts = new FinanceAccountService(db);
            var wht = new WhtService(db, accounts);
            var outstanding = (await wht.GetPayableSummaryAsync(null, null)).OutstandingPayable;
            Assert.True(outstanding > 0m);
            await wht.CreateDepositAsync(new SaveWhtDepositDto
            {
                FinanceAccountId = accountId, Amount = outstanding, ChallanNumber = "CPR-RETRY",
                DepositDate = new DateTime(2026, 8, 5)
            }, adminUserId: 1);

            var finance = Finance(db);
            var expense = await db.Expenses.AsNoTracking().SingleAsync(e => e.Id == expenseId);
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                finance.DeleteExpenseAsync(expenseId, Convert.ToBase64String(expense.RowVersion)));
            Assert.Contains("already deposited with FBR", refused.Message);

            var purchase = await db.AssetPurchases.AsNoTracking().SingleAsync(p => p.Id == purchaseId);
            var refusedPurchase = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                finance.DeleteAssetPurchaseAsync(purchaseId, Convert.ToBase64String(purchase.RowVersion)));
            Assert.Contains("already deposited with FBR", refusedPurchase.Message);
        }

        // Both are still there, and the payable is still exactly zero rather than negative.
        await using (var db = new AppDbContext(options))
        {
            Assert.True(await db.Expenses.AnyAsync(e => e.Id == expenseId));
            Assert.True(await db.AssetPurchases.AnyAsync(p => p.Id == purchaseId));
            Assert.Equal(0m, (await new WhtService(db, new FinanceAccountService(db))
                .GetPayableSummaryAsync(null, null)).OutstandingPayable);
        }

        // ── And once the deposit is out of the way, the deletes go through ──
        await using (var db = new AppDbContext(options))
        {
            var deposit = await db.WhtDeposits.AsNoTracking().SingleAsync();
            await new WhtService(db, new FinanceAccountService(db))
                .DeleteDepositAsync(deposit.Id, Convert.ToBase64String(deposit.RowVersion));
            var finance = Finance(db);
            var expense = await db.Expenses.AsNoTracking().SingleAsync(e => e.Id == expenseId);
            await finance.DeleteExpenseAsync(expenseId, Convert.ToBase64String(expense.RowVersion));
            var purchase = await db.AssetPurchases.AsNoTracking().SingleAsync(p => p.Id == purchaseId);
            await finance.DeleteAssetPurchaseAsync(purchaseId, Convert.ToBase64String(purchase.RowVersion));
        }

        await using (var db = new AppDbContext(options))
        {
            Assert.False(await db.Expenses.AnyAsync(e => e.Id == expenseId));
            Assert.False(await db.AssetPurchases.AnyAsync(p => p.Id == purchaseId));
        }

        // ── The customer-document paths that open a serialisable transaction of their own ──
        await using (var db = new AppDbContext(options))
        {
            var customer = new Customer { FullName = "Retry Probe Buyer", Phone = "03009990000", Status = CustomerStatus.Active };
            db.Add(customer);
            await db.SaveChangesAsync();

            var documents = new CustomerDocumentService(db, new NullPrivateStorage(),
                NullLogger<CustomerDocumentService>.Instance);
            var actor = new CustomerDocumentActor(Actor.UserId, Actor.DisplayName);
            var category = await documents.CreateCategoryAsync(new CreateCustomerDocumentCategoryDto
            {
                Name = "Retry probe proof", Code = "retry_probe_proof", IsRequiredByDefault = true,
                AllowedFileTypes = [".pdf"], MaxFileSizeBytes = 1024 * 1024,
                AssignmentMode = CustomerDocumentAssignmentMode.SelectedCustomers,
                SelectedCustomerIds = [customer.Id]
            }, actor);

            // Assignment ran inside the create, so the requirement exists.
            var requirement = await db.CustomerDocumentRequirements.AsNoTracking()
                .SingleAsync(r => r.CategoryId == category.Id && r.CustomerId == customer.Id);

            var uploaded = await documents.UploadAsync(customer.Id, requirement.Id,
                Convert.ToBase64String(requirement.RowVersion),
                new CustomerDocumentUpload
                {
                    Content = new MemoryStream(ProbePdf), FileName = "probe.pdf", Length = ProbePdf.LongLength
                }, actor);
            Assert.Equal(CustomerDocumentStatus.UnderReview, uploaded.Status);
            Assert.Single(await db.CustomerDocumentVersions.AsNoTracking()
                .Where(v => v.RequirementId == requirement.Id && v.IsCurrent).ToListAsync());
        }
    }

    private static (ActionContext ActionContext, ActionExecutingContext Executing) FilterContexts(
        IServiceProvider provider, string key, object arguments, IFormFile? attachment = null)
    {
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Request.Method = "POST";
        http.Request.Path = "/api/Finance/expenses";
        http.Request.Headers[IdempotentMoneyOperationFilter.HeaderName] = key;
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var args = new Dictionary<string, object?> { ["dto"] = arguments };
        if (attachment != null) args["attachment"] = attachment;
        return (actionContext, new ActionExecutingContext(actionContext, new List<IFilterMetadata>(),
            args, new object()));
    }

    /// <summary>An uploaded file the filter can hash, over a seekable buffer so
    /// <see cref="IFormFile.OpenReadStream"/> can be called more than once.</summary>
    private static IFormFile Upload(string fileName, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "attachment", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private static ActionExecutedContext Executed(ActionContext actionContext, IActionResult result) =>
        new(actionContext, new List<IFilterMetadata>(), new object()) { Result = result };


    /// <summary>
    /// The dashboard's database cost is bounded, and stays bounded as the client grows.
    /// <para>
    /// The original shape called <c>/summary</c> once per chart bucket and once per project, each
    /// summary being twenty-odd aggregates: twelve buckets and ten projects came to hundreds of
    /// database operations for one refresh, growing with every development added. Collapsing that to
    /// one HTTP request removed the round trips but not the reads — the cards still summed each
    /// source, the trend grouped the same sources by date, and the pie grouped them again by project.
    /// </para>
    /// <para>
    /// Now every source is read once, grouped by (date, project), and all three sections are folded
    /// out of those rows. The invariants asserted here are the ones that matter: the command count
    /// does not move when the number of projects doubles, and does not move when the range widens
    /// from three months to three years. Both used to multiply it.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task TheDashboard_IssuesABoundedNumberOfCommands_WhateverTheVolume()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var counter = new CommandCounter();
        var options = OptionsWith(database.ConnectionString, counter);
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var accounts = new FinanceAccountService(db);
        var finance = new FinanceService(db, new NullPrivateStorage(), accounts,
            new WhtService(db, accounts), NullLogger<FinanceService>.Instance);

        // Representative volume: eight developments, each with a recognised sale and a year of
        // monthly expenses and quarterly asset purchases. Around 8 * (1 + 12 + 4) = 136 financial
        // rows spread over twelve months and eight projects — enough that a per-project or
        // per-bucket read would show up immediately.
        await SeedDashboardVolumeAsync(db, firstProject: 1, projects: 8);

        var from = new DateTime(2026, 1, 1);
        var to = new DateTime(2026, 12, 31);

        // Warm up first: the very first query on a context pays for model and query-plan building,
        // and one-off work is not what this test is measuring.
        await finance.GetDashboardAsync(null, from, to);

        var eightProjects = await MeasureAsync(counter, () => finance.GetDashboardAsync(null, from, to));
        // Four named developments plus "Other Projects" — the pie is capped, the reads behind it are
        // not what this test is about, but a pie of one slice would mean the seed did nothing.
        Assert.Equal(5, eightProjects.Result.Distribution.Count);

        // Twice the projects, same period.
        await SeedDashboardVolumeAsync(db, firstProject: 9, projects: 8);
        var sixteenProjects = await MeasureAsync(counter, () => finance.GetDashboardAsync(null, from, to));

        // Three years instead of one: more buckets, wider windows, same reads.
        var threeYears = await MeasureAsync(counter, () =>
            finance.GetDashboardAsync(null, new DateTime(2024, 1, 1), new DateTime(2026, 12, 31)));

        // THE invariants. Not "fewer than before" — flat.
        Assert.Equal(eightProjects.Commands, sixteenProjects.Commands);
        Assert.Equal(eightProjects.Commands, threeYears.Commands);

        // And a hard ceiling, so a future edit that adds a read per source is caught even if it
        // happens to be flat in projects and buckets. Thirteen source reads plus the deposit
        // balance (3), outstanding, overdue and the project-name lookup.
        Assert.True(eightProjects.Commands <= FinanceService.PeriodAggregateQueryCount + 8,
            $"dashboard issued {eightProjects.Commands} commands; the bound is "
            + $"{FinanceService.PeriodAggregateQueryCount + 8}");
        // Lower bound too: a count that collapsed far below this would mean sources stopped being
        // read at all, which the reconciliation assertions below would then have to catch.
        Assert.True(eightProjects.Commands >= FinanceService.PeriodAggregateQueryCount,
            $"dashboard issued only {eightProjects.Commands} commands");

        // The numbers still reconcile at this volume: the bars total the cards and the pie totals
        // the revenue card. A cheaper dashboard that stopped adding up would be no fix at all.
        var dashboard = sixteenProjects.Result;
        Assert.Equal(dashboard.Summary.TotalRevenue, dashboard.Trend.Sum(b => b.Revenue));
        Assert.Equal(dashboard.Summary.TotalExpenses, dashboard.Trend.Sum(b => b.Expense));
        Assert.Equal(dashboard.Summary.TotalRevenue, dashboard.Distribution.Sum(s => s.Revenue));
        Assert.True(dashboard.Trend.Count <= 12, $"{dashboard.Trend.Count} buckets");
        var breakdown = await finance.GetCostBreakdownPageAsync(null, from, to, 0, 1000);
        Assert.Equal(dashboard.Summary.TotalExpenses, breakdown.Items.Sum(i => i.Amount));

        // The benchmark, as a ceiling rather than a number: at representative volume one refresh is
        // a handful of grouped queries, so seconds-per-refresh would mean something is wrong.
        Assert.True(sixteenProjects.Elapsed < TimeSpan.FromSeconds(15),
            $"dashboard took {sixteenProjects.Elapsed.TotalSeconds:0.00}s at 16 projects");
    }

    private sealed record Measured<T>(T Result, int Commands, TimeSpan Elapsed);

    private static async Task<Measured<T>> MeasureAsync<T>(CommandCounter counter, Func<Task<T>> work)
    {
        counter.Reset();
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await work();
        started.Stop();
        return new Measured<T>(result, counter.Count, started.Elapsed);
    }

    /// <summary>
    /// Counts every command EF sends to SQL Server. Deliberately counts commands rather than timing
    /// anything: a query count is a property of the code, so it can be asserted exactly, while a
    /// duration is a property of the machine.
    /// </summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Interlocked.Increment(ref _count);
            return base.ScalarExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Interlocked.Increment(ref _count);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>A year of activity on each of several developments, in one round of inserts.</summary>
    private static async Task SeedDashboardVolumeAsync(AppDbContext db, int firstProject, int projects)
    {
        for (var p = firstProject; p < firstProject + projects; p++)
        {
            var project = new Project { ProjectName = $"Volume {p}", Location = "Karachi", CreatedById = 1 };
            var equipment = new FinanceAccount
            {
                Name = $"Equipment {p}", AccountHolderName = "DAMS",
                Type = FinanceAccountType.FixedAsset, IsActive = true
            };
            var bank = new FinanceAccount
            {
                Name = $"Bank {p}", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true
            };
            var unit = new Unit
            {
                Project = project, UnitNumber = $"V-{p}", UnitType = "Apartment",
                Price = 4_000_000m, Status = UnitStatus.Sold
            };
            var customer = new Customer { FullName = $"Volume Buyer {p}", Phone = $"0300000{p:0000}", Status = CustomerStatus.Active };
            var booking = new Booking
            {
                BookingReference = $"BK-VOL-{p}", Customer = customer, Unit = unit, Source = CustomerSource.Referral,
                Status = BookingStatus.PossessionGiven, AgreedSalePrice = 4_000_000m, DiscountAmount = 0m,
                BookingDate = new DateTime(2025, 12, 1)
            };
            db.AddRange(project, equipment, bank, unit, customer, booking);
            await db.SaveChangesAsync();

            db.BookingSaleRecognitions.Add(new BookingSaleRecognition
            {
                BookingId = booking.Id, RecognitionDate = new DateTime(2026, (p % 12) + 1, 10),
                NetSaleValue = 4_000_000m,
                RecognizedAt = new DateTime(2026, (p % 12) + 1, 10, 6, 0, 0, DateTimeKind.Utc)
            });
            for (var month = 1; month <= 12; month++)
            {
                db.Expenses.Add(new Expense
                {
                    ProjectId = project.Id, FinanceAccountId = bank.Id, Category = "Office Rent",
                    Amount = 50_000m, Date = new DateTime(2026, month, 15)
                });
                if (month % 3 == 0)
                    db.AssetPurchases.Add(new AssetPurchase
                    {
                        ProjectId = project.Id, AssetAccountId = equipment.Id, FinanceAccountId = bank.Id,
                        Amount = 100_000m, ItemName = $"Rack {p}-{month}", Category = "Equipment",
                        Date = new DateTime(2026, month, 20)
                    });
            }
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Two admins correcting the same salary. Without a concurrency token the slower save won
    /// silently: it rewrote the amount, the pay date, the payroll period AND the linked Expense
    /// back to whatever its own screen had shown, so a corrected 120,000 quietly became 90,000
    /// again — with the posted expense following it — and both admins were told they had succeeded.
    /// </summary>
    [SqlServerFact]
    public async Task ConcurrentSalaryCorrections_CannotSilentlyOverwriteEachOther()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var bank = new FinanceAccount { Name = "Payroll Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var employee = new Employee
        {
            FullName = "Payroll Subject", JobTitle = "Engineer", Department = "Build",
            Phone = "03001234567", Salary = 100_000m, JoinDate = new DateTime(2025, 1, 1),
            Status = EmployeeStatus.Active
        };
        db.AddRange(bank, employee);
        await db.SaveChangesAsync();

        var created = await new EmployeeService(db, new FinanceAccountService(db)).GenerateSalaryAsync(
            employee.Id,
            new DAMS.Application.DTOs.EmployeeDtos.GenerateSalaryDto
            {
                Amount = 100_000m, PayDate = new DateTime(2026, 8, 1), FinanceAccountId = bank.Id
            }, adminUserId: 1);

        // On SQL Server the record really does carry a version, and it reaches the client.
        Assert.False(string.IsNullOrWhiteSpace(created.ConcurrencyToken));
        var token = created.ConcurrencyToken;

        // Admin A corrects the amount and wins.
        await using (var first = new AppDbContext(options))
        {
            await new EmployeeService(first, new FinanceAccountService(first)).UpdateSalaryAsync(
                created.Id, new DAMS.Application.DTOs.EmployeeDtos.UpdateSalaryDto
                {
                    Amount = 120_000m, ConcurrencyToken = token
                });
        }

        // Admin B, holding the SAME stale token, is refused rather than silently overwriting A.
        await using (var second = new AppDbContext(options))
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                new EmployeeService(second, new FinanceAccountService(second)).UpdateSalaryAsync(
                    created.Id, new DAMS.Application.DTOs.EmployeeDtos.UpdateSalaryDto
                    {
                        Amount = 90_000m, ConcurrencyToken = token
                    }));
        }

        // Omitting the token entirely is refused too — otherwise the protection would be opt-out by
        // simply not sending a field.
        await using (var third = new AppDbContext(options))
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                new EmployeeService(third, new FinanceAccountService(third)).UpdateSalaryAsync(
                    created.Id, new DAMS.Application.DTOs.EmployeeDtos.UpdateSalaryDto { Amount = 90_000m }));
        }

        // A's correction stands, and the linked expense audit behaviour is intact: the posted
        // expense moved with it and no second expense was created.
        await using (var check = new AppDbContext(options))
        {
            var salary = await check.EmployeeSalaries.AsNoTracking().SingleAsync();
            Assert.Equal(120_000m, salary.Amount);
            var expense = Assert.Single(await check.Expenses.AsNoTracking().ToListAsync());
            Assert.Equal(120_000m, expense.Amount);
            Assert.Equal(salary.ExpenseId, expense.Id);

            // And a fresh token lets the next legitimate correction through.
            var fresh = Convert.ToBase64String(salary.RowVersion);
            await new EmployeeService(check, new FinanceAccountService(check)).UpdateSalaryAsync(
                created.Id, new DAMS.Application.DTOs.EmployeeDtos.UpdateSalaryDto
                {
                    Amount = 130_000m, ConcurrencyToken = fresh
                });
        }
        await using (var check = new AppDbContext(options))
            Assert.Equal(130_000m, (await check.EmployeeSalaries.AsNoTracking().SingleAsync()).Amount);
    }
    /// <summary>
    /// Staff invitations make Users.Password nullable and add an account status. Every login
    /// that already existed must still be Active afterwards — a numbering or default-value
    /// mistake here locks every Admin, Client, Manager and Employee out of the system.
    /// </summary>
    [SqlServerFact]
    public async Task StaffInvitationMigration_KeepsExistingLoginsActiveAndEnforcesInvitationInvariants()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);

        // The schema as it stood before staff invitations: Password is still NOT NULL and
        // AccountStatus does not exist yet, so these rows have to be written as raw SQL.
        await using (var db = new AppDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync("20260821020414_AddEmployeeSalaryRowVersion");

        await ExecuteAsync(database.ConnectionString, """
            INSERT INTO [Users] ([RoleId], [FullName], [Email], [Password]) VALUES
                (1, N'Legacy Admin',    N'admin@dams.test',   N'$2a$11$legacyadminhash'),
                (2, N'Legacy Client',   N'client@dams.test',  N'$2a$11$legacyclienthash'),
                (3, N'Legacy Manager',  N'manager@dams.test', N'$2a$11$legacymanagerhash'),
                (4, N'Legacy Employee', N'sales@dams.test',   N'$2a$11$legacysaleshash');
            """);

        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        // The explicit backfill, not a CLR or column default, is what has to hold here.
        Assert.Equal(0, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [Users] WHERE [AccountStatus] <> 0"));
        Assert.Equal(4, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [Users] WHERE [Password] IS NOT NULL"));
        Assert.Equal(1, await ScalarAsync(database.ConnectionString, """
            SELECT CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'Password'
            """));

        int invitedUserId;
        int adminUserId;
        await using (var db = new AppDbContext(options))
        {
            Assert.All(await db.Users.ToListAsync(),
                u => Assert.Equal(UserAccountStatus.Active, u.AccountStatus));
            adminUserId = await db.Users
                .Where(u => u.Email == "admin@dams.test").Select(u => u.UserId).SingleAsync();

            // An invited login exists before it has any credential to verify against.
            var invited = new User
            {
                RoleId = 4,
                FullName = "Invited Sales",
                Email = "invited@dams.test",
                Password = null,
                AccountStatus = UserAccountStatus.Invited
            };
            db.Users.Add(invited);
            await db.SaveChangesAsync();
            invitedUserId = invited.UserId;

            db.StaffInvitations.Add(new StaffInvitation
            {
                UserId = invitedUserId,
                InvitedByUserId = adminUserId,
                TokenHash = "hash-one",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(3)
            });
            await db.SaveChangesAsync();
        }

        // Both relationships resolve, and to two different logins.
        await using (var db = new AppDbContext(options))
        {
            var invitation = await db.StaffInvitations
                .Include(i => i.User).Include(i => i.InvitedByUser).SingleAsync();
            Assert.Equal("invited@dams.test", invitation.User.Email);
            Assert.Null(invitation.User.Password);
            Assert.Equal(UserAccountStatus.Invited, invitation.User.AccountStatus);
            Assert.Equal("admin@dams.test", invitation.InvitedByUser.Email);
            Assert.Null(invitation.AcceptedAt);
            Assert.Null(invitation.RevokedAt);
        }

        // One token hash, one invitation — a replayed or colliding token cannot resolve twice.
        await using (var db = new AppDbContext(options))
        {
            db.StaffInvitations.Add(new StaffInvitation
            {
                UserId = invitedUserId,
                InvitedByUserId = adminUserId,
                TokenHash = "hash-one",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(3)
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Restrict on both foreign keys: who was granted access, by whom, survives an attempt
        // to delete the login it refers to, rather than vanishing with it.
        await using (var db = new AppDbContext(options))
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.UserId == invitedUserId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    /// <summary>
    /// Single use has to be a property of the database, not of the order two requests happen to
    /// arrive in. Two clicks on the same activation link at the same instant — the second tab,
    /// the impatient double-click, the replay — must leave exactly one of them holding a
    /// password, and the loser must not be able to overwrite the winner's.
    /// </summary>
    [SqlServerFact]
    public async Task ConcurrentStaffActivations_LetExactlyOneRequestConsumeTheInvitation()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        const string rawToken = "concurrent-activation-token";
        await using (var db = new AppDbContext(options))
        {
            var admin = new User
            {
                RoleId = 1, FullName = "SQL Admin", Email = "sql-admin@dams.test",
                Password = "$2a$11$adminhash", AccountStatus = UserAccountStatus.Active
            };
            var invited = new User
            {
                RoleId = 4, FullName = "SQL Invited", Email = "sql-invited@dams.test",
                Password = null, AccountStatus = UserAccountStatus.Invited
            };
            db.Users.AddRange(admin, invited);
            await db.SaveChangesAsync();

            db.Employees.Add(new Employee
            {
                FullName = "SQL Invited", UserId = invited.UserId, Email = invited.Email,
                JobTitle = "Sales Executive", JoinDate = new DateTime(2026, 1, 1),
                Status = EmployeeStatus.Active
            });
            db.StaffInvitations.Add(new StaffInvitation
            {
                UserId = invited.UserId,
                InvitedByUserId = admin.UserId,
                // The hash the service will compute for the token below, written independently
                // of the service so this test does not depend on its internals being public.
                TokenHash = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(rawToken))),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
        }

        async Task<StaffActivationResult> ActivateAsync(string password)
        {
            await using var context = new AppDbContext(options);
            var service = new StaffInvitationService(
                context, new NotificationSettingsStore(context),
                new ThrowingEmailSender(), TimeProvider.System);
            return await service.ActivateAsync(rawToken, password);
        }

        var outcomes = await Task.WhenAll(
            ActivateAsync("first-request-pass-1"), ActivateAsync("second-request-pass-2"));

        var winner = Assert.Single(outcomes, o => o.Activated);
        var loser = Assert.Single(outcomes, o => !o.Activated);
        Assert.Equal(StaffActivationFailure.InvalidInvitation, loser.Failure);

        await using (var db = new AppDbContext(options))
        {
            var user = await db.Users.SingleAsync(u => u.Email == "sql-invited@dams.test");
            Assert.Equal(UserAccountStatus.Active, user.AccountStatus);

            // Exactly one of the two passwords took, and the invitation is spent once.
            var accepted = new[] { "first-request-pass-1", "second-request-pass-2" }
                .Count(p => BCrypt.Net.BCrypt.Verify(p, user.Password));
            Assert.Equal(1, accepted);
            Assert.NotNull(winner);

            var invitation = await db.StaffInvitations.SingleAsync();
            Assert.NotNull(invitation.AcceptedAt);
        }
    }

    /// <summary>Activation must never send mail, so the sender it is given cannot.</summary>
    private sealed class ThrowingEmailSender : IEmailSender
    {
        public string ProviderName => "none";

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Activation must not send email.");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static DbContextOptions<AppDbContext> Options(string connectionString,
        SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString,
            sql => sql.EnableRetryOnFailure());
        if (interceptor != null) builder.AddInterceptors(interceptor);
        return builder.Options;
    }

    /// <summary>Same options, with command-level interceptors. Separately named rather than an
    /// overload, because <see cref="SaveChangesInterceptor"/> is itself an
    /// <see cref="IInterceptor"/> and the two would be ambiguous at every existing call site.</summary>
    private static DbContextOptions<AppDbContext> OptionsWith(
        string connectionString, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString,
            sql => sql.EnableRetryOnFailure());
        if (interceptors.Length > 0) builder.AddInterceptors(interceptors);
        return builder.Options;
    }

    /// <summary>For schema queries, which take no parameters.</summary>
    private static async Task<int> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> ScalarAsync(string connectionString, string sql, int id)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class FailDocumentAssignmentInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            FailIfAssignment(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            FailIfAssignment(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private static void FailIfAssignment(DbContext? context)
        {
            if (context?.ChangeTracker.Entries<CustomerDocumentRequirement>()
                    .Any(entry => entry.State == EntityState.Added) == true)
                throw new InvalidOperationException("Simulated SQL assignment persistence failure.");
        }
    }

    /// <summary>
    /// Simulates a second browser winning a concurrent OAuth reconnect: the moment the context
    /// under test is about to insert its own new connection row for a given Meta account, this
    /// inserts and commits a colliding row for the same account through a completely separate
    /// connection first, so the context under test's own insert then genuinely collides with the
    /// database's unique index rather than merely being told to expect one.
    /// </summary>
    private sealed class InsertCollidingConnectionInterceptor : SaveChangesInterceptor
    {
        private readonly string _connectionString;
        private readonly string _externalAccountId;
        private bool _raced;

        public InsertCollidingConnectionInterceptor(string connectionString, string externalAccountId)
        {
            _connectionString = connectionString;
            _externalAccountId = externalAccountId;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_raced && eventData.Context is not null
                && eventData.Context.ChangeTracker.Entries<ExternalIntegrationConnection>()
                    .Any(e => e.State == EntityState.Added && e.Entity.ExternalAccountId == _externalAccountId))
            {
                _raced = true;

                await using var racer = new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options);
                racer.ExternalIntegrationConnections.Add(new ExternalIntegrationConnection
                {
                    Provider = "meta", ExternalAccountId = _externalAccountId, DisplayName = "Racing winner",
                    Status = ExternalIntegrationConnectionStatus.Connected
                });
                await racer.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    /// <summary>
    /// Simulates a second connection legitimately winning a concurrent "enable this Page" race:
    /// the moment the context under test is about to save its own enable-change for a specific
    /// resource, this runs a completely separate connection's full SetResourceEnabledAsync
    /// (including its own real Meta Subscribe call) to commit first through a totally separate
    /// AppDbContext/connection, so the context under test's own save then genuinely collides with
    /// the database's unique index — with a real, already-established winning subscription
    /// sitting behind it, not merely a row.
    /// </summary>
    private sealed class RunFullEnableThroughAnotherConnectionInterceptor : SaveChangesInterceptor
    {
        private readonly string _connectionString;
        private readonly DAMS.Application.Tests.Integrations.FakeMetaGraphClient _graph;
        private readonly DAMS.Application.Tests.Integrations.PlaintextSecretProtector _protector;
        private readonly MetaIntegrationOptions _metaOptions;
        private readonly int _winnerConnectionId;
        private readonly int _winnerResourceId;
        private readonly int _watchedResourceId;
        private bool _raced;

        public RunFullEnableThroughAnotherConnectionInterceptor(
            string connectionString,
            DAMS.Application.Tests.Integrations.FakeMetaGraphClient graph,
            DAMS.Application.Tests.Integrations.PlaintextSecretProtector protector,
            MetaIntegrationOptions metaOptions,
            int winnerConnectionId, int winnerResourceId, int watchedResourceId)
        {
            _connectionString = connectionString;
            _graph = graph;
            _protector = protector;
            _metaOptions = metaOptions;
            _winnerConnectionId = winnerConnectionId;
            _winnerResourceId = winnerResourceId;
            _watchedResourceId = watchedResourceId;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_raced && eventData.Context is not null
                && eventData.Context.ChangeTracker.Entries<ExternalIntegrationResource>()
                    .Any(e => e.State == EntityState.Modified && e.Entity.Id == _watchedResourceId && e.Entity.IsEnabled))
            {
                _raced = true;

                await using var racer = new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options);
                var racerSync = new DAMS.Application.Services.Integrations.MetaResourceSyncService(
                    racer, _graph, _protector, _metaOptions,
                    NullLogger<DAMS.Application.Services.Integrations.MetaResourceSyncService>.Instance);
                var racerIntegration = new DAMS.Application.Services.Integrations.MetaIntegrationService(
                    racer, _graph, _protector, racerSync, _metaOptions,
                    NullLogger<DAMS.Application.Services.Integrations.MetaIntegrationService>.Instance);

                await racerIntegration.SetResourceEnabledAsync(
                    _winnerConnectionId, _winnerResourceId, isEnabled: true, cancellationToken);
            }

            return result;
        }
    }

    /// <summary>
    /// Simulates a second admin toggling the very same resource row at the very same moment: as
    /// the context under test is about to save its own change to that row, this runs the
    /// identical SetResourceEnabledAsync — real Meta call included — through a completely
    /// separate AppDbContext and commits it first. The row's rowversion has therefore genuinely
    /// moved on by the time the context under test's UPDATE reaches the database, which is the
    /// only way to produce a real DbUpdateConcurrencyException here rather than merely assert
    /// that one would be handled.
    /// </summary>
    private sealed class RunFullToggleThroughASecondContextInterceptor : SaveChangesInterceptor
    {
        private readonly string _connectionString;
        private readonly DAMS.Application.Tests.Integrations.FakeMetaGraphClient _graph;
        private readonly DAMS.Application.Tests.Integrations.PlaintextSecretProtector _protector;
        private readonly MetaIntegrationOptions _metaOptions;
        private readonly int _connectionId;
        private readonly int _resourceId;
        private readonly bool _toggleTo;
        private bool _raced;

        public RunFullToggleThroughASecondContextInterceptor(
            string connectionString,
            DAMS.Application.Tests.Integrations.FakeMetaGraphClient graph,
            DAMS.Application.Tests.Integrations.PlaintextSecretProtector protector,
            MetaIntegrationOptions metaOptions,
            int connectionId, int resourceId, bool toggleTo)
        {
            _connectionString = connectionString;
            _graph = graph;
            _protector = protector;
            _metaOptions = metaOptions;
            _connectionId = connectionId;
            _resourceId = resourceId;
            _toggleTo = toggleTo;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_raced && eventData.Context is not null
                && eventData.Context.ChangeTracker.Entries<ExternalIntegrationResource>()
                    .Any(e => e.State == EntityState.Modified
                              && e.Entity.Id == _resourceId
                              && e.Entity.IsEnabled == _toggleTo))
            {
                _raced = true;

                // Deliberately built without this interceptor, so the racer's own save cannot
                // recurse back into here.
                await using var racer = new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options);
                var racerSync = new DAMS.Application.Services.Integrations.MetaResourceSyncService(
                    racer, _graph, _protector, _metaOptions,
                    NullLogger<DAMS.Application.Services.Integrations.MetaResourceSyncService>.Instance);
                var racerIntegration = new DAMS.Application.Services.Integrations.MetaIntegrationService(
                    racer, _graph, _protector, racerSync, _metaOptions,
                    NullLogger<DAMS.Application.Services.Integrations.MetaIntegrationService>.Instance);

                await racerIntegration.SetResourceEnabledAsync(
                    _connectionId, _resourceId, _toggleTo, cancellationToken);
            }

            return result;
        }
    }

    private sealed class NullPrivateStorage : ICustomerDocumentStorage, IFinancialEvidenceStorage, IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) =>
            Task.FromResult($"{Guid.NewGuid():N}{extension}");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SqlTestDatabase : IAsyncDisposable
    {
        private readonly string _masterConnection;
        private readonly string _databaseName;
        public string ConnectionString { get; }

        private SqlTestDatabase(string masterConnection, string databaseName, string connectionString)
        {
            _masterConnection = masterConnection; _databaseName = databaseName; ConnectionString = connectionString;
        }

        public static async Task<SqlTestDatabase> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("DAMS_SQLSERVER_TEST_CONNECTION")
                ?? throw new InvalidOperationException("DAMS_SQLSERVER_TEST_CONNECTION is required.");
            var databaseName = $"DamsProductionTests_{Guid.NewGuid():N}";
            var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
            var test = new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName };
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{databaseName}]", connection);
            await command.ExecuteNonQueryAsync();
            return new SqlTestDatabase(master.ConnectionString, databaseName, test.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_databaseName.StartsWith("DamsProductionTests_", StringComparison.Ordinal)
                || _databaseName.Length != "DamsProductionTests_".Length + 32)
                throw new InvalidOperationException("Refusing to drop an unexpected SQL test database.");
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(_masterConnection);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];",
                connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
