using DAMS.Application.Common;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.DTOs.CustomerDocumentDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
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

    private static DbContextOptions<AppDbContext> Options(string connectionString,
        SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString,
            sql => sql.EnableRetryOnFailure());
        if (interceptor != null) builder.AddInterceptors(interceptor);
        return builder.Options;
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
