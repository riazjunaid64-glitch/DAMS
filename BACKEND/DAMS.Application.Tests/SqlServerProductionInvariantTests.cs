using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
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

    private static DbContextOptions<AppDbContext> Options(string connectionString,
        SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString,
            sql => sql.EnableRetryOnFailure());
        if (interceptor != null) builder.AddInterceptors(interceptor);
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
