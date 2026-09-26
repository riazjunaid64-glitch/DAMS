using DAMS.Api.Filters;
using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.BookingRequestDtos;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.DTOs.CustomerDocumentDtos;
using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.DTOs.InstallmentDtos;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.DTOs.WhtDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Application.Services.Notifications;
using DAMS.Application.Services.Integrations;
using DAMS.Application.Tests.Integrations;
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
using Microsoft.Extensions.Logging;
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
                Commission(bookingId, partnerId, BookingCommissionStatus.Cancelled, 20m));
            db.CustomerRebates.AddRange(
                Rebate(bookingId, customer.Id, CustomerRebateStatus.Cancelled, 10m),
                Rebate(bookingId, customer.Id, CustomerRebateStatus.Cancelled, 20m));
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

            verify.BookingCommissions.Add(Commission(bookingId, partnerId, BookingCommissionStatus.Pending, 100m));
            await verify.SaveChangesAsync();
        }

        int commissionId;
        string token;
        await using (var db = new AppDbContext(options))
        {
            var commission = await db.BookingCommissions.SingleAsync(c => c.BookingId == bookingId
                && c.Status == BookingCommissionStatus.Pending);
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
            Assert.Equal(BookingCommissionStatus.Pending,
                (await db.BookingCommissions.FindAsync(commissionId))!.Status);
        }
    }

    /// <summary>
    /// InstallmentService had no transaction lock at all until it was given one to match
    /// CommissionRebateService's money-moving methods: two concurrent installment payments, each
    /// reading "100,000 is owed" before either commits, must not both be allowed to collect against
    /// it. Mirrors MigrationsTransactionsAndConcurrentPayouts_PreserveProductionInvariants above,
    /// which proves the same thing for a commission payout.
    /// </summary>
    [SqlServerFact]
    public async Task ConcurrentInstallmentPayments_CannotTogetherOverpayTheSameInstallment()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int bookingId; int installmentId; int accountId;
        await using (var db = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "Concurrency", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project, UnitNumber = "CX-01", UnitType = "Apartment",
                Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan
            };
            var customer = new Customer { FullName = "Concurrency Customer", Phone = "03001111111", Status = CustomerStatus.Active };
            var booking = new Booking
            {
                BookingReference = $"CX-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = 1_000_000m,
                BookingAmountRequired = 100_000m, BookingAmountReceived = 100_000m,
                BookingDate = DateTime.UtcNow
            };
            var installment = new Installment
            {
                Booking = booking, SequenceNumber = 1, Type = InstallmentType.Regular,
                DueDate = DateTime.UtcNow.AddMonths(1), Amount = 100_000m, Status = InstallmentStatus.Pending
            };
            var account = new FinanceAccount
            {
                Name = "Concurrency Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true
            };
            db.AddRange(project, unit, customer, booking, installment, account);
            await db.SaveChangesAsync();
            bookingId = booking.Id; installmentId = installment.Id; accountId = account.Id;
        }

        async Task<Exception?> PayAsync(string reference)
        {
            try
            {
                await using var context = new AppDbContext(options);
                var service = new InstallmentService(context, new FinanceAccountService(context));
                await service.RecordInstallmentPaymentAsync(bookingId, installmentId, new RecordInstallmentPaymentDto
                {
                    Amount = 70_000m, FinanceAccountId = accountId, PaymentMethod = PaymentMethod.Cash,
                    PaidAt = DateTime.UtcNow, PaymentReference = reference
                }, adminUserId: 901);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        // Each side believes the full 100,000 is available; together they ask for 140,000.
        var outcomes = await Task.WhenAll(PayAsync("cx-payment-a"), PayAsync("cx-payment-b"));
        Assert.Single(outcomes, error => error == null);
        Assert.Single(outcomes, error => error is InvalidOperationException
            && error.Message.Contains("remaining installment balance"));

        await using var verify = new AppDbContext(options);
        Assert.Equal(70_000m, await verify.Payments.Where(p => p.InstallmentId == installmentId).SumAsync(p => p.Amount));
        Assert.Equal(InstallmentStatus.PartiallyPaid, (await verify.Installments.FindAsync(installmentId))!.Status);
    }

    /// <summary>
    /// The cross-flow half of the same gap: a schedule built while a non-cash rebate credit stood
    /// is smaller by that credit, so reversing it is only safe while the schedule can still be
    /// rebuilt — and an installment payment permanently pins the schedule the instant it lands
    /// (Payment.InstallmentId is Restrict). Concurrently reversing the credit and paying the
    /// installment it was covering must not let both through: that would strand the reversed
    /// amount on a plan nothing can ever collect it with. Whichever side wins the race, the
    /// invariant BookingCreditPolicy itself enforces — stranded is either zero or still
    /// repairable — must hold once both requests have settled.
    /// </summary>
    [SqlServerFact]
    public async Task ConcurrentCreditReversalAndInstallmentPayment_CannotStrandAnUncollectableBalance()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int bookingId; int rebateId; int disbursementId; int installmentId; int accountId;
        await using (var db = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "Credit Race", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project, UnitNumber = "CR-01", UnitType = "Apartment",
                Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan
            };
            var customer = new Customer { FullName = "Credit Race Customer", Phone = "03002222222", Status = CustomerStatus.Active };
            var booking = new Booking
            {
                BookingReference = $"CR-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = 1_000_000m,
                BookingAmountRequired = 100_000m, BookingAmountReceived = 100_000m,
                BookingDate = DateTime.UtcNow
            };
            var account = new FinanceAccount
            {
                Name = "Credit Race Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true
            };
            db.AddRange(project, unit, customer, booking, account);
            await db.SaveChangesAsync();
            bookingId = booking.Id; accountId = account.Id;

            var rebates = new CommissionRebateService(db, new FinanceAccountService(db), new NullPrivateStorage());
            var workspace = await rebates.CreateRebateAsync(bookingId, new CreateCustomerRebateDto
            {
                CalculationType = FinancialCalculationType.FixedAmount,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                FixedAmount = 200_000m, Reason = "Loyalty credit", Method = CustomerRebateMethod.OutstandingBalanceReduction
            }, Actor);
            var rebate = Assert.Single(workspace.Rebates);
            rebateId = rebate.Id;
            workspace = await rebates.RecordRebateDisbursementAsync(bookingId, rebateId, new RecordRebateDisbursementDto
            {
                Method = CustomerRebateMethod.OutstandingBalanceReduction, Amount = 200_000m, AppliedAt = DateTime.UtcNow,
                IdempotencyKey = "seed-disbursement", RebateConcurrencyToken = rebate.ConcurrencyToken
            }, Actor);
            rebate = Assert.Single(workspace.Rebates);
            Assert.Equal(CustomerRebateStatus.Applied, rebate.Status);
            disbursementId = Assert.Single(rebate.Disbursements).Id;

            // Pool = 1,000,000 net price - 100,000 booking amount - 200,000 credit = 700,000, all in
            // one installment, built while the credit still stands.
            var installments = new InstallmentService(db, new FinanceAccountService(db));
            var schedule = await installments.GenerateScheduleAsync(bookingId, new GenerateInstallmentPlanDto
            {
                AgreedSalePrice = 1_000_000m, DiscountPercent = 0m, Frequency = InstallmentFrequency.Monthly,
                NumberOfInstallments = 1, InstallmentStartDate = DateTime.UtcNow.AddMonths(1)
            }, adminUserId: 901);
            Assert.Equal(700_000m, schedule.ScheduleTotal);
            installmentId = Assert.Single(schedule.Items).Id;
        }

        async Task<Exception?> ReverseAsync()
        {
            try
            {
                await using var context = new AppDbContext(options);
                var service = new CommissionRebateService(context, new FinanceAccountService(context), new NullPrivateStorage());
                await service.ReverseRebateDisbursementAsync(bookingId, rebateId, disbursementId,
                    new ReverseMoneyMovementDto { Amount = 200_000m, Reason = "Credit withdrawn", IdempotencyKey = "cr-reversal" }, Actor);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        async Task<Exception?> PayAsync()
        {
            try
            {
                await using var context = new AppDbContext(options);
                var service = new InstallmentService(context, new FinanceAccountService(context));
                // Any amount pins the installment (Payment.InstallmentId is Restrict) — it does not
                // need to pay the installment off to make the schedule unreformable.
                await service.RecordInstallmentPaymentAsync(bookingId, installmentId, new RecordInstallmentPaymentDto
                {
                    Amount = 1_000m, FinanceAccountId = accountId, PaymentMethod = PaymentMethod.Cash,
                    PaidAt = DateTime.UtcNow, PaymentReference = "cr-pin"
                }, adminUserId: 901);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        var outcomes = await Task.WhenAll(ReverseAsync(), PayAsync());
        var (reversalError, paymentError) = (outcomes[0], outcomes[1]);

        // EVERYTHING is read before ANYTHING is asserted. This test failed once, intermittently, and
        // could not be diagnosed afterwards: the exception-shape check came first, so an unexpected
        // failure ended the run on its message alone and nothing recorded what the database actually
        // held at that moment. Re-running over the evidence is not an investigation.
        await using var verify = new AppDbContext(options);
        var finalBooking = await verify.Bookings.SingleAsync(b => b.Id == bookingId);
        var scheduleTotal = await verify.Installments.Where(i => i.BookingId == bookingId)
            .SumAsync(i => (decimal?)i.Amount) ?? 0m;
        var finalCredits = await BookingCreditPolicy.GetNonCashCreditsAsync(verify, bookingId);
        var canRegenerate = await BookingCreditPolicy.CanRegenerateScheduleAsync(verify, bookingId);
        var stranded = await BookingCreditPolicy.UncollectableFromReversedCreditsAsync(
            verify, finalBooking, scheduleTotal, finalCredits);
        var receipts = await verify.Payments
            .Where(p => p.BookingId == bookingId && p.Type == PaymentType.Installment)
            .Select(p => new { p.Id, p.Amount, p.InstallmentId }).ToListAsync();
        var reversals = await verify.RebateDisbursementReversals
            .Where(rv => rv.DisbursementId == disbursementId)
            .Select(rv => new { rv.Id, rv.Amount }).ToListAsync();
        var installment = await verify.Installments.SingleAsync(i => i.Id == installmentId);

        static string Describe(Exception? ex)
        {
            if (ex == null) return "COMMITTED";
            var inner = ex.InnerException;
            var sqlNumber = inner is SqlException sql ? $" [SQL error {sql.Number}]"
                : ex is SqlException outer ? $" [SQL error {outer.Number}]" : "";
            return $"{ex.GetType().Name}: {ex.Message}{sqlNumber}"
                + (inner != null ? $" <- {inner.GetType().Name}: {inner.Message}" : "");
        }

        // Attached to every assertion below, so a failure of ANY kind arrives with the persisted
        // financial state that produced it rather than only the exception that surfaced.
        var evidence = string.Join(Environment.NewLine,
            $"  reversal:     {Describe(reversalError)}",
            $"  payment:      {Describe(paymentError)}",
            $"  receipts:     {receipts.Count} [{string.Join(", ", receipts.Select(r => $"#{r.Id} {r.Amount:0.00} on installment {r.InstallmentId}"))}]",
            $"  reversals:    {reversals.Count} [{string.Join(", ", reversals.Select(r => $"#{r.Id} {r.Amount:0.00}"))}]",
            $"  net credits:  {finalCredits:0.00}",
            $"  schedule:     {scheduleTotal:0.00} total, installment {installmentId} is {installment.Status} "
                + $"({receipts.Where(r => r.InstallmentId == installmentId).Sum(r => r.Amount):0.00} "
                + $"received of {installment.Amount:0.00})",
            $"  regenerate:   {(canRegenerate ? "allowed" : "blocked")}, stranded {stranded:0.00}",
            $"  booking:      {finalBooking.Status}");

        // Whichever request lost the race must have lost it with the guard's own message, not some
        // unrelated failure (a real deadlock that exhausted its retries, a translation error, etc.).
        foreach (var error in outcomes)
            if (error != null)
                Assert.True(error is InvalidOperationException ioe && (ioe.Message.Contains("uncollectable")
                        || ioe.Message.Contains("no way to collect it")),
                    $"Unexpected failure shape.{Environment.NewLine}{evidence}");

        // Exactly one of them may commit, and one of them MUST. Both succeeding means the guards did
        // not see each other — the write skew this fixture exists to catch. Both failing is not a
        // safe outcome either: it means neither side got through, which is what an exhausted deadlock
        // retry looks like, and the safety assertion below would pass on it vacuously.
        Assert.True((reversalError == null) ^ (paymentError == null),
            $"Expected exactly one of the two operations to commit.{Environment.NewLine}{evidence}");

        // And the side that committed must be there in the money, not merely un-rejected.
        if (paymentError == null)
        {
            Assert.True(receipts.Count == 1 && receipts[0].Amount == 1_000m,
                $"The payment reported success but is not in the ledger.{Environment.NewLine}{evidence}");
            Assert.True(reversals.Count == 0,
                $"The reversal was rejected yet persisted.{Environment.NewLine}{evidence}");
            // Its own guard rejected the reversal because this receipt pinned the smaller plan.
            Assert.Equal(200_000m, finalCredits);
        }
        else
        {
            Assert.True(reversals.Count == 1 && reversals[0].Amount == 200_000m,
                $"The reversal reported success but is not in the ledger.{Environment.NewLine}{evidence}");
            Assert.True(receipts.Count == 0,
                $"The payment was rejected yet persisted.{Environment.NewLine}{evidence}");
            // The credit is gone, so the debt it was absorbing is back and the plan is short of it —
            // which is exactly why regeneration has to remain available.
            Assert.Equal(0m, finalCredits);
            Assert.True(canRegenerate,
                $"The reversal restored a debt the plan cannot collect and left no way to rebuild it."
                + $"{Environment.NewLine}{evidence}");
        }

        Assert.True(stranded == 0m || canRegenerate,
            $"Stranded {stranded:0.00} on a schedule that cannot be regenerated — the reversed-credit "
            + $"guard was bypassed by the race.{Environment.NewLine}{evidence}");
    }

    /// <summary>
    /// Recording a customer payment runs inside a retrying execution strategy, and a commit whose
    /// acknowledgement is lost on the way back looks exactly like a commit that never happened — so
    /// the strategy replays the whole delegate. Replaying it used to take the money again: a second
    /// Payment row under a second receipt number, with nothing on either to say they were one
    /// intent. The endpoint's Idempotency-Key filter cannot see this; it guards the HTTP call from
    /// outside, and this replay happens wholly within one such call.
    /// <para>
    /// Both payment paths are driven here through the seam the retry itself uses — the same attempt
    /// key twice — because that is precisely what a replay is. Part payments on purpose: a replayed
    /// FULL payment happens to be caught by the "already received"/"already paid" status guards,
    /// which is what let this hide.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task ReplayingOnePaymentAttempt_TakesTheMoneyOnceNotTwice()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int bookingId, installmentId, accountId;
        await using (var db = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "Replay", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project, UnitNumber = "RP-01", UnitType = "Apartment",
                Price = 1_000_000m, Status = UnitStatus.Booked
            };
            var customer = new Customer { FullName = "Replay Customer", Phone = "03002223333", Status = CustomerStatus.Active };
            var booking = new Booking
            {
                BookingReference = $"RP-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.AwaitingBookingAmount, AgreedSalePrice = 1_000_000m,
                BookingAmountRequired = 100_000m, BookingAmountReceived = 0m, BookingDate = DateTime.UtcNow
            };
            var installment = new Installment
            {
                Booking = booking, SequenceNumber = 1, Type = InstallmentType.Regular,
                DueDate = DateTime.UtcNow.AddMonths(1), Amount = 100_000m, Status = InstallmentStatus.Pending
            };
            var account = new FinanceAccount
            {
                Name = "Replay Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true
            };
            db.AddRange(project, unit, customer, booking, installment, account);
            await db.SaveChangesAsync();
            bookingId = booking.Id; installmentId = installment.Id; accountId = account.Id;
        }

        const string bookingAttempt = "booking-amount-payment:replayed-attempt";
        await using (var context = new AppDbContext(options))
        {
            var bookings = new BookingService(context, new CustomerService(context), new FinanceAccountService(context));
            var dto = new RecordBookingAmountPaymentDto
            {
                Amount = 40_000m, FinanceAccountId = accountId, PaymentMethod = PaymentMethod.Cash,
                PaidAt = DateTime.UtcNow, PaymentReference = "rp-booking"
            };
            await bookings.RecordBookingAmountPaymentAsync(bookingId, dto, 901, bookingAttempt, CancellationToken.None);
            await bookings.RecordBookingAmountPaymentAsync(bookingId, dto, 901, bookingAttempt, CancellationToken.None);
        }

        await using (var verify = new AppDbContext(options))
        {
            var deposits = await verify.Payments
                .Where(p => p.BookingId == bookingId && p.Type == PaymentType.BookingAmount).ToListAsync();
            var deposit = Assert.Single(deposits);
            Assert.Equal(40_000m, deposit.Amount);
            Assert.Equal(bookingAttempt, deposit.IdempotencyKey);
            // The booking's own running total is written by the same delegate, so a replay that
            // wrote one payment twice would have doubled this too.
            Assert.Equal(40_000m, (await verify.Bookings.SingleAsync(b => b.Id == bookingId)).BookingAmountReceived);
        }

        // Move it on to the stage installments can be collected in, without pretending a second
        // payment arrived: this test is about how many rows one attempt writes.
        await using (var advance = new AppDbContext(options))
        {
            var booking = await advance.Bookings.SingleAsync(b => b.Id == bookingId);
            booking.BookingAmountReceived = 100_000m;
            booking.Status = BookingStatus.PaymentPlanActive;
            await advance.SaveChangesAsync();
        }

        const string installmentAttempt = "installment-payment:replayed-attempt";
        await using (var context = new AppDbContext(options))
        {
            var installments = new InstallmentService(context, new FinanceAccountService(context));
            var dto = new RecordInstallmentPaymentDto
            {
                Amount = 40_000m, FinanceAccountId = accountId, PaymentMethod = PaymentMethod.Cash,
                PaidAt = DateTime.UtcNow, PaymentReference = "rp-installment"
            };
            await installments.RecordInstallmentPaymentAsync(bookingId, installmentId, dto, 901, installmentAttempt);
            var schedule = await installments.RecordInstallmentPaymentAsync(
                bookingId, installmentId, dto, 901, installmentAttempt);
            // The replay still answers with the truth about the schedule, not an error and not a
            // doubled figure — the caller cannot tell it was replayed, which is the point.
            Assert.Equal(40_000m, Assert.Single(schedule.Items).AmountPaid);
        }

        await using (var verify = new AppDbContext(options))
        {
            var collected = await verify.Payments
                .Where(p => p.BookingId == bookingId && p.Type == PaymentType.Installment).ToListAsync();
            var installmentPayment = Assert.Single(collected);
            Assert.Equal(40_000m, installmentPayment.Amount);
            Assert.Equal(installmentAttempt, installmentPayment.IdempotencyKey);
            Assert.Equal(InstallmentStatus.PartiallyPaid, (await verify.Installments.FindAsync(installmentId))!.Status);
            // Two receipts for one payment is the visible symptom operators would have chased.
            Assert.Equal(2, await verify.Payments.CountAsync(p => p.BookingId == bookingId && p.ReceiptNumber != null));
        }
    }

    /// <summary>
    /// Cancelling a booking releases the accrual of every commission it can SEE, inside its own
    /// Serializable transaction. Creating a commission reads the booking to check it is still
    /// active and then raises an expense and a payable on it — so unless both sides are serialized,
    /// a commission created alongside a cancellation is simply never seen by the release, and the
    /// cancelled sale carries a live commission expense and payable that nothing will ever clear.
    /// <para>
    /// Whichever side wins, the invariant is the same and is checked from the ledger itself: a
    /// commission on a cancelled booking owes nothing.
    /// </para>
    /// <para>
    /// End-to-end cover only. Like the terms-edit race, it does NOT prove the locking — the window
    /// is a few milliseconds wide and this passes with the transaction removed.
    /// <see cref="WritingCommissionsAndRebates_HappensInOneSerializableTransaction"/> is what pins that.
    /// </para>
    /// <para>
    /// The cancellation MUST happen for any of this to mean anything. An earlier version of this
    /// test omitted the booking's RowVersion, so every run was rejected with "The booking version is
    /// missing" and returned green having exercised nothing — a test that cannot fail is worse than
    /// no test, because it is counted as cover. Both outcomes are therefore inspected, a genuine
    /// concurrency rejection is retried against a refreshed version rather than accepted, and the
    /// final state is asserted unconditionally.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task ConcurrentCommissionCreationAndBookingCancellation_LeaveNothingAccruedOnACancelledSale()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int bookingId, partnerId;
        string bookingVersion;
        await using (var db = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "Cancel Race", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project, UnitNumber = "CR-01", UnitType = "Apartment",
                Price = 1_000_000m, Status = UnitStatus.Booked
            };
            var customer = new Customer { FullName = "Cancel Race Customer", Phone = "03001234567", Status = CustomerStatus.Active };
            var booking = new Booking
            {
                BookingReference = $"CR-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.AwaitingBookingAmount, AgreedSalePrice = 1_000_000m,
                BookingAmountRequired = 100_000m, BookingAmountReceived = 0m, BookingDate = DateTime.UtcNow
            };
            var partner = new ThirdPartyPartner
            {
                Name = "Cancel Race Broker", PartnerType = "Broker", InternalCode = "CRB-1", IsActive = true
            };
            db.AddRange(project, unit, customer, booking, partner);
            await db.SaveChangesAsync();
            bookingId = booking.Id; partnerId = partner.Id;
            // SQL Server fills RowVersion on insert, and cancellation refuses to run without it.
            bookingVersion = Convert.ToBase64String(booking.RowVersion);
        }

        async Task<Exception?> AddCommissionAsync()
        {
            try
            {
                await using var context = new AppDbContext(options);
                await new CommissionRebateService(context, new FinanceAccountService(context), new NullPrivateStorage())
                    .CreateCommissionAsync(bookingId, new CreateBookingCommissionDto
                    {
                        PartnerId = partnerId, IsManual = true,
                        ManualCalculationType = FinancialCalculationType.FixedAmount,
                        ManualCalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                        ManualFixedAmount = 50_000m, ManualReason = "Agreed with the broker"
                    }, Actor);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        async Task<Exception?> CancelAsync(string version, string key)
        {
            try
            {
                await using var context = new AppDbContext(options);
                var commissions = new CommissionRebateService(context, new FinanceAccountService(context), new NullPrivateStorage());
                await new BookingService(context, new CustomerService(context), new FinanceAccountService(context),
                        commissionLifecycle: commissions)
                    .CancelBookingAsync(bookingId, new CancelBookingDto
                    {
                        Reason = "Customer withdrew", RefundAmount = 0m, ExpectedCustomerCashReceived = 0m,
                        IdempotencyKey = key, ConcurrencyToken = version
                    }, Actor);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        var outcomes = await Task.WhenAll(AddCommissionAsync(), CancelAsync(bookingVersion, "cr-cancel"));
        var (creationError, cancellationError) = (outcomes[0], outcomes[1]);

        // Losing the race is a legitimate outcome for the CREATION — the booking really was cancelled
        // out from under it, and that is the message the operator should see. Any other failure means
        // the scenario did not run as written, so it is surfaced instead of being counted as a pass.
        if (creationError != null)
            Assert.True(creationError.Message.Contains("Cancelled bookings cannot", StringComparison.OrdinalIgnoreCase),
                $"The commission creation failed for an unexpected reason: {creationError}");

        // Losing the race is NOT an acceptable outcome for the cancellation: this test exists to
        // check what the cancellation leaves behind, so it has to actually happen. A version conflict
        // is the one rejection the race can legitimately produce, and the operator's answer to it is
        // to refresh and cancel again — so that is what happens here, once, before giving up.
        if (cancellationError != null)
        {
            bool alreadyCancelled;
            await using (var refresh = new AppDbContext(options))
            {
                var current = await refresh.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
                bookingVersion = Convert.ToBase64String(current.RowVersion);
                alreadyCancelled = current.Status == BookingStatus.Cancelled;
            }
            // Same key, because this is the same cancellation: a retry that reached a committed
            // attempt is answered from the idempotency record rather than cancelling twice.
            var retryError = alreadyCancelled ? null : await CancelAsync(bookingVersion, "cr-cancel");
            Assert.True(retryError == null,
                $"The booking could not be cancelled, so this test never reached the invariant it exists to check.{Environment.NewLine}"
                + $"First attempt: {cancellationError}{Environment.NewLine}Retry: {retryError}");
        }

        await using var verify = new AppDbContext(options);
        var booked = await verify.Bookings.SingleAsync(b => b.Id == bookingId);
        Assert.Equal(BookingStatus.Cancelled, booked.Status);

        // Whichever side committed first, the cancelled sale must end up owing this partner nothing.
        // If the creation won, the release saw it and closed it; if the cancellation won, the
        // creation was refused and there is nothing to close. A commission left OPEN here is the
        // exact defect: one that slipped past the release's read and kept its expense and payable.
        var commissionRows = await verify.BookingCommissions.Where(c => c.BookingId == bookingId).ToListAsync();
        if (creationError == null)
            Assert.NotEmpty(commissionRows);

        foreach (var commission in commissionRows)
        {
            var accrued = await verify.CommissionAccruals
                .Where(a => a.CommissionId == commission.Id).SumAsync(a => (decimal?)a.Amount) ?? 0m;
            Assert.True(accrued == 0m,
                $"Commission {commission.Id} on a cancelled booking still owes {accrued:0.00} — "
                + "it was created after the cancellation released everything it could see.");
            // Nothing was ever paid out here, so the release has no recovery to record: every
            // commission on this booking must be closed outright.
            Assert.True(commission.Status == BookingCommissionStatus.Cancelled,
                $"Commission {commission.Id} on a cancelled booking is still {commission.Status}.");
        }
    }

    /// <summary>
    /// A payment carrying more decimal places than the money columns hold must settle the same
    /// figure it banks.
    /// <para>
    /// The amount is stored in decimal(18,2) while the in-memory value keeps every place it was
    /// sent with, and the milestone was decided on the in-memory one. So 99,999.996 against a
    /// 100,000 installment banked 100,000.00 and then ruled the row only PARTLY paid, because
    /// 99,999.996 is not >= 100,000. The remaining balance is zero from that moment on, so no
    /// further payment can be taken: the installment — and the booking amount, on the other path —
    /// is stuck short of a target it has already been paid, and no operator action can free it.
    /// </para>
    /// <para>Only a real database can show this: the in-memory provider keeps all six places.</para>
    /// </summary>
    [SqlServerFact]
    public async Task PaymentsCarryingExtraDecimalPlaces_SettleTheAmountTheyBank()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int bookingId, installmentId, accountId;
        await using (var db = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "Rounding", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project, UnitNumber = "RD-01", UnitType = "Apartment",
                Price = 1_000_000m, Status = UnitStatus.Booked
            };
            var customer = new Customer { FullName = "Rounding Customer", Phone = "03008889999", Status = CustomerStatus.Active };
            var booking = new Booking
            {
                BookingReference = $"RD-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.AwaitingBookingAmount, AgreedSalePrice = 1_000_000m,
                BookingAmountRequired = 100_000m, BookingAmountReceived = 0m, BookingDate = DateTime.UtcNow
            };
            var installment = new Installment
            {
                Booking = booking, SequenceNumber = 1, Type = InstallmentType.Regular,
                DueDate = DateTime.UtcNow.AddMonths(1), Amount = 100_000m, Status = InstallmentStatus.Pending
            };
            var account = new FinanceAccount
            {
                Name = "Rounding Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true
            };
            db.AddRange(project, unit, customer, booking, installment, account);
            await db.SaveChangesAsync();
            bookingId = booking.Id; installmentId = installment.Id; accountId = account.Id;
        }

        // The booking amount, paid to the last hundredth of a paisa.
        await using (var context = new AppDbContext(options))
            await new BookingService(context, new CustomerService(context), new FinanceAccountService(context))
                .RecordBookingAmountPaymentAsync(bookingId, new RecordBookingAmountPaymentDto
                {
                    Amount = 99_999.996m, FinanceAccountId = accountId, PaymentMethod = PaymentMethod.Cash,
                    PaidAt = DateTime.UtcNow, PaymentReference = "rd-booking"
                }, adminUserId: 901);

        await using (var verify = new AppDbContext(options))
        {
            var booking = await verify.Bookings.SingleAsync(b => b.Id == bookingId);
            Assert.Equal(100_000m, booking.BookingAmountReceived);
            Assert.Equal(BookingStatus.PaymentPlanActive, booking.Status);
            Assert.Equal(100_000m, await verify.Payments
                .Where(p => p.BookingId == bookingId && p.Type == PaymentType.BookingAmount)
                .SumAsync(p => p.Amount));
        }

        await using (var context = new AppDbContext(options))
            await new InstallmentService(context, new FinanceAccountService(context))
                .RecordInstallmentPaymentAsync(bookingId, installmentId, new RecordInstallmentPaymentDto
                {
                    Amount = 99_999.996m, FinanceAccountId = accountId, PaymentMethod = PaymentMethod.Cash,
                    PaidAt = DateTime.UtcNow, PaymentReference = "rd-installment"
                }, adminUserId: 901);

        await using (var verify = new AppDbContext(options))
        {
            Assert.Equal(100_000m, await verify.Payments
                .Where(p => p.InstallmentId == installmentId).SumAsync(p => p.Amount));
            // The row banked its full amount, so it is settled — not left one thousandth short with
            // no way to collect the difference.
            Assert.Equal(InstallmentStatus.Paid, (await verify.Installments.FindAsync(installmentId))!.Status);
        }
    }

    /// <summary>
    /// A plan built from a possession amount carrying more decimal places than the column holds
    /// must still total exactly what the customer owes.
    /// <para>
    /// The possession row is stored rounded while the installment pool is worked out from the
    /// unrounded figure, so the rows together come to a paisa MORE than the balance: 100.005 is
    /// banked as 100.01 and the pool still gives away the other 0.005. Every payment is capped at
    /// the balance actually outstanding, so that last paisa can never be collected — the plan stays
    /// one payment short for ever on a sale the customer has paid in full, and the sale cannot be
    /// completed.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task APlanBuiltFromAnExtraDecimalPossessionAmount_TotalsExactlyWhatIsOwed()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int bookingId;
        await using (var db = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "Possession Rounding", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project, UnitNumber = "PR-01", UnitType = "Apartment",
                Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan
            };
            var customer = new Customer { FullName = "Possession Customer", Phone = "03003030303", Status = CustomerStatus.Active };
            var booking = new Booking
            {
                BookingReference = $"PR-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = 1_000_000m, DiscountAmount = 0m,
                BookingAmountRequired = 100_000m, BookingAmountReceived = 100_000m, BookingDate = DateTime.UtcNow
            };
            db.AddRange(project, unit, customer, booking);
            await db.SaveChangesAsync();
            bookingId = booking.Id;
        }

        await using (var context = new AppDbContext(options))
            await new InstallmentService(context, new FinanceAccountService(context))
                .GenerateScheduleAsync(bookingId, new GenerateInstallmentPlanDto
                {
                    AgreedSalePrice = 1_000_000m, DiscountPercent = 0m,
                    Frequency = InstallmentFrequency.Monthly, NumberOfInstallments = 1,
                    InstallmentStartDate = DateTime.UtcNow.AddMonths(1),
                    PossessionAmount = 100.005m, PossessionDueDate = DateTime.UtcNow.AddMonths(6)
                }, adminUserId: 901);

        await using var verify = new AppDbContext(options);
        // 1,000,000 net less the 100,000 already received: the plan may collect this and not a
        // paisa more, or the sale can never be finished.
        var owed = 900_000m;
        var rows = await verify.Installments.Where(i => i.BookingId == bookingId).ToListAsync();
        Assert.Equal(owed, rows.Sum(i => i.Amount));
        Assert.Equal(100.01m, Assert.Single(rows, i => i.Type == InstallmentType.Possession).Amount);
        Assert.Equal(owed, (await verify.Bookings.SingleAsync(b => b.Id == bookingId)).TotalInstallmentAmount);
    }

    /// <summary>
    /// Editing the booking's financial terms refuses a net sale price below what the customer has
    /// already settled in cash and rebate credits — otherwise the same concession is granted twice,
    /// once off the balance and again off the price, and the receivable goes negative on a sale
    /// nobody has paid off.
    /// <para>
    /// End-to-end cover for that guard while both sides run at once. It does NOT prove the locking:
    /// the damaging interleaving is a write skew a few milliseconds wide, and this passes with the
    /// transaction removed as readily as with it. <see cref="EditingBookingTerms_ReadsAndWritesInOneSerializableTransaction"/>
    /// is what pins that; this is here to catch the guard's own logic breaking.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task ConcurrentTermsEditAndCreditApplication_CannotPriceTheSaleBelowWhatIsCredited()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int bookingId, rebateId;
        string rebateToken;
        await using (var db = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "Terms Race", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project, UnitNumber = "TR-01", UnitType = "Apartment",
                Price = 1_000_000m, Status = UnitStatus.Booked
            };
            var customer = new Customer { FullName = "Terms Customer", Phone = "03009990000", Status = CustomerStatus.Active };
            var booking = new Booking
            {
                BookingReference = $"TR-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.AwaitingBookingAmount, AgreedSalePrice = 1_000_000m,
                BookingAmountRequired = 200_000m, BookingAmountReceived = 0m, BookingDate = DateTime.UtcNow
            };
            db.AddRange(project, unit, customer, booking);
            await db.SaveChangesAsync();
            bookingId = booking.Id;

            var rebates = new CommissionRebateService(db, new FinanceAccountService(db), new NullPrivateStorage());
            var workspace = await rebates.CreateRebateAsync(bookingId, new CreateCustomerRebateDto
            {
                CalculationType = FinancialCalculationType.FixedAmount,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                FixedAmount = 300_000m, Reason = "Loyalty credit", Method = CustomerRebateMethod.OutstandingBalanceReduction
            }, Actor);
            var rebate = Assert.Single(workspace.Rebates);
            rebateId = rebate.Id; rebateToken = rebate.ConcurrencyToken;
        }

        // Each side is legitimate against the state it can see: 300,000 of credit fits inside a
        // 1,000,000 sale, and a 250,000 sale is above the nothing settled so far. Together they
        // credit the customer more than the unit is being sold for.
        async Task<Exception?> RepriceAsync()
        {
            try
            {
                await using var context = new AppDbContext(options);
                await new BookingService(context, new CustomerService(context), new FinanceAccountService(context))
                    .UpdateBookingFinancialsAsync(bookingId, new UpdateBookingFinancialsDto
                    {
                        AgreedSalePrice = 250_000m, DiscountPercent = 0m, BookingAmountRequired = 100_000m,
                        BookingAmountDueDate = DateTime.UtcNow.AddMonths(1)
                    }, adminUserId: 901);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        async Task<Exception?> CreditAsync()
        {
            try
            {
                await using var context = new AppDbContext(options);
                await new CommissionRebateService(context, new FinanceAccountService(context), new NullPrivateStorage())
                    .RecordRebateDisbursementAsync(bookingId, rebateId, new RecordRebateDisbursementDto
                    {
                        Method = CustomerRebateMethod.OutstandingBalanceReduction, Amount = 300_000m,
                        AppliedAt = DateTime.UtcNow, IdempotencyKey = "tr-credit",
                        RebateConcurrencyToken = rebateToken
                    }, Actor);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        var outcomes = await Task.WhenAll(RepriceAsync(), CreditAsync());

        await using var verify = new AppDbContext(options);
        var finalBooking = await verify.Bookings.SingleAsync(b => b.Id == bookingId);
        var credits = await BookingCreditPolicy.GetNonCashCreditsAsync(verify, bookingId);
        var collected = await verify.Payments.Where(p => p.BookingId == bookingId)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        var netSalePrice = finalBooking.AgreedSalePrice - finalBooking.DiscountAmount;
        Assert.True(netSalePrice >= collected + credits,
            $"Net sale price {netSalePrice:0.00} is below the {collected + credits:0.00} already settled — "
            + "the price guard was bypassed by the race.");
        // Both cannot have succeeded, and the one that failed must have failed on a guard or a
        // serialization conflict, not on something unrelated.
        Assert.Contains(outcomes, error => error != null);
    }

    /// <summary>
    /// The terms edit reads what has already settled on the booking and then writes a price against
    /// that reading. Two statements at READ COMMITTED leave a window between them in which a rebate
    /// credit — applied under Serializable, like every other money path — can commit unseen, and the
    /// guard that exists to refuse a price below the credits never sees the credit it should have
    /// refused.
    /// <para>
    /// Asserted on the isolation level rather than by racing the two, because the window is a write
    /// skew a few milliseconds wide: a race test passes with the transaction removed and proves
    /// nothing. This fails the moment the wrapper goes.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task EditingBookingTerms_ReadsAndWritesInOneSerializableTransaction()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var recorder = new TransactionIsolationRecorder();
        var options = OptionsWith(database.ConnectionString, recorder);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        await using var context = new AppDbContext(options);
        var project = new Project { ProjectName = "Isolation", Location = "Karachi", CreatedById = 1 };
        var unit = new Unit
        {
            Project = project, UnitNumber = "IS-01", UnitType = "Apartment",
            Price = 1_000_000m, Status = UnitStatus.Booked
        };
        var customer = new Customer { FullName = "Isolation Customer", Phone = "03005556666", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = $"IS-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
            Status = BookingStatus.AwaitingBookingAmount, AgreedSalePrice = 1_000_000m,
            BookingAmountRequired = 200_000m, BookingAmountReceived = 0m, BookingDate = DateTime.UtcNow
        };
        context.AddRange(project, unit, customer, booking);
        await context.SaveChangesAsync();

        recorder.Started.Clear();
        await new BookingService(context, new CustomerService(context), new FinanceAccountService(context))
            .UpdateBookingFinancialsAsync(booking.Id, new UpdateBookingFinancialsDto
            {
                AgreedSalePrice = 800_000m, DiscountPercent = 0m, BookingAmountRequired = 150_000m,
                BookingAmountDueDate = DateTime.UtcNow.AddMonths(1)
            }, adminUserId: 901);

        Assert.Contains(System.Data.IsolationLevel.Serializable, recorder.Started);
        var updated = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(800_000m, updated.AgreedSalePrice);
    }

    /// <summary>
    /// Creating and editing a commission or a rebate decides something from the booking — is it
    /// still active, does this partner already hold one, what is it worth — and then writes money
    /// against that reading. Every other money-moving method on this service already runs
    /// Serializable; these did not, which is what let a commission be raised on a booking that was
    /// being cancelled at the same moment, its expense and payable surviving a release that could
    /// never have seen it.
    /// <para>
    /// Asserted on the isolation level because the race itself is a few milliseconds wide and a
    /// timing test passes with the transaction removed.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task WritingCommissionsAndRebates_HappensInOneSerializableTransaction()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var recorder = new TransactionIsolationRecorder();
        var options = OptionsWith(database.ConnectionString, recorder);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        await using var context = new AppDbContext(options);
        var project = new Project { ProjectName = "Isolated Writes", Location = "Karachi", CreatedById = 1 };
        var unit = new Unit
        {
            Project = project, UnitNumber = "IW-01", UnitType = "Apartment",
            Price = 1_000_000m, Status = UnitStatus.Booked
        };
        var customer = new Customer { FullName = "Isolated Customer", Phone = "03007070707", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = $"IW-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
            Status = BookingStatus.AwaitingBookingAmount, AgreedSalePrice = 1_000_000m,
            BookingAmountRequired = 100_000m, BookingAmountReceived = 0m, BookingDate = DateTime.UtcNow
        };
        var partner = new ThirdPartyPartner
        {
            Name = "Isolated Broker", PartnerType = "Broker", InternalCode = "IWB-1", IsActive = true
        };
        context.AddRange(project, unit, customer, booking, partner);
        await context.SaveChangesAsync();

        var service = new CommissionRebateService(context, new FinanceAccountService(context), new NullPrivateStorage());

        recorder.Started.Clear();
        var workspace = await service.CreateCommissionAsync(booking.Id, new CreateBookingCommissionDto
        {
            PartnerId = partner.Id, IsManual = true,
            ManualCalculationType = FinancialCalculationType.FixedAmount,
            ManualCalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            ManualFixedAmount = 50_000m, ManualReason = "Agreed with the broker"
        }, Actor);
        Assert.Contains(System.Data.IsolationLevel.Serializable, recorder.Started);
        var commission = Assert.Single(workspace.Commissions);

        recorder.Started.Clear();
        await service.UpdateCommissionAsync(booking.Id, commission.Id, new UpdateBookingCommissionDto
        {
            PartnerId = partner.Id, IsManual = true,
            ManualCalculationType = FinancialCalculationType.FixedAmount,
            ManualCalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            ManualFixedAmount = 60_000m, ManualReason = "Renegotiated",
            ConcurrencyToken = commission.ConcurrencyToken, ChangeReason = "Renegotiated"
        }, Actor);
        Assert.Contains(System.Data.IsolationLevel.Serializable, recorder.Started);

        recorder.Started.Clear();
        var withRebate = await service.CreateRebateAsync(booking.Id, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 20_000m, Reason = "Goodwill", Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        Assert.Contains(System.Data.IsolationLevel.Serializable, recorder.Started);
        var rebate = Assert.Single(withRebate.Rebates);

        recorder.Started.Clear();
        await service.UpdateRebateAsync(booking.Id, rebate.Id, new UpdateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 25_000m, Reason = "Goodwill", Method = CustomerRebateMethod.OutstandingBalanceReduction,
            ConcurrencyToken = rebate.ConcurrencyToken, ChangeReason = "Increased"
        }, Actor);
        Assert.Contains(System.Data.IsolationLevel.Serializable, recorder.Started);

        recorder.Started.Clear();
        await service.ChangeRebateStatusAsync(booking.Id, rebate.Id, new RebateStatusChangeDto
        {
            TargetStatus = CustomerRebateStatus.Cancelled, Reason = "Withdrawn",
            ConcurrencyToken = (await context.CustomerRebates.AsNoTracking()
                .SingleAsync(r => r.Id == rebate.Id)).RowVersion is { Length: > 0 } v
                ? Convert.ToBase64String(v) : null
        }, Actor);
        Assert.Contains(System.Data.IsolationLevel.Serializable, recorder.Started);
    }

    /// <summary>Every isolation level the code under test asked a transaction to start at.</summary>
    private sealed class TransactionIsolationRecorder : DbTransactionInterceptor
    {
        public List<System.Data.IsolationLevel> Started { get; } = [];

        public override InterceptionResult<DbTransaction> TransactionStarting(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
        {
            Started.Add(eventData.IsolationLevel);
            return base.TransactionStarting(connection, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            Started.Add(eventData.IsolationLevel);
            return base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }

    [SqlServerFact]
    public async Task FinanceMigrationAndReports_RunOnTheRealSqlServerProvider()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        int accountId;
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
            accountId = Convert.ToInt32(await command.ExecuteScalarAsync());
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

            var bankRow = trial.Rows.Single(row => row.AccountKey == $"A:{accountId}");
            var bankDetails = await finance.GetTrialBalanceDetailsAsync(
                bankRow.AccountKey, null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
            Assert.Equal("legacy-sql", Assert.Single(bankDetails.Rows).Reference);
            Assert.Equal(bankRow.Debit, bankDetails.ClosingBalance);
            Assert.Equal("Debit", bankDetails.ClosingBalanceType);

            var unclassifiedRow = trial.Rows.Single(row => row.AccountName == "Unclassified");
            var unclassifiedDetails = await finance.GetTrialBalanceDetailsAsync(
                unclassifiedRow.AccountKey, null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
            Assert.Equal("legacy-sql", Assert.Single(unclassifiedDetails.Rows).Reference);
            Assert.Equal(unclassifiedRow.Credit, unclassifiedDetails.ClosingBalance);
            Assert.Equal("Credit", unclassifiedDetails.ClosingBalanceType);

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
        var counter = new CommandCounter();
        var options = OptionsWith(database.ConnectionString, counter);
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
            Method = CustomerRebateMethod.CreditNote, Status = CustomerRebateStatus.Pending
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
        var measuredPnl = await MeasureAsync(counter, () => finance.GetProfitAndLossAsync(null, from, to));
        // Finance settings plus one UNION ALL aggregate for the current period and one for prior.
        // This used to be nineteen commands (settings + nine reads per period).
        Assert.Equal(3, measuredPnl.Commands);
        var pnl = measuredPnl.Result;
        Assert.Equal(dashboard.Summary.NetProfit, pnl.NetProfit);
        var projectPnl = await MeasureAsync(counter,
            () => finance.GetProfitAndLossAsync(project.Id, from, to));
        Assert.Equal(4, projectPnl.Commands); // project identity + settings + current + prior
        var measuredSheet = await MeasureAsync(counter, () => finance.GetBalanceSheetAsync(null, to));
        // Settings + seven snapshot commands + retained P&L + fixed-asset disclosure + allocations.
        // The snapshot portion alone previously required twenty-two commands.
        Assert.Equal(11, measuredSheet.Commands);
        var sheet = measuredSheet.Result;
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
        var loans = new LoanService(db, accounts, TestAttachments.Writer());
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
        CalculatedAmount = amount, FinalAmount = amount,
        Status = status,
        CreatedAt = DateTime.UtcNow, CreatedByName = "SQL seed"
    };

    /// <summary>
    /// Inserts a payment the way the schema of the day would have, naming only the columns that
    /// existed then.
    /// <para>
    /// A test that seeds a partially-migrated database through the EF model is seeding it through a
    /// model that has run AHEAD of that database, and it breaks the day any column is added to
    /// Payments — which is exactly what happened when the retry-attempt key arrived. Raw SQL is what
    /// keeps a legacy fixture pinned to the schema it is meant to represent.
    /// </para>
    /// </summary>
    private static Task SeedLegacyPaymentAsync(AppDbContext db, int bookingId, int? installmentId,
        int financeAccountId, decimal amount, PaymentType type, DateTime paidAt) =>
        db.Database.ExecuteSqlRawAsync("""
            INSERT INTO [Payments]
                ([BookingId], [InstallmentId], [FinanceAccountId], [Type], [Amount], [PaymentMethod],
                 [ReceiptNumber], [PaidAt], [CreatedAt])
            VALUES (@bookingId, @installmentId, @accountId, @type, @amount, @method, @receipt, @paidAt, @paidAt);
            """,
            new SqlParameter("@bookingId", bookingId),
            // Named parameters rather than positional: a NULL installment has no CLR type for EF to
            // infer a store mapping from, and DBNull cannot be passed as a bare positional value.
            new SqlParameter("@installmentId", (object?)installmentId ?? DBNull.Value),
            new SqlParameter("@accountId", financeAccountId),
            new SqlParameter("@type", (int)type),
            new SqlParameter("@amount", amount),
            new SqlParameter("@method", (int)PaymentMethod.BankTransfer),
            new SqlParameter("@receipt", $"BF-{Guid.NewGuid():N}"[..12]),
            new SqlParameter("@paidAt", paidAt));

    /// <summary>
    /// The rebate-allocation backfill, run against a booking that already existed when the migration
    /// arrived. It only does work on real SQL Server — it is a T-SQL cursor, so no in-memory test can
    /// reach it — and it only does work on legacy rows, so the other migration tests here (which
    /// migrate an empty schema) never execute a single line of it.
    /// <para>
    /// The shape is chosen to catch the way an ordered fill fails quietly: the LAST installment is
    /// already fully paid, so it has no room. Numbering the installments and then discarding the
    /// empty ones leaves ordinal 1 missing, and a loop that walks ordinals in order stops dead at
    /// the gap — placing nothing at all, on a booking whose earlier installments had ample room. The
    /// migration succeeds, and the schedule quietly goes on demanding money the rebate already
    /// settled. Filtering before numbering is what keeps the ordinals dense.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task TheRebateAllocationBackfill_PlacesLegacyCredits_EvenWhenTheLastInstallmentIsAlreadyPaid()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);

        // The migration immediately before the allocation table: everything the legacy booking needs
        // exists, the allocations do not.
        await using (var db = new AppDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync("20260823225954_AddMovementAttachments");

        int bookingId, firstId, secondId, thirdId, spillBookingId;
        int[] spillIds;
        await using (var db = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "Backfill", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project, UnitNumber = "BF-01", UnitType = "Apartment",
                Price = 10_000_000m, Status = UnitStatus.OnPaymentPlan
            };
            var customer = new Customer { FullName = "Backfill Buyer", Phone = "03007777777", Status = CustomerStatus.Active };
            var account = new FinanceAccount
            {
                Name = "Backfill Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true
            };
            // 10,000,000 sale, 4,000,000 taken as the booking amount, 6,000,000 scheduled over three.
            var booking = new Booking
            {
                BookingReference = $"BF-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = 10_000_000m, DiscountAmount = 0m,
                BookingAmountRequired = 4_000_000m, BookingAmountReceived = 4_000_000m,
                BookingDate = new DateTime(2026, 1, 15)
            };
            db.AddRange(project, unit, customer, account, booking);
            await db.SaveChangesAsync();
            bookingId = booking.Id;

            Installment Row(int sequence, DateTime due) => new()
            {
                BookingId = bookingId, SequenceNumber = sequence, DueDate = due, Type = InstallmentType.Regular,
                Amount = 2_000_000m, Status = InstallmentStatus.Pending
            };
            var first = Row(1, new DateTime(2026, 2, 15));
            var second = Row(2, new DateTime(2026, 3, 15));
            var third = Row(3, new DateTime(2026, 4, 15));
            db.Installments.AddRange(first, second, third);
            await db.SaveChangesAsync();
            firstId = first.Id; secondId = second.Id; thirdId = third.Id;
            await SeedLegacyPaymentAsync(db, bookingId, null, account.Id, 4_000_000m,
                PaymentType.BookingAmount, new DateTime(2026, 1, 15));

            // The LAST installment is settled in full — the row that leaves a hole in the numbering.
            third.Status = InstallmentStatus.Paid;
            third.PaidAt = new DateTime(2026, 4, 15);
            await SeedLegacyPaymentAsync(db, bookingId, thirdId, account.Id, 2_000_000m,
                PaymentType.Installment, new DateTime(2026, 4, 15));

            // A 1,000,000 balance reduction the plan was never shrunk by: it names no installment,
            // and before the allocation table there was nowhere for it to land.
            var rebate = Rebate(bookingId, customer.Id, CustomerRebateStatus.Applied, 1_000_000m);
            db.CustomerRebates.Add(rebate);
            await db.SaveChangesAsync();
            db.RebateDisbursements.Add(new RebateDisbursement
            {
                RebateId = rebate.Id, Method = CustomerRebateMethod.OutstandingBalanceReduction,
                Amount = 1_000_000m, AppliedAt = new DateTime(2026, 5, 1),
                IdempotencyKey = $"bf-{Guid.NewGuid():N}", RecordedByName = "Backfill seed"
            });
            await db.SaveChangesAsync();

            // A SECOND booking, because the backfill walks a cursor and resets its counters per
            // booking. If that reset did not happen, everything after the first booking would be
            // skipped in silence — the same failure as the ordinal gap, one level up. This one also
            // takes a credit bigger than its last installment, so the spill onto the installment
            // before it is covered too.
            var secondUnit = new Unit
            {
                Project = project, UnitNumber = "BF-02", UnitType = "Apartment",
                Price = 6_000_000m, Status = UnitStatus.OnPaymentPlan
            };
            var secondCustomer = new Customer { FullName = "Spill Buyer", Phone = "03006666666", Status = CustomerStatus.Active };
            var spill = new Booking
            {
                BookingReference = $"BF-{Guid.NewGuid():N}", Customer = secondCustomer, Unit = secondUnit,
                Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = 6_000_000m, DiscountAmount = 0m,
                BookingAmountRequired = 0m, BookingAmountReceived = 0m, BookingDate = new DateTime(2026, 1, 15)
            };
            db.AddRange(secondUnit, secondCustomer, spill);
            await db.SaveChangesAsync();
            spillBookingId = spill.Id;

            var spillRows = Enumerable.Range(1, 3).Select(n => new Installment
            {
                BookingId = spillBookingId, SequenceNumber = n, DueDate = new DateTime(2026, 1 + n, 20),
                Type = InstallmentType.Regular, Amount = 2_000_000m, Status = InstallmentStatus.Pending
            }).ToList();
            db.Installments.AddRange(spillRows);
            var spillRebate = Rebate(spillBookingId, secondCustomer.Id, CustomerRebateStatus.Applied, 3_000_000m);
            db.CustomerRebates.Add(spillRebate);
            await db.SaveChangesAsync();
            spillIds = spillRows.Select(i => i.Id).ToArray();
            db.RebateDisbursements.Add(new RebateDisbursement
            {
                RebateId = spillRebate.Id, Method = CustomerRebateMethod.CreditNote,
                Amount = 3_000_000m, AppliedAt = new DateTime(2026, 5, 2), Reference = "CN-SPILL",
                IdempotencyKey = $"bf-{Guid.NewGuid():N}", RecordedByName = "Backfill seed"
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        await using (var db = new AppDbContext(options))
        {
            var allocations = await db.RebateCreditAllocations.AsNoTracking()
                .Where(a => a.Disbursement.Rebate.BookingId == bookingId)
                .ToListAsync();

            // The whole placeable credit is placed — 6,000,000 scheduled + 4,000,000 received +
            // 1,000,000 credit − 10,000,000 net — and it lands on the latest installment that still
            // has room, which is the second, not the paid third.
            Assert.Equal(1_000_000m, allocations.Sum(a => a.Amount));
            var allocation = Assert.Single(allocations);
            Assert.Equal(secondId, allocation.InstallmentId);

            var installments = await db.Installments.AsNoTracking()
                .Where(i => i.BookingId == bookingId).ToListAsync();
            // Half covered by the credit, so it reads partially paid rather than still wholly due.
            Assert.Equal(InstallmentStatus.PartiallyPaid, installments.Single(i => i.Id == secondId).Status);
            Assert.Equal(InstallmentStatus.Pending, installments.Single(i => i.Id == firstId).Status);
            Assert.Equal(InstallmentStatus.Paid, installments.Single(i => i.Id == thirdId).Status);

            // The second booking was reached, and its 3,000,000 filled the last installment then
            // spilled onto the one before it — latest first, never more than the credit.
            var spillAllocations = await db.RebateCreditAllocations.AsNoTracking()
                .Where(a => a.Disbursement.Rebate.BookingId == spillBookingId)
                .ToListAsync();
            Assert.Equal(3_000_000m, spillAllocations.Sum(a => a.Amount));
            Assert.Equal(2_000_000m, spillAllocations.Single(a => a.InstallmentId == spillIds[2]).Amount);
            Assert.Equal(1_000_000m, spillAllocations.Single(a => a.InstallmentId == spillIds[1]).Amount);
            Assert.DoesNotContain(spillAllocations, a => a.InstallmentId == spillIds[0]);

            var spillInstallments = await db.Installments.AsNoTracking()
                .Where(i => i.BookingId == spillBookingId).ToListAsync();
            Assert.Equal(InstallmentStatus.Paid, spillInstallments.Single(i => i.Id == spillIds[2]).Status);
            Assert.Equal(InstallmentStatus.PartiallyPaid, spillInstallments.Single(i => i.Id == spillIds[1]).Status);
            Assert.Equal(InstallmentStatus.Pending, spillInstallments.Single(i => i.Id == spillIds[0]).Status);
        }
    }

    private static CustomerRebate Rebate(int bookingId, int customerId, CustomerRebateStatus status,
        decimal amount) => new()
    {
        BookingId = bookingId, CustomerId = customerId, CalculationType = FinancialCalculationType.FixedAmount,
        FixedAmount = amount, CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
        BasisAmount = 1_000_000m, CalculatedAmount = amount, FinalAmount = amount,
        Reason = "SQL invariant seed", Method = CustomerRebateMethod.OutstandingBalanceReduction,
        Status = status, CreatedAt = DateTime.UtcNow, CreatedByName = "SQL seed"
    };

    /// <summary>
    /// The held-enquiry table on real SQL Server: its filtered unique index (which the in-memory
    /// provider ignores) keeps one hold per provider submission while allowing any number without
    /// an id, and its row version stops two administrators both resolving the same hold.
    /// </summary>
    [SqlServerFact]
    public async Task HeldEnquiries_AreUniquePerSubmission_AndResolvedOnlyOnce_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        int leadA, leadB, holdId;
        var admin = new LeadUserContext { UserId = 1, Role = LeadRoles.Admin, DisplayName = "SQL admin" };

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            using var dispatcher = SqlLeadDispatcher(db);
            var leads = SqlLeadService(db, dispatcher);
            leadA = (await leads.IngestAsync(new LeadIntakeDto
            {
                FirstName = "Person A", Phone = "0300-1234567", Email = "a@example.com", SourceCode = "walk_in"
            }, admin)).Lead!.Id;
            leadB = (await leads.IngestAsync(new LeadIntakeDto
            {
                FirstName = "Person B", Phone = "0321-7654321", Email = "b@example.com", SourceCode = "walk_in"
            }, admin)).Lead!.Id;

            var conflicting = new LeadIntakeDto
            {
                FirstName = "Who Is This", Phone = "0300-1234567", Email = "b@example.com", SourceCode = "facebook",
                ExternalProvider = "meta", ExternalLeadId = "sql-conflict", AllowDuplicate = true, Notes = "Held on SQL."
            };
            var held = await leads.IngestAsync(conflicting, actor: null, trustedExternal: true);
            var replay = await leads.IngestAsync(conflicting, actor: null, trustedExternal: true);
            Assert.True(held.HeldForReview);
            Assert.Equal(held.HoldId, replay.HoldId);
            holdId = held.HoldId!.Value;

            var listed = Assert.Single((await leads.GetIntakeHoldsAsync(admin)).Items);
            Assert.Equal(new[] { leadA, leadB }.OrderBy(x => x), listed.Candidates.Select(c => c.LeadId).OrderBy(x => x));
        }

        await using (var db = new AppDbContext(options))
        {
            db.LeadIntakeHolds.Add(new LeadIntakeHold
            {
                Provider = "meta", ExternalLeadId = "sql-conflict", PayloadJson = "{}", CandidateLeadIds = $"{leadA},{leadB}"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = new AppDbContext(options))
        {
            db.LeadIntakeHolds.AddRange(
                new LeadIntakeHold { PayloadJson = "{}", CandidateLeadIds = $"{leadA},{leadB}" },
                new LeadIntakeHold { PayloadJson = "{}", CandidateLeadIds = $"{leadA},{leadB}" });
            await db.SaveChangesAsync();
        }

        // Two administrators open the same hold; the second decision must not also land. The
        // second context already holds the hold as it was, just as a request that loaded it
        // before the first decision committed would.
        await using var first = new AppDbContext(options);
        await using var second = new AppDbContext(options);
        using var firstDispatcher = SqlLeadDispatcher(first);
        using var secondDispatcher = SqlLeadDispatcher(second);
        await second.LeadIntakeHolds.SingleAsync(h => h.Id == holdId);

        await SqlLeadService(first, firstDispatcher).ResolveIntakeHoldAsync(
            holdId, new ResolveLeadIntakeHoldDto { LeadId = leadA }, admin);

        var lost = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SqlLeadService(second, secondDispatcher).ResolveIntakeHoldAsync(
                holdId, new ResolveLeadIntakeHoldDto { LeadId = leadB }, admin));
        Assert.Contains("Someone else dealt with this enquiry", lost.Message);

        await using var verify = new AppDbContext(options);
        var resolved = await verify.LeadIntakeHolds.SingleAsync(h => h.Id == holdId);
        Assert.Equal((LeadIntakeHoldStatus.Resolved, (int?)leadA), (resolved.Status, resolved.ResolvedLeadId));
        Assert.Contains("Held on SQL.", (await verify.Leads.SingleAsync(l => l.Id == leadA)).Notes);
        Assert.DoesNotContain("Held on SQL.", (await verify.Leads.SingleAsync(l => l.Id == leadB)).Notes ?? "");
        Assert.Equal(leadA, (await verify.LeadExternalSubmissions.SingleAsync(s => s.ExternalLeadId == "sql-conflict")).LeadId);
    }

    // The migration immediately before resolved holds' webhook events were linked to their leads.
    private const string BeforeHoldEventLinks = "20260925165441_AddLeadCommunicationConnected";

    /// <summary>
    /// A held Meta enquiry's webhook event reaches the lead it was resolved into: the migration
    /// backfills enquiries resolved before, resolving now links as it goes, and the lead reads the
    /// event back — the key's suffix match translated by SQL Server, not evaluated in memory, and
    /// never confusing one Meta lead id with a longer one ending in it.
    /// </summary>
    [SqlServerFact]
    public async Task ResolvedHeldEnquiries_ReachTheirWebhookEvents_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        var admin = new LeadUserContext { UserId = 1, Role = LeadRoles.Admin, DisplayName = "SQL admin" };
        int leadA, leadB;

        await using (var db = new AppDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(BeforeHoldEventLinks);
            using var dispatcher = SqlLeadDispatcher(db);
            var leads = SqlLeadService(db, dispatcher);
            leadA = (await leads.IngestAsync(new LeadIntakeDto
            {
                FirstName = "Person A", Phone = "0300-1234567", Email = "a@example.com", SourceCode = "walk_in"
            }, admin)).Lead!.Id;
            leadB = (await leads.IngestAsync(new LeadIntakeDto
            {
                FirstName = "Person B", Phone = "0321-7654321", Email = "b@example.com", SourceCode = "walk_in"
            }, admin)).Lead!.Id;

            foreach (var leadgenId in new[] { "sql-held-1", "sql-held-11" })
            {
                Assert.True((await leads.IngestAsync(new LeadIntakeDto
                {
                    FirstName = "Who Is This", Phone = "0300-1234567", Email = "b@example.com", SourceCode = "facebook",
                    ExternalProvider = "meta", ExternalLeadId = leadgenId, AllowDuplicate = true
                }, actor: null, trustedExternal: true)).HeldForReview);
                // Held, as the processor leaves it: done, with no lead.
                db.ExternalIntegrationEvents.Add(new ExternalIntegrationEvent
                {
                    Provider = "meta", EventType = "leadgen", EventKey = $"0:page-1:{leadgenId}",
                    RawPayloadJson = $"{{\"leadgen_id\":\"{leadgenId}\"}}",
                    Status = ExternalIntegrationEventStatus.Processed, ProcessedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }

        // Resolved before this change: the hold records its lead, the event does not.
        await using (var db = new AppDbContext(options))
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [LeadIntakeHolds] SET [Status] = 1, [ResolvedLeadId] = {0}, [ResolvedAt] = SYSUTCDATETIME() WHERE [ExternalLeadId] = 'sql-held-1'",
                leadA);

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var events = await db.ExternalIntegrationEvents.AsNoTracking().ToListAsync();
            Assert.Equal(leadA, events.Single(e => e.EventKey.EndsWith(":sql-held-1")).LeadId);
            Assert.Null(events.Single(e => e.EventKey.EndsWith(":sql-held-11")).LeadId);
        }

        await using (var db = new AppDbContext(options))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            var leads = SqlLeadService(db, dispatcher);
            var holdId = (await db.LeadIntakeHolds.SingleAsync(h => h.ExternalLeadId == "sql-held-11")).Id;
            await leads.ResolveIntakeHoldAsync(holdId, new ResolveLeadIntakeHoldDto { LeadId = leadB }, admin);

            var submission = await db.LeadExternalSubmissions.AsNoTracking().SingleAsync(s => s.ExternalLeadId == "sql-held-11");
            var raw = await leads.GetExternalSubmissionRawAsync(leadB, submission.Id, admin);
            Assert.Equal("{\"leadgen_id\":\"sql-held-11\"}", raw.Event!.RawPayloadJson);
        }

        await using var verify = new AppDbContext(options);
        var linked = await verify.ExternalIntegrationEvents.AsNoTracking().ToListAsync();
        Assert.Equal(leadA, linked.Single(e => e.EventKey.EndsWith(":sql-held-1")).LeadId);
        Assert.Equal(leadB, linked.Single(e => e.EventKey.EndsWith(":sql-held-11")).LeadId);
    }

    /// <summary>
    /// An administrator rejects a website request at the very moment another resolves its held
    /// enquiry. The resolution wins; the rejection must be refused with a clear message, not a 500.
    /// </summary>
    [SqlServerFact]
    public async Task RejectingARequestWhileItsHoldIsResolved_IsRefusedClearly_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        LeadUserContext admin;
        int leadA, requestId, holdId;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var adminUser = new User
            {
                RoleId = 1, FullName = "SQL admin", Email = "held-race-admin@dams.test",
                Password = "test-hash", AccountStatus = UserAccountStatus.Active
            };
            db.Users.Add(adminUser);
            await db.SaveChangesAsync();
            admin = new LeadUserContext { UserId = adminUser.UserId, Role = LeadRoles.Admin, DisplayName = "SQL admin" };
            var unit = new Unit
            {
                Project = new Project { ProjectName = "Held race", Location = "Karachi", CreatedById = 1 },
                UnitNumber = "HR-01", UnitType = "Apartment", Price = 1_000_000m, Status = UnitStatus.Available
            };
            db.Units.Add(unit);
            await db.SaveChangesAsync();

            using var dispatcher = SqlLeadDispatcher(db);
            var leads = SqlLeadService(db, dispatcher);
            leadA = (await leads.IngestAsync(new LeadIntakeDto
            {
                FirstName = "Person A", Phone = "0300-1234567", Email = "a@example.com", SourceCode = "walk_in"
            }, admin)).Lead!.Id;
            await leads.IngestAsync(new LeadIntakeDto
            {
                FirstName = "Person B", Phone = "0321-7654321", Email = "b@example.com", SourceCode = "walk_in"
            }, admin);

            requestId = (await new BookingRequestService(db, leads).CreateBookingRequestAsync(new CreateBookingRequestDto
            {
                UnitId = unit.Id, FullName = "Website Enquirer", Phone = "03001234567",
                Email = "b@example.com", CNIC = "42101-1111111-1", Address = "Karachi"
            }, userId: null)).Id;
            holdId = (await db.LeadIntakeHolds.SingleAsync()).Id;
        }

        var race = new RunBeforeSavingInterceptor(
            context => context.ChangeTracker.Entries<LeadIntakeHold>().Any(e => e.State == EntityState.Modified),
            async () =>
            {
                await using var other = new AppDbContext(options);
                using var dispatcher = SqlLeadDispatcher(other);
                await SqlLeadService(other, dispatcher).ResolveIntakeHoldAsync(
                    holdId, new ResolveLeadIntakeHoldDto { LeadId = leadA }, admin);
            });

        await using (var db = new AppDbContext(Options(database.ConnectionString, race)))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new BookingRequestService(db, SqlLeadService(db, dispatcher))
                    .RejectBookingRequestAsync(requestId, admin.UserId, "Unit taken."));
            Assert.Contains("dealt with this request's held enquiry at the same moment", refused.Message);
        }

        await using var verify = new AppDbContext(options);
        var request = await verify.BookingRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal((BookingRequestStatus.Pending, (int?)leadA), (request.Status, request.LeadId));
        Assert.Equal(LeadIntakeHoldStatus.Resolved, (await verify.LeadIntakeHolds.SingleAsync()).Status);
    }

    private static LeadIntakeDto ConcurrentEnquiry(string externalId, string? phone = null, string? whatsapp = null) => new()
    {
        FirstName = "Same Person", Phone = phone, WhatsappNumber = whatsapp, SourceCode = "facebook",
        ExternalProvider = "meta", ExternalLeadId = externalId, AllowDuplicate = true,
        Notes = $"Enquiry {externalId}."
    };

    /// <summary>
    /// Two different submissions for one person, each on its own connection. The second starts
    /// at the worst moment: after the first has checked for an existing lead and is saving its
    /// new one. Without the intake locks it would see no lead yet and create a second.
    /// </summary>
    [SqlServerFact]
    public async Task ASecondEnquiryArrivingWhileTheFirstIsSaving_JoinsItsLead_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        async Task<LeadIntakeResultDto> IngestOnItsOwnConnectionAsync(LeadIntakeDto dto, SaveChangesInterceptor? interceptor = null)
        {
            await using var db = new AppDbContext(Options(database.ConnectionString, interceptor));
            using var dispatcher = SqlLeadDispatcher(db);
            return await SqlLeadService(db, dispatcher).IngestAsync(dto, actor: null, trustedExternal: true);
        }

        Task<LeadIntakeResultDto>? second = null;
        var race = new RunBeforeSavingInterceptor(
            context => context.ChangeTracker.Entries<Lead>().Any(e => e.State == EntityState.Added),
            async () =>
            {
                // WhatsApp this time: the same number must be the same person in either field.
                second = IngestOnItsOwnConnectionAsync(ConcurrentEnquiry("race-2", whatsapp: "+92 300 1234567"));
                // The second must be seen actually waiting on the contact lock, and still not done,
                // before this one commits. A second that merely started late would run after the
                // commit and pass without any lock, so waiting for a while proves nothing.
                Assert.True(await SomeoneIsWaitingOnAContactLockAsync(database.ConnectionString, second, TimeSpan.FromSeconds(20)),
                    "The second enquiry never waited on the contact lock.");
                Assert.False(second.IsCompleted);
            });

        var first = await IngestOnItsOwnConnectionAsync(ConcurrentEnquiry("race-1", phone: "0300-1234567"), race);
        var joined = await second!;

        Assert.NotNull(first.Lead);
        Assert.True(joined.EnrichedExisting);
        Assert.Equal(first.Lead!.Id, joined.Lead!.Id);

        await using var verify = new AppDbContext(options);
        var lead = await verify.Leads.SingleAsync();
        Assert.Contains("Enquiry race-2.", lead.Notes);
        Assert.Equal(new[] { "race-1", "race-2" },
            await verify.LeadExternalSubmissions.Where(x => x.LeadId == lead.Id)
                .OrderBy(x => x.ExternalLeadId).Select(x => x.ExternalLeadId).ToArrayAsync());
    }

    /// <summary>Many distinct submissions for one person at once, on independent connections:
    /// one lead, every submission kept, none lost.</summary>
    [SqlServerFact]
    public async Task ManyConcurrentEnquiriesForOnePerson_ProduceOneLead_AndKeepEverySubmission_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        const int enquiries = 8;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(1, enquiries).Select(i => Task.Run(async () =>
        {
            await start.Task;
            await using var db = new AppDbContext(options);
            using var dispatcher = SqlLeadDispatcher(db);
            // Half arrive with the number as a phone, half as WhatsApp.
            var dto = i % 2 == 0
                ? ConcurrentEnquiry($"burst-{i}", phone: "0300-7654321")
                : ConcurrentEnquiry($"burst-{i}", whatsapp: "0300 7654321");
            return await SqlLeadService(db, dispatcher).IngestAsync(dto, actor: null, trustedExternal: true);
        })).ToList();

        start.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.NotNull(r.Lead));
        Assert.Single(results.Select(r => r.Lead!.Id).Distinct());
        Assert.Equal(enquiries - 1, results.Count(r => r.EnrichedExisting));

        await using var verify = new AppDbContext(options);
        Assert.Equal(1, await verify.Leads.CountAsync());
        Assert.Equal(enquiries, await verify.LeadExternalSubmissions.CountAsync());
    }

    private static async Task<LeadIntakeResultDto[]> IngestTogetherAsync(DbContextOptions<AppDbContext> options, params LeadIntakeDto[] enquiries)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = enquiries.Select(dto => Task.Run(async () =>
        {
            await start.Task;
            await using var db = new AppDbContext(options);
            using var dispatcher = SqlLeadDispatcher(db);
            return await SqlLeadService(db, dispatcher).IngestAsync(dto, actor: null, trustedExternal: true);
        })).ToList();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    [SqlServerFact]
    public async Task ConcurrentEnquiriesSharingOnlyAnEmail_ProduceOneLead_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var results = await IngestTogetherAsync(options, Enumerable.Range(1, 6).Select(i => new LeadIntakeDto
        {
            FirstName = "Email Only", Email = "shared@example.com", SourceCode = "facebook",
            ExternalProvider = "meta", ExternalLeadId = $"email-{i}", AllowDuplicate = true
        }).ToArray());

        Assert.Single(results.Select(r => r.Lead!.Id).Distinct());
        await using var verify = new AppDbContext(options);
        Assert.Equal(1, await verify.Leads.CountAsync());
        Assert.Equal(6, await verify.LeadExternalSubmissions.CountAsync());
    }

    /// <summary>
    /// A carries phone P and email E, B only E, C only P — several locks at once, overlapping in
    /// different ways. Whatever order they land in, the outcome must be one a sequential run could
    /// give: no two open leads share a number or an email, and every submission is kept, either
    /// as a receipt on a lead or as a hold for an administrator.
    /// </summary>
    [SqlServerFact]
    public async Task ThreeOverlappingEnquiriesAtOnce_NeverLeaveTwoOpenLeadsForOneContact_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        for (var round = 1; round <= 5; round++)
        {
            var phone = $"0300-55500{round:00}";
            var email = $"round{round}@example.com";
            LeadIntakeDto Enquiry(string id, string? p, string? e) => new()
            {
                FirstName = "Overlap", Phone = p, Email = e, SourceCode = "facebook",
                ExternalProvider = "meta", ExternalLeadId = $"{id}-{round}", AllowDuplicate = true
            };

            await IngestTogetherAsync(options, Enquiry("a", phone, email), Enquiry("b", null, email), Enquiry("c", phone, null));

            await using var verify = new AppDbContext(options);
            var normalizedPhone = LeadContactNormalizer.NormalizeUsablePhoneOrNull(phone);
            var open = await verify.Leads
                .Where(l => !LeadStageRules.ClosedStages.Contains(l.Stage)
                            && (l.NormalizedPhone == normalizedPhone || l.NormalizedEmail == email))
                .Select(l => new { l.NormalizedPhone, l.NormalizedEmail })
                .ToListAsync();
            Assert.True(open.Count(l => l.NormalizedPhone == normalizedPhone) <= 1, $"Round {round}: two open leads share the phone.");
            Assert.True(open.Count(l => l.NormalizedEmail == email) <= 1, $"Round {round}: two open leads share the email.");

            var ids = new[] { $"a-{round}", $"b-{round}", $"c-{round}" };
            var kept = await verify.LeadExternalSubmissions.CountAsync(x => ids.Contains(x.ExternalLeadId))
                       + await verify.LeadIntakeHolds.CountAsync(x => ids.Contains(x.ExternalLeadId!));
            Assert.Equal(3, kept);
        }
    }

    /// <summary>
    /// Lead L has phone P and email E. One enquiry matches it by E and another by P: different
    /// contact locks, same lead. While the first is saving its enrichment the second must wait,
    /// then enrich L after it — not update L at the same time and fail on its row version.
    /// </summary>
    [SqlServerFact]
    public async Task TwoEnquiriesMatchingOneLeadByDifferentDetails_EnrichItInTurn_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        int leadId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            using var dispatcher = SqlLeadDispatcher(db);
            leadId = (await SqlLeadService(db, dispatcher).IngestAsync(new LeadIntakeDto
            {
                FirstName = "Both Details", Phone = "0300-1234567", Email = "both@example.com", SourceCode = "facebook",
                ExternalProvider = "meta", ExternalLeadId = "turn-0", AllowDuplicate = true
            }, actor: null, trustedExternal: true)).Lead!.Id;
        }

        Task<LeadIntakeResultDto>? byPhone = null;
        var race = new RunBeforeSavingInterceptor(
            context => context.ChangeTracker.Entries<Lead>().Any(e => e.State == EntityState.Modified),
            async () =>
            {
                byPhone = Task.Run(async () =>
                {
                    await using var db = new AppDbContext(options);
                    using var dispatcher = SqlLeadDispatcher(db);
                    return await SqlLeadService(db, dispatcher).IngestAsync(
                        ConcurrentEnquiry("turn-phone", phone: "0300-1234567"), actor: null, trustedExternal: true);
                });
                Assert.True(await SomeoneIsWaitingOnAContactLockAsync(database.ConnectionString, byPhone, TimeSpan.FromSeconds(20)),
                    "The second enrichment never waited for the first.");
            });

        LeadIntakeResultDto byEmail;
        await using (var db = new AppDbContext(Options(database.ConnectionString, race)))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            byEmail = await SqlLeadService(db, dispatcher).IngestAsync(new LeadIntakeDto
            {
                FirstName = "Same Person", Email = "both@example.com", SourceCode = "facebook",
                ExternalProvider = "meta", ExternalLeadId = "turn-email", AllowDuplicate = true
            }, actor: null, trustedExternal: true);
        }
        var phoneResult = await byPhone!;

        Assert.Equal((leadId, leadId), (byEmail.Lead!.Id, phoneResult.Lead!.Id));
        await using var verify = new AppDbContext(options);
        Assert.Equal(1, await verify.Leads.CountAsync());
        Assert.Equal(3, await verify.LeadExternalSubmissions.CountAsync(x => x.LeadId == leadId));
    }

    [SqlServerFact]
    public async Task AContactLockHeldTooLong_RefusesAsBusy_AndWritesNothing_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        // Someone else holds the number's lock and does not let go.
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var held = (SqlTransaction)await holder.BeginTransactionAsync();
        await using (var take = new SqlCommand(
                         "EXEC sp_getapplock @Resource = @r, @LockMode = 'Exclusive', @LockOwner = 'Transaction';",
                         holder, held))
        {
            take.Parameters.AddWithValue("@r",
                LeadService.ContactLockName("number", LeadContactNormalizer.NormalizeUsablePhoneOrNull("0300-1234567")!));
            await take.ExecuteNonQueryAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            await Assert.ThrowsAsync<LeadIntakeBusyException>(() =>
                SqlLeadService(db, dispatcher, lockTimeoutMilliseconds: 300)
                    .IngestAsync(ConcurrentEnquiry("busy-1", phone: "0300-1234567"), actor: null, trustedExternal: true));
        }

        await held.RollbackAsync();
        await using var verify = new AppDbContext(options);
        Assert.Equal(0, await verify.Leads.CountAsync());
        Assert.Equal(0, await verify.LeadExternalSubmissions.CountAsync());
    }

    /// <summary>
    /// An edit giving lead X a number while an enquiry for that number is creating its lead. The
    /// edit must wait for the enquiry and then be refused, not leave two open leads for one number.
    /// </summary>
    [SqlServerFact]
    public async Task EditingInANumberWhileAnEnquiryForItIsSaving_IsRefused_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        var admin = new LeadUserContext { UserId = 1, Role = LeadRoles.Admin, DisplayName = "SQL admin" };
        LeadResponseDto existing;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            using var dispatcher = SqlLeadDispatcher(db);
            existing = (await SqlLeadService(db, dispatcher).IngestAsync(new LeadIntakeDto
            {
                FirstName = "Existing", Email = "existing@example.com", SourceCode = "walk_in"
            }, admin)).Lead!;
        }

        Task<LeadResponseDto>? edit = null;
        var race = new RunBeforeSavingInterceptor(
            context => context.ChangeTracker.Entries<Lead>().Any(e => e.State == EntityState.Added),
            async () =>
            {
                edit = Task.Run(async () =>
                {
                    await using var db = new AppDbContext(options);
                    using var dispatcher = SqlLeadDispatcher(db);
                    return await SqlLeadService(db, dispatcher).UpdateAsync(existing.Id, new UpdateLeadDto
                    {
                        FirstName = "Existing", Email = "existing@example.com", Phone = "0300-1234567",
                        ConcurrencyToken = existing.ConcurrencyToken
                    }, admin);
                });
                Assert.True(await SomeoneIsWaitingOnAContactLockAsync(database.ConnectionString, edit, TimeSpan.FromSeconds(20)),
                    "The edit never waited on the contact lock.");
            });

        await using (var db = new AppDbContext(Options(database.ConnectionString, race)))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            await SqlLeadService(db, dispatcher).IngestAsync(ConcurrentEnquiry("edit-race", phone: "0300-1234567"),
                actor: null, trustedExternal: true);
        }

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => edit!);
        Assert.Contains("Another open lead already uses that phone number", refused.Message);

        await using var verify = new AppDbContext(options);
        var number = LeadContactNormalizer.NormalizeUsablePhoneOrNull("0300-1234567");
        Assert.Equal(1, await verify.Leads.CountAsync(l => l.NormalizedPhone == number || l.NormalizedWhatsapp == number));
    }

    /// <summary>
    /// Two edit forms opened on the same version. The first save moves the real rowversion, so
    /// the second is refused as a whole — neither its details nor its timeline entry are written.
    /// </summary>
    [SqlServerFact]
    public async Task TwoLeadEditFormsOpenedTogether_TheStaleSaveIsRefusedAtomically_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        var admin = new LeadUserContext { UserId = 1, Role = LeadRoles.Admin, DisplayName = "SQL admin" };
        LeadResponseDto opened;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            using var dispatcher = SqlLeadDispatcher(db);
            opened = (await SqlLeadService(db, dispatcher).IngestAsync(new LeadIntakeDto
            {
                FirstName = "Existing", Email = "existing@example.com", SourceCode = "walk_in"
            }, admin)).Lead!;
        }

        UpdateLeadDto Edit(string city) => new()
        {
            FirstName = opened.FirstName, Email = opened.Email, City = city,
            ConcurrencyToken = opened.ConcurrencyToken
        };

        await using (var db = new AppDbContext(options))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            await SqlLeadService(db, dispatcher).UpdateAsync(opened.Id, Edit("Lahore"), admin);
        }

        await using (var db = new AppDbContext(options))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            await Assert.ThrowsAsync<LeadConcurrencyException>(() =>
                SqlLeadService(db, dispatcher).UpdateAsync(opened.Id, Edit("Karachi"), admin));
        }

        await using var verify = new AppDbContext(options);
        Assert.Equal("Lahore", (await verify.Leads.SingleAsync(l => l.Id == opened.Id)).City);
        Assert.Equal(1, await verify.LeadActivities.CountAsync(
            a => a.LeadId == opened.Id && a.Type == LeadActivityType.DetailsUpdated));
    }

    /// <summary>
    /// An edit that shares no contact detail with an enquiry enriching the same lead. The contact
    /// locks cannot order them, so the lead lock must: the edit waits for the enrichment to commit
    /// and is then refused as stale, instead of saving first and failing the enquiry on its row version.
    /// </summary>
    [SqlServerFact]
    public async Task AnEditWaitsBehindAnEnquiryEnrichingTheSameLead_AndIsThenRefusedAsStale_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        var admin = new LeadUserContext { UserId = 1, Role = LeadRoles.Admin, DisplayName = "SQL admin" };
        LeadResponseDto opened;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            using var dispatcher = SqlLeadDispatcher(db);
            opened = (await SqlLeadService(db, dispatcher).IngestAsync(new LeadIntakeDto
            {
                FirstName = "Existing", Email = "existing@example.com", SourceCode = "walk_in"
            }, admin)).Lead!;
        }

        Task<LeadResponseDto>? edit = null;
        var race = new RunBeforeSavingInterceptor(
            context => context.ChangeTracker.Entries<Lead>().Any(e => e.State == EntityState.Modified),
            async () =>
            {
                edit = Task.Run(async () =>
                {
                    await using var db = new AppDbContext(options);
                    using var dispatcher = SqlLeadDispatcher(db);
                    return await SqlLeadService(db, dispatcher).UpdateAsync(opened.Id, new UpdateLeadDto
                    {
                        FirstName = "Existing", Email = "renamed@example.com",
                        ConcurrencyToken = opened.ConcurrencyToken
                    }, admin);
                });
                Assert.True(await SomeoneIsWaitingOnAContactLockAsync(database.ConnectionString, edit, TimeSpan.FromSeconds(20)),
                    "The edit never waited on the lead lock.");
            });

        await using (var db = new AppDbContext(Options(database.ConnectionString, race)))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            var enquiry = ConcurrentEnquiry("enrich-race", phone: null);
            enquiry.Email = "existing@example.com";
            enquiry.City = "Islamabad";
            var enriched = await SqlLeadService(db, dispatcher).IngestAsync(enquiry, actor: null, trustedExternal: true);
            Assert.True(enriched.EnrichedExisting);
        }

        await Assert.ThrowsAsync<LeadConcurrencyException>(() => edit!);

        await using var verify = new AppDbContext(options);
        var stored = await verify.Leads.SingleAsync(l => l.Id == opened.Id);
        Assert.Equal("Islamabad", stored.City);
        Assert.Equal("existing@example.com", stored.Email);
    }

    [SqlServerFact]
    public async Task ExternalLeadFinalizationFailure_RollsBackAndReplayQueuesOneNotification()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Users.Add(new User
            {
                RoleId = 1, FullName = "Lead supervisor", Email = "lead-supervisor@dams.test",
                Password = "test-hash", AccountStatus = UserAccountStatus.Active
            });
            await db.SaveChangesAsync();
        }

        var intake = new LeadIntakeDto
        {
            FirstName = "External", SourceCode = "facebook", ExternalProvider = "meta",
            ExternalLeadId = "crash-recovery-lead", Email = "external@example.com"
        };
        var failure = new FailLeadFinalizationInterceptor();
        await using (var db = new AppDbContext(Options(database.ConnectionString, failure)))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            var leads = SqlLeadService(db, dispatcher);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                leads.IngestAsync(intake, actor: null, trustedExternal: true));
        }

        await using (var verify = new AppDbContext(options))
        {
            Assert.Empty(await verify.Leads.ToListAsync());
            Assert.Empty(await verify.LeadExternalSubmissions.ToListAsync());
            Assert.Empty(await verify.Notifications.ToListAsync());
        }

        await using (var db = new AppDbContext(options))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            var leads = SqlLeadService(db, dispatcher);
            var result = await leads.IngestAsync(intake, actor: null, trustedExternal: true);
            Assert.False(result.AlreadyIngested);
            Assert.Matches("^LD-[0-9]{6,}$", result.Lead!.LeadReference);
            Assert.Single(await db.Leads.ToListAsync());
            Assert.Single(await db.LeadExternalSubmissions.ToListAsync());
            Assert.Single(await db.Notifications.Where(n => n.Type == NotificationType.LeadCreated).ToListAsync());

            var replay = await leads.IngestAsync(intake, actor: null, trustedExternal: true);
            Assert.True(replay.AlreadyIngested);
            Assert.Single(await db.Notifications.Where(n => n.Type == NotificationType.LeadCreated).ToListAsync());
        }
    }

    [SqlServerFact]
    public async Task MetaEventCompletionFailure_RollsBackLeadAndRecoversOnRetry()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        var protector = new PlaintextSecretProtector();
        var graph = new FakeMetaGraphClient();
        var metaOptions = new MetaIntegrationOptions { MaxAttempts = 3, BaseRetryDelaySeconds = 1 };

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Users.Add(new User
            {
                RoleId = 1, FullName = "Lead supervisor", Email = "meta-supervisor@dams.test",
                Password = "test-hash", AccountStatus = UserAccountStatus.Active
            });
            var connection = new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "meta-recovery-account",
                DisplayName = "Recovery account", AccessTokenProtected = protector.Protect("token")
            };
            db.ExternalIntegrationConnections.Add(connection);
            db.ExternalIntegrationResources.Add(new ExternalIntegrationResource
            {
                Connection = connection, Provider = "meta", ResourceType = "facebook_page",
                ExternalId = "recovery-page", Name = "Recovery page", IsEnabled = true,
                IsActive = true, ResourceTokenProtected = protector.Protect("page-token")
            });
            await db.SaveChangesAsync();

            var intake = new MetaWebhookIntakeService(db, NullLogger<MetaWebhookIntakeService>.Instance);
            await intake.RecordAsync(MetaIntegrationHarness.WebhookBody("recovery-page", "meta-recovery-lead"));
        }

        graph.Leads["meta-recovery-lead"] = FakeMetaGraphClient.Lead("meta-recovery-lead",
            [("full_name", "Ali Khan"), ("email", "ali@example.com")],
            platform: "fb", pageId: "recovery-page", campaignName: "Recovery campaign");

        await using (var db = new AppDbContext(Options(database.ConnectionString,
                         new FailMetaCompletionInterceptor())))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            var processor = new MetaLeadEventProcessor(db, graph, protector,
                SqlLeadService(db, dispatcher), dispatcher, metaOptions,
                NullLogger<MetaLeadEventProcessor>.Instance);
            Assert.Equal(0, await processor.ProcessPendingEventsAsync(1));
        }

        await using (var verify = new AppDbContext(options))
        {
            Assert.Empty(await verify.Leads.ToListAsync());
            Assert.Empty(await verify.LeadExternalSubmissions.ToListAsync());
            Assert.Empty(await verify.Notifications.ToListAsync());
            var integrationEvent = await verify.ExternalIntegrationEvents.SingleAsync();
            Assert.Equal(ExternalIntegrationEventStatus.Retry, integrationEvent.Status);
            integrationEvent.AvailableAt = DateTime.UtcNow.AddSeconds(-1);
            await verify.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            var processor = new MetaLeadEventProcessor(db, graph, protector,
                SqlLeadService(db, dispatcher), dispatcher, metaOptions,
                NullLogger<MetaLeadEventProcessor>.Instance);
            Assert.Equal(1, await processor.ProcessPendingEventsAsync(1));
            Assert.Equal(0, await processor.ProcessPendingEventsAsync(1));
            var lead = await db.Leads.SingleAsync();
            var submission = await db.LeadExternalSubmissions.SingleAsync();
            Assert.Equal("Recovery campaign", submission.CampaignName);
            Assert.Equal("recovery-page", submission.PageExternalId);
            Assert.Equal(lead.Id, (await db.ExternalIntegrationEvents.SingleAsync()).LeadId);
            Assert.Single(await db.Notifications.Where(n => n.Type == NotificationType.LeadCreated).ToListAsync());
        }
    }

    [SqlServerFact]
    public async Task ALeadFormMapping_IsOnePerForm_RefusesAStaleSave_AndMapsTheNextLead_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        var protector = new PlaintextSecretProtector();
        var graph = new FakeMetaGraphClient();
        var metaOptions = new MetaIntegrationOptions { MaxAttempts = 3, BaseRetryDelaySeconds = 1 };
        const string buyingFor = "are_you_buying_for_?";
        int projectId;
        LeadUserContext admin;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var user = new User
            {
                RoleId = 1, FullName = "Mapping admin", Email = "mapping-admin@dams.test",
                Password = "test-hash", AccountStatus = UserAccountStatus.Active
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var project = new Project { ProjectName = "Floria Heights", Location = "Lahore", CreatedById = user.UserId };
            db.Projects.Add(project);
            var connection = new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "mapping-account",
                DisplayName = "Mapping account", AccessTokenProtected = protector.Protect("token")
            };
            db.ExternalIntegrationConnections.Add(connection);
            db.ExternalIntegrationResources.Add(new ExternalIntegrationResource
            {
                Connection = connection, Provider = "meta", ResourceType = "facebook_page",
                ExternalId = "mapping-page", Name = "Mapping page", IsEnabled = true,
                IsActive = true, ResourceTokenProtected = protector.Protect("page-token")
            });
            db.ExternalIntegrationResources.Add(new ExternalIntegrationResource
            {
                Connection = connection, Provider = "meta", ResourceType = "lead_form",
                ExternalId = "mapping-form", ParentExternalId = "mapping-page", Name = "Floria form",
                IsActive = true, LastSyncedAt = DateTime.UtcNow,
                MetadataJson = LeadFormQuestions.Serialize(
                [
                    new LeadFormQuestionDto
                    {
                        Key = buyingFor, Label = "Are you buying for ?",
                        Options =
                        [
                            new LeadFormOptionDto { Key = "investment", Value = "Investment" },
                            new LeadFormOptionDto { Key = "personal_living", Value = "Personal Living" }
                        ]
                    }
                ])
            });
            await db.SaveChangesAsync();
            projectId = project.Id;
            admin = new LeadUserContext { UserId = user.UserId, Role = LeadRoles.Admin, DisplayName = "Mapping admin" };

            var intake = new MetaWebhookIntakeService(db, NullLogger<MetaWebhookIntakeService>.Instance);
            await intake.RecordAsync(MetaIntegrationHarness.WebhookBody("mapping-page", "mapping-lead", "mapping-form"));
        }

        var answers = new List<LeadFormAnswerMappingDto>
        {
            new()
            {
                QuestionKey = buyingFor, Target = LeadFormAnswerTarget.PurchaseIntent,
                Options =
                [
                    new LeadFormOptionMappingDto { OptionKey = "investment", Value = "Investment" },
                    new LeadFormOptionMappingDto { OptionKey = "personal_living", Value = "SelfUse" }
                ]
            }
        };

        await using (var db = new AppDbContext(options))
        {
            var integration = new MetaIntegrationService(db, graph, protector,
                new MetaResourceSyncService(db, graph, protector, metaOptions, NullLogger<MetaResourceSyncService>.Instance),
                metaOptions, NullLogger<MetaIntegrationService>.Instance);

            var first = await integration.SaveLeadFormMappingAsync("mapping-form",
                new SaveLeadFormMappingDto { InterestedProjectId = projectId }, admin);
            Assert.False(string.IsNullOrEmpty(first.Version));
            db.ChangeTracker.Clear();

            // A screen opened before any mapping existed has not seen the one it would replace.
            await Assert.ThrowsAsync<LeadConcurrencyException>(() => integration.SaveLeadFormMappingAsync("mapping-form",
                new SaveLeadFormMappingDto { Answers = answers }, admin));
            db.ChangeTracker.Clear();

            await integration.SaveLeadFormMappingAsync("mapping-form",
                new SaveLeadFormMappingDto { InterestedProjectId = projectId, Answers = answers, Version = first.Version },
                admin);
            db.ChangeTracker.Clear();

            // The first version has moved on; saving over it again is refused by the rowversion.
            await Assert.ThrowsAsync<LeadConcurrencyException>(() => integration.SaveLeadFormMappingAsync("mapping-form",
                new SaveLeadFormMappingDto { Version = first.Version }, admin));
            db.ChangeTracker.Clear();

            // And the database itself allows one mapping per form, whatever the application checks.
            db.ExternalLeadFormMappings.Add(new ExternalLeadFormMapping { Provider = "meta", FormExternalId = "mapping-form" });
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("IX_ExternalLeadFormMappings_Provider_FormExternalId", duplicate.InnerException?.Message);
        }

        // Sent as the option's text, as the live API has been reported to do.
        graph.Leads["mapping-lead"] = FakeMetaGraphClient.Lead("mapping-lead",
            [("full_name", "Ali Khan"), ("email", "ali@example.com"), (buyingFor, "Personal Living")],
            pageId: "mapping-page", formId: "mapping-form");

        await using (var db = new AppDbContext(options))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            var leads = SqlLeadService(db, dispatcher);
            var processor = new MetaLeadEventProcessor(db, graph, protector, leads, dispatcher, metaOptions,
                NullLogger<MetaLeadEventProcessor>.Instance);
            Assert.Equal(1, await processor.ProcessPendingEventsAsync(1));

            var lead = await db.Leads.AsNoTracking().SingleAsync();
            Assert.Equal(projectId, lead.InterestedProjectId);
            Assert.Equal(LeadPurchaseIntent.SelfUse, lead.PurchaseIntent);
            Assert.Equal(LeadPaymentPreference.Unknown, lead.PaymentPreference);

            var answer = (await leads.GetExternalSubmissionsAsync(lead.Id, admin)).Single().FieldData
                .Single(a => a.Name == buyingFor);
            Assert.True(answer.IsMapped);
            Assert.Equal("Are you buying for ?", answer.Label);
            Assert.Equal("Personal Living", answer.ValueLabel);

            var filtered = await leads.GetLeadsAsync(new LeadFilterDto { ProjectId = projectId }, admin);
            Assert.Equal(lead.Id, Assert.Single(filtered.Items).Id);
        }
    }

    [SqlServerFact]
    public async Task TheMetaWorker_StartedWhileTheDatabaseIsDown_ResumesOnItsOwnWhenItRecovers()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var protector = new PlaintextSecretProtector();
        await using (var db = new AppDbContext(Options(database.ConnectionString)))
        {
            await db.Database.MigrateAsync();
            var connection = new ExternalIntegrationConnection
            {
                Provider = "meta", ExternalAccountId = "outage-account",
                DisplayName = "Outage account", AccessTokenProtected = protector.Protect("token")
            };
            db.ExternalIntegrationConnections.Add(connection);
            db.ExternalIntegrationResources.Add(new ExternalIntegrationResource
            {
                Connection = connection, Provider = "meta", ResourceType = "facebook_page",
                ExternalId = "outage-page", Name = "Outage page", IsEnabled = true,
                IsActive = true, ResourceTokenProtected = protector.Protect("page-token")
            });
            await db.SaveChangesAsync();
            await new MetaWebhookIntakeService(db, NullLogger<MetaWebhookIntakeService>.Instance)
                .RecordAsync(MetaIntegrationHarness.WebhookBody("outage-page", "outage-lead"));
        }

        var graph = new FakeMetaGraphClient();
        graph.Leads["outage-lead"] = FakeMetaGraphClient.Lead("outage-lead",
            [("full_name", "Sara Ahmed"), ("email", "sara@example.com")], pageId: "outage-page");

        var outage = new DatabaseOutage { Down = true };
        var logs = new ListLogger<DAMS.Api.IntegrationBackgroundService>();
        var metaOptions = new MetaIntegrationOptions
        {
            AppId = "app", AppSecret = "secret", WebhookVerifyToken = "verify",
            OAuthCallbackUrl = "https://dams.test/callback",
            EventIntervalSeconds = 1, StartupDelaySeconds = 0, StartupRetryMaxDelaySeconds = 1
        };

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(database.ConnectionString).AddInterceptors(outage));
        services.AddScoped(sp => SqlLeadDispatcher(sp.GetRequiredService<AppDbContext>()));
        services.AddScoped<IMetaLeadEventProcessor>(sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            return new MetaLeadEventProcessor(db, graph, protector,
                SqlLeadService(db, sp.GetRequiredService<NotificationDispatcher>()),
                sp.GetRequiredService<NotificationDispatcher>(), metaOptions,
                NullLogger<MetaLeadEventProcessor>.Instance);
        });
        services.AddScoped<IMetaResourceSyncService, NoDueResourceSync>();
        await using var provider = services.BuildServiceProvider();

        var worker = new DAMS.Api.IntegrationBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(metaOptions), logs);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            // Several failed readiness checks must leave the worker waiting, not finished.
            await WaitUntilAsync(() => outage.RefusedOpens >= 3, TimeSpan.FromSeconds(30));
            Assert.False(worker.ExecuteTask!.IsCompleted);
            Assert.Contains(logs.Messages, m => m.Contains("waiting for the database"));

            outage.Down = false;
            await WaitUntilAsync(() => LeadCountAsync().GetAwaiter().GetResult() == 1, TimeSpan.FromSeconds(30));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Contains(logs.Messages, m => m.Contains("started after"));
        Assert.DoesNotContain(logs.Messages, m => m.Contains("tables are missing"));
        await using (var verify = new AppDbContext(Options(database.ConnectionString)))
        {
            var integrationEvent = await verify.ExternalIntegrationEvents.SingleAsync();
            Assert.Equal(ExternalIntegrationEventStatus.Processed, integrationEvent.Status);
            Assert.Equal((await verify.Leads.SingleAsync()).Id, integrationEvent.LeadId);
        }

        async Task<int> LeadCountAsync()
        {
            await using var db = new AppDbContext(Options(database.ConnectionString));
            return await db.Leads.CountAsync();
        }
    }

    [SqlServerFact]
    public async Task TheMetaWorker_OnAReachableDatabaseWithoutItsTables_StopsOnceAndSaysWhy()
    {
        // No migration: the database answers, and its answer is that the tables are missing.
        await using var database = await SqlTestDatabase.CreateAsync();
        var logs = new ListLogger<DAMS.Api.IntegrationBackgroundService>();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(database.ConnectionString));
        await using var provider = services.BuildServiceProvider();

        var worker = new DAMS.Api.IntegrationBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new MetaIntegrationOptions
            {
                AppId = "app", AppSecret = "secret", WebhookVerifyToken = "verify",
                OAuthCallbackUrl = "https://dams.test/callback", StartupDelaySeconds = 0
            }), logs);
        await worker.StartAsync(CancellationToken.None);

        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30));
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(logs.Messages, m => m.Contains("tables are missing"));
        Assert.DoesNotContain(logs.Messages, m => m.Contains("waiting for the database"));
    }

    [SqlServerFact]
    public async Task TheNotificationWorker_StartedWhileTheDatabaseIsDown_ResumesOnItsOwnWhenItRecovers()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var db = new AppDbContext(Options(database.ConnectionString)))
            await db.Database.MigrateAsync();

        var outage = new DatabaseOutage { Down = true };
        var deliveries = new CountingDeliveryProcessor();
        var logs = new ListLogger<DAMS.Api.NotificationBackgroundService>();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(database.ConnectionString).AddInterceptors(outage));
        services.AddScoped<INotificationDeliveryProcessor>(_ => deliveries);
        await using var provider = services.BuildServiceProvider();

        var worker = new DAMS.Api.NotificationBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new NotificationOptions
            {
                DeliveryIntervalSeconds = 1, StartupDelaySeconds = 0, StartupRetryMaxDelaySeconds = 1
            }), logs);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => outage.RefusedOpens >= 3, TimeSpan.FromSeconds(30));
            Assert.False(worker.ExecuteTask!.IsCompleted);
            Assert.Equal(0, deliveries.Sweeps);
            Assert.Contains(logs.Messages, m => m.Contains("waiting for the database"));

            outage.Down = false;
            await WaitUntilAsync(() => deliveries.Sweeps > 0, TimeSpan.FromSeconds(30));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Contains(logs.Messages, m => m.Contains("Notification background processing started after"));
        Assert.DoesNotContain(logs.Messages, m => m.Contains("tables are missing"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(100);
        }
    }

    /// <summary>Refuses every connection the worker's context opens while <see cref="Down"/>.</summary>
    private sealed class DatabaseOutage : DbConnectionInterceptor
    {
        private int _refusedOpens;
        public volatile bool Down;
        public int RefusedOpens => Volatile.Read(ref _refusedOpens);

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Down)
            {
                Interlocked.Increment(ref _refusedOpens);
                throw new InvalidOperationException("Simulated database outage.");
            }

            return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
        }
    }

    private sealed class CountingDeliveryProcessor : INotificationDeliveryProcessor
    {
        private int _sweeps;
        public int Sweeps => Volatile.Read(ref _sweeps);

        public Task<int> ProcessDueDeliveriesAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _sweeps);
            return Task.FromResult(0);
        }

        public Task<int> ProcessScheduledJobsAsync(int batchSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> PruneAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class NoDueResourceSync : IMetaResourceSyncService
    {
        public Task<MetaSyncResultDto> SyncConnectionAsync(int connectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MetaSyncResultDto> SyncNowAsync(int connectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> SyncDueConnectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = [];
        public IReadOnlyList<string> Messages { get { lock (_messages) return _messages.ToList(); } }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_messages) _messages.Add(formatter(state, exception));
        }
    }

    [SqlServerFact]
    public async Task ATransientFailureWhileFinalizingALead_IsRetriedToOneCompleteLead()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var db = new AppDbContext(Options(database.ConnectionString)))
        {
            await db.Database.MigrateAsync();
            db.Users.Add(new User
            {
                RoleId = 1, FullName = "Lead supervisor", Email = "retry-supervisor@dams.test",
                Password = "test-hash", AccountStatus = UserAccountStatus.Active
            });
            await db.SaveChangesAsync();
        }

        // The API retries transient SQL errors. One arriving on the save that writes the final
        // reference must be retried into a whole lead, as it was before both saves shared a
        // transaction — not replayed against rows that transaction already rolled back.
        var transient = new FailOnceTransientlyOnLeadReference();
        var retrying = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString, sql => sql.EnableRetryOnFailure(
                3, TimeSpan.FromMilliseconds(10), [FailOnceTransientlyOnLeadReference.ErrorNumber]))
            .AddInterceptors(transient)
            .Options;

        await using (var db = new AppDbContext(retrying))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            var result = await SqlLeadService(db, dispatcher).IngestAsync(new LeadIntakeDto
            {
                FirstName = "Retried", SourceCode = "facebook", ExternalProvider = "meta",
                ExternalLeadId = "transient-finalization-lead", Email = "retried@example.com"
            }, actor: null, trustedExternal: true);

            Assert.True(transient.Failed);
            Assert.False(result.AlreadyIngested);
            Assert.Matches("^LD-[0-9]{6,}$", result.Lead!.LeadReference);
        }

        await using (var verify = new AppDbContext(Options(database.ConnectionString)))
        {
            var lead = await verify.Leads.SingleAsync();
            Assert.Equal($"LD-{lead.Id:D6}", lead.LeadReference);
            Assert.Equal(lead.Id, (await verify.LeadExternalSubmissions.SingleAsync()).LeadId);
            Assert.Equal(2, await verify.LeadActivities.CountAsync(a => a.LeadId == lead.Id));
            Assert.Single(await verify.Notifications.Where(n => n.Type == NotificationType.LeadCreated).ToListAsync());
        }

        // A sales employee's own lead is assigned to them by writing into the request. The retry
        // must see the request as it arrived, or it would refuse them their own lead.
        int salesUserId, salesEmployeeId;
        await using (var db = new AppDbContext(Options(database.ConnectionString)))
        {
            var salesUser = new User
            {
                RoleId = 4, FullName = "Retry seller", Email = "retry-seller@dams.test",
                Password = "test-hash", AccountStatus = UserAccountStatus.Active
            };
            db.Users.Add(salesUser);
            await db.SaveChangesAsync();
            var salesEmployee = new Employee
            {
                FullName = "Retry seller", UserId = salesUser.UserId, JobTitle = "Sales Executive",
                JoinDate = new DateTime(2026, 1, 1), Status = EmployeeStatus.Active
            };
            db.Employees.Add(salesEmployee);
            await db.SaveChangesAsync();
            (salesUserId, salesEmployeeId) = (salesUser.UserId, salesEmployee.Id);
        }

        transient.Arm();
        await using (var db = new AppDbContext(retrying))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            var result = await SqlLeadService(db, dispatcher).IngestAsync(new LeadIntakeDto
            {
                FirstName = "Walk-in", SourceCode = "walk_in", Phone = "03001112233"
            }, new LeadUserContext
            {
                UserId = salesUserId, Role = LeadRoles.Employee, DisplayName = "Retry seller",
                EmployeeId = salesEmployeeId
            });

            Assert.True(transient.Failed);
            Assert.Equal(salesEmployeeId, result.Lead!.AssignedEmployeeId);
        }

        await using (var verify = new AppDbContext(Options(database.ConnectionString)))
        {
            var lead = await verify.Leads.SingleAsync(l => l.FirstName == "Walk-in");
            Assert.Equal($"LD-{lead.Id:D6}", lead.LeadReference);
            Assert.Equal(salesEmployeeId, lead.AssignedEmployeeId);
            Assert.Single(await verify.LeadAssignmentHistories.Where(h => h.LeadId == lead.Id).ToListAsync());
            Assert.Equal(2, await verify.Leads.CountAsync());
        }
    }

    /// <summary>
    /// A small alert batch over a larger backlog must still reach every lead, on the real
    /// query plan. The owner has no login, so nobody can be told about these leads — exactly
    /// the rows that used to sit at the front of every batch for ever.
    /// </summary>
    [SqlServerFact]
    public async Task ASmallAlertScanWorksThroughTheWholeBacklog_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(options))
        {
            var owner = new Employee
            {
                FullName = "No Login",
                JobTitle = "Sales Executive",
                Department = "Sales",
                Phone = "03001114444",
                JoinDate = now.AddYears(-1),
                Status = EmployeeStatus.Active
            };
            db.Employees.Add(owner);
            await db.SaveChangesAsync();

            var source = await db.LeadSources.FirstAsync(s => s.Code == "walk_in");
            for (var i = 0; i < 5; i++)
                db.Leads.Add(new Lead
                {
                    LeadReference = $"LD-SCAN-{i}",
                    FirstName = $"Backlog {i}",
                    LeadSourceId = source.Id,
                    Stage = LeadStage.FirstContactPending,
                    AssignmentState = LeadAssignmentState.Assigned,
                    AssignedEmployeeId = owner.Id,
                    AssignedAt = now.AddHours(-10).AddMinutes(i),
                    LastActivityAt = now.AddDays(-30).AddMinutes(i),
                    CreatedAt = now.AddDays(-30)
                });
            await db.SaveChangesAsync();
        }

        async Task<LeadAlertScanResultDto> ScanAsync()
        {
            await using var db = new AppDbContext(options);
            using var dispatcher = SqlLeadDispatcher(db);
            var alerts = new LeadAlertService(db, new LeadNotificationService(db, dispatcher),
                Microsoft.Extensions.Options.Options.Create(new LeadAlertOptions { MaxRowsPerScan = 2 }),
                TimeProvider.System);
            return await alerts.RunScanAsync();
        }

        for (var i = 0; i < 3; i++)
        {
            var scan = await ScanAsync();
            Assert.InRange(scan.FirstContactOverdue, 1, 2);
            Assert.InRange(scan.InactiveLeads, 1, 2);
        }

        var settled = await ScanAsync();
        Assert.Equal(0, settled.FirstContactOverdue);
        Assert.Equal(0, settled.InactiveLeads);

        await using var verify = new AppDbContext(options);
        Assert.Equal(5, await verify.LeadAlertChecks.CountAsync(c =>
            c.FirstContactAssignmentCheckedAt == c.Lead.AssignedAt && c.InactivityCheckedAt != null));
    }

    /// <summary>
    /// Follow-up and visit alerts are keyed by schedule from KeyLeadAlertsBySchedule on. The
    /// migration must carry reminders already sent for the CURRENT schedule onto the new key, so
    /// the first scan after release does not remind everyone again — while a reminder sent before
    /// a reschedule keeps its old key and the new time is still reminded.
    /// </summary>
    [SqlServerFact]
    public async Task AlertKeysAreCarriedOntoTheCurrentSchedule_SoReleaseSendsNoRepeatReminders_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync("20260925141153_AddLeadAlertChecks");

        var now = DateTime.UtcNow;
        int userId, employeeId, leadId, visitId;
        await using (var db = new AppDbContext(options))
        {
            var user = new User
            {
                RoleId = 4, FullName = "Rekey seller", Email = "rekey-seller@dams.test",
                Password = "test-hash", AccountStatus = UserAccountStatus.Active
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var employee = new Employee
            {
                FullName = "Rekey seller", UserId = user.UserId, JobTitle = "Sales Executive",
                JoinDate = now.AddYears(-1), Status = EmployeeStatus.Active
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();

            var source = await db.LeadSources.FirstAsync(s => s.Code == "walk_in");
            var lead = new Lead
            {
                LeadReference = "LD-REKEY", FirstName = "Rekey", LeadSourceId = source.Id,
                Stage = LeadStage.Contacted, AssignmentState = LeadAssignmentState.Assigned,
                AssignedEmployeeId = employee.Id, AssignedAt = now.AddDays(-1),
                FirstContactAt = now.AddDays(-1), LastActivityAt = now, CreatedAt = now.AddDays(-1)
            };
            db.Leads.Add(lead);
            await db.SaveChangesAsync();

            // Moved twice, last three hours ago.
            var visit = new LeadSiteVisit
            {
                LeadId = lead.Id, AssignedEmployeeId = employee.Id, ScheduledAt = now.AddDays(30),
                MeetingLocation = "Site office", Status = LeadSiteVisitStatus.Rescheduled,
                RescheduleCount = 2, CreatedAt = now.AddDays(-2), UpdatedAt = now.AddHours(-3)
            };
            db.LeadSiteVisits.Add(visit);
            await db.SaveChangesAsync();
            (userId, employeeId, leadId, visitId) = (user.UserId, employee.Id, lead.Id, visit.Id);
        }

        // The model already carries RescheduleCount, which this schema does not yet have.
        Task<int> FollowUpAsync(string title, int status, DateTime dueAt, DateTime createdAt, DateTime? updatedAt) =>
            ScalarAsync(database.ConnectionString, $"""
                INSERT INTO [LeadFollowUps] ([LeadId],[Type],[AssignedEmployeeId],[Title],[DueAt],[Priority],[Status],[CreatedAt],[UpdatedAt])
                VALUES ({leadId}, 0, {employeeId}, N'{title}', '{dueAt:O}', 1, {status}, '{createdAt:O}',
                        {(updatedAt == null ? "NULL" : $"'{updatedAt:O}'")});
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """);

        var current = await FollowUpAsync("Current", 0, now.AddHours(1), now.AddHours(-5), null);
        var rescheduled = await FollowUpAsync("Rescheduled", 0, now.AddHours(2), now.AddHours(-5), now.AddHours(-1));
        var alreadyRekeyed = await FollowUpAsync("Already rekeyed", 0, now.AddHours(3), now.AddHours(-5), null);
        var completed = await FollowUpAsync("Completed", 1, now.AddHours(-1), now.AddHours(-5), now.AddMinutes(-30));

        await using (var db = new AppDbContext(options))
        {
            Notification Sent(NotificationType type, string key, DateTime createdAt) => new()
            {
                Type = type, RecipientUserId = userId, DedupKey = key, Title = key, Message = key,
                EntityType = NotificationEntityType.Lead, EntityId = leadId, CreatedAt = createdAt
            };

            db.Notifications.AddRange(
                Sent(NotificationType.FollowUpDue, $"FollowUpDue:{current}:{userId}", now.AddHours(-1)),
                Sent(NotificationType.FollowUpOverdue, $"FollowUpOverdue:{rescheduled}:{userId}", now.AddHours(-2)),
                Sent(NotificationType.FollowUpDue, $"FollowUpDue:{alreadyRekeyed}:{userId}", now.AddHours(-2)),
                Sent(NotificationType.FollowUpDue, $"FollowUpDue:{alreadyRekeyed}:{userId}:0", now.AddMinutes(-5)),
                Sent(NotificationType.FollowUpDue, $"FollowUpDue:{completed}:{userId}", now.AddHours(-2)),
                Sent(NotificationType.SiteVisitReminder, $"SiteVisitToday:{visitId}:{userId}:2026-01-01", now.AddHours(-4)),
                Sent(NotificationType.SiteVisitReminder, $"SiteVisitToday:{visitId}:{userId}:2026-01-02", now.AddHours(-1)));
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        async Task<List<string>> KeysAsync(string prefix)
        {
            await using var db = new AppDbContext(options);
            return await db.Notifications.AsNoTracking()
                .Where(n => n.DedupKey.StartsWith(prefix))
                .OrderBy(n => n.DedupKey)
                .Select(n => n.DedupKey)
                .ToListAsync();
        }

        // Sent for the current schedule: carried onto count 0 (a follow-up) or its own count (a visit).
        Assert.Equal(new[] { $"FollowUpDue:{current}:{userId}:0" }, await KeysAsync($"FollowUpDue:{current}:"));
        Assert.Equal(new[] { $"SiteVisitToday:{visitId}:{userId}:2026-01-01", $"SiteVisitToday:{visitId}:{userId}:2026-01-02:2" },
            await KeysAsync($"SiteVisitToday:{visitId}:"));
        // Sent before the reschedule, for a closed follow-up, or already carried by new code: untouched.
        Assert.Equal(new[] { $"FollowUpOverdue:{rescheduled}:{userId}" }, await KeysAsync($"FollowUpOverdue:{rescheduled}:"));
        Assert.Equal(new[] { $"FollowUpDue:{completed}:{userId}" }, await KeysAsync($"FollowUpDue:{completed}:"));
        Assert.Equal(new[] { $"FollowUpDue:{alreadyRekeyed}:{userId}", $"FollowUpDue:{alreadyRekeyed}:{userId}:0" },
            await KeysAsync($"FollowUpDue:{alreadyRekeyed}:"));

        // The first scan after release: only the rescheduled follow-up's new time is reminded.
        await using (var db = new AppDbContext(options))
        {
            using var dispatcher = SqlLeadDispatcher(db);
            await new LeadAlertService(db, new LeadNotificationService(db, dispatcher),
                Microsoft.Extensions.Options.Options.Create(new LeadAlertOptions()), TimeProvider.System).RunScanAsync();
        }

        Assert.Single(await KeysAsync($"FollowUpDue:{current}:"));
        Assert.Equal(2, (await KeysAsync($"FollowUpDue:{alreadyRekeyed}:")).Count);
        Assert.Equal(new[] { $"FollowUpDue:{rescheduled}:{userId}:0" }, await KeysAsync($"FollowUpDue:{rescheduled}:"));
    }

    private static NotificationDispatcher SqlLeadDispatcher(AppDbContext db) => new(
        db, new NotificationSettingsStore(db), new NotificationRealtimeBroker(),
        TimeProvider.System, new NotificationEligibilityPolicy(db),
        NullLogger<NotificationDispatcher>.Instance);

    private static LeadService SqlLeadService(AppDbContext db, NotificationDispatcher dispatcher, int lockTimeoutMilliseconds = 15000)
    {
        var customers = new CustomerService(db);
        return new LeadService(db, customers,
            new BookingService(db, customers, new FinanceAccountService(db)),
            new LeadNotificationService(db, dispatcher),
            Microsoft.Extensions.Options.Options.Create(new LeadAlertOptions()))
        {
            IntakeLockTimeoutMilliseconds = lockTimeoutMilliseconds
        };
    }

    /// <summary>
    /// True once SQL Server shows some session waiting on an application lock in this database —
    /// proof that a concurrent write is blocked on a contact lock, not merely slow to start.
    /// Gives up when <paramref name="other"/> finishes first or the timeout passes.
    /// </summary>
    private static async Task<bool> SomeoneIsWaitingOnAContactLockAsync(string connectionString, Task other, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        while (DateTime.UtcNow < deadline && !other.IsCompleted)
        {
            await using var command = new SqlCommand("""
                SELECT COUNT(*) FROM sys.dm_tran_locks
                WHERE resource_type = 'APPLICATION' AND request_status = 'WAIT' AND resource_database_id = DB_ID()
                """, connection);
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) > 0)
                return true;
            await Task.Delay(50);
        }

        return false;
    }

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
    /// Retry is an operator action guarded by the event rowversion. Two independent SQL Server
    /// contexts may click it together, but exactly one append-only audit row and one requeue may
    /// commit; the loser must reread the winner's pending state rather than fail the request.
    /// </summary>
    [SqlServerFact]
    public async Task TwoAdminsRetryingTheSameFailedMetaEvent_ProduceOneAuditAndOneRequeue()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        int adminUserId, connectionId, eventId;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var admin = new User
            {
                FullName = "Retry Admin",
                Email = "retry-admin@dams.test",
                Password = "hash",
                RoleId = 1
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            adminUserId = admin.UserId;

            var connection = new ExternalIntegrationConnection
            {
                Provider = IntegrationProviders.Meta,
                ExternalAccountId = "retry-race-account",
                DisplayName = "Retry race",
                Status = ExternalIntegrationConnectionStatus.Connected
            };
            db.ExternalIntegrationConnections.Add(connection);
            await db.SaveChangesAsync();
            connectionId = connection.Id;

            var page = new ExternalIntegrationResource
            {
                ExternalIntegrationConnectionId = connectionId,
                Provider = IntegrationProviders.Meta,
                ResourceType = ExternalResourceTypes.FacebookPage,
                ExternalId = "retry-race-page",
                IsEnabled = true,
                IsActive = true
            };
            db.ExternalIntegrationResources.Add(page);
            await db.SaveChangesAsync();

            var integrationEvent = new ExternalIntegrationEvent
            {
                Provider = IntegrationProviders.Meta,
                ExternalIntegrationConnectionId = connectionId,
                ExternalIntegrationResourceId = page.Id,
                EventType = "leadgen",
                EventKey = "retry-race:event",
                ResourceExternalId = page.ExternalId,
                RawPayloadJson = "{\"leadgen_id\":\"retry-race-lead\"}",
                Status = ExternalIntegrationEventStatus.Failed,
                LastError = "temporary failure",
                ProcessedAt = DateTime.UtcNow,
                AvailableAt = DateTime.UtcNow
            };
            db.ExternalIntegrationEvents.Add(integrationEvent);
            await db.SaveChangesAsync();
            eventId = integrationEvent.Id;
        }

        var protector = new DAMS.Application.Tests.Integrations.PlaintextSecretProtector();
        var graph = new DAMS.Application.Tests.Integrations.FakeMetaGraphClient();
        var metaOptions = new MetaIntegrationOptions
        {
            AppId = "app", AppSecret = "secret", WebhookVerifyToken = "verify",
            OAuthCallbackUrl = "https://dams.test/callback"
        };
        var adminContext = new LeadUserContext
        {
            UserId = adminUserId,
            Role = LeadRoles.Admin,
            DisplayName = "Retry Admin"
        };

        async Task<MetaEventDto> RetryAsync()
        {
            await using var db = new AppDbContext(options);
            var sync = new DAMS.Application.Services.Integrations.MetaResourceSyncService(
                db, graph, protector, metaOptions,
                NullLogger<DAMS.Application.Services.Integrations.MetaResourceSyncService>.Instance);
            var integration = new DAMS.Application.Services.Integrations.MetaIntegrationService(
                db, graph, protector, sync, metaOptions,
                NullLogger<DAMS.Application.Services.Integrations.MetaIntegrationService>.Instance);
            return await integration.RetryEventAsync(connectionId, eventId, adminContext);
        }

        var results = await Task.WhenAll(RetryAsync(), RetryAsync());

        Assert.All(results, result =>
        {
            Assert.Equal(ExternalIntegrationEventStatus.Pending, result.Status);
            Assert.Equal(1, result.RetryCount);
        });
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [ExternalIntegrationEventRetries]"));
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT [RetryCount] FROM [ExternalIntegrationEvents] WHERE [Id] = " + eventId));
    }

    /// <summary>
    /// Event retention deletes old finished events in one set-based statement. Retry history
    /// rows point at their event, so a restricting foreign key made that whole statement fail
    /// once any old event had ever been retried — and since every later run hits the same row,
    /// cleanup then never succeeded again. Only real SQL Server enforces the key.
    /// </summary>
    [SqlServerFact]
    public async Task PruningOldEvents_AlsoRemovesTheRetryHistoryOfRetriedEvents()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        var old = DateTime.UtcNow.AddDays(-40);
        int recentId;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var admin = new User
            {
                FullName = "Prune Admin", Email = "prune-admin@dams.test", Password = "hash", RoleId = 1
            };
            var connection = new ExternalIntegrationConnection
            {
                Provider = IntegrationProviders.Meta, ExternalAccountId = "prune-account",
                DisplayName = "Prune", Status = ExternalIntegrationConnectionStatus.Connected
            };
            db.Users.Add(admin);
            db.ExternalIntegrationConnections.Add(connection);
            await db.SaveChangesAsync();

            ExternalIntegrationEvent Event(string key, ExternalIntegrationEventStatus status, DateTime receivedAt) => new()
            {
                Provider = IntegrationProviders.Meta,
                ExternalIntegrationConnectionId = connection.Id,
                EventType = "leadgen",
                EventKey = key,
                ResourceExternalId = "prune-page",
                RawPayloadJson = "{}",
                Status = status,
                ReceivedAt = receivedAt,
                AvailableAt = receivedAt
            };
            var retriedThenProcessed = Event("prune:retried", ExternalIntegrationEventStatus.Processed, old);
            var retriedThenFailed = Event("prune:failed-again", ExternalIntegrationEventStatus.Failed, old);
            var neverRetried = Event("prune:plain", ExternalIntegrationEventStatus.Processed, old);
            var recent = Event("prune:recent", ExternalIntegrationEventStatus.Processed, DateTime.UtcNow);
            db.ExternalIntegrationEvents.AddRange(retriedThenProcessed, retriedThenFailed, neverRetried, recent);
            await db.SaveChangesAsync();
            recentId = recent.Id;

            db.ExternalIntegrationEventRetries.AddRange(
                new ExternalIntegrationEventRetry
                    { ExternalIntegrationEventId = retriedThenProcessed.Id, RequestedByUserId = admin.UserId, RequestedAt = old },
                new ExternalIntegrationEventRetry
                    { ExternalIntegrationEventId = retriedThenFailed.Id, RequestedByUserId = admin.UserId, RequestedAt = old },
                new ExternalIntegrationEventRetry
                    { ExternalIntegrationEventId = retriedThenFailed.Id, RequestedByUserId = admin.UserId, RequestedAt = old.AddHours(1) },
                new ExternalIntegrationEventRetry
                    { ExternalIntegrationEventId = recent.Id, RequestedByUserId = admin.UserId, RequestedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var retention = new MetaIntegrationOptions
        {
            AppId = SqlMetaOptions.AppId, AppSecret = SqlMetaOptions.AppSecret,
            WebhookVerifyToken = SqlMetaOptions.WebhookVerifyToken, EventRetentionDays = 30
        };
        await using (var db = new AppDbContext(options))
        {
            var pruned = await MetaProcessor(db, new DAMS.Application.Tests.Integrations.FakeMetaGraphClient(), retention)
                .PruneOldEventsAsync();
            Assert.Equal(3, pruned);
        }

        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [ExternalIntegrationEvents]"));
        Assert.Equal(recentId, await ScalarAsync(database.ConnectionString,
            "SELECT [Id] FROM [ExternalIntegrationEvents]"));
        // The recent event keeps its audit; the pruned events' audit went with them.
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [ExternalIntegrationEventRetries] WHERE [ExternalIntegrationEventId] = " + recentId));
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [ExternalIntegrationEventRetries]"));
    }

    /// <summary>
    /// The enable check treats a copy of the Page that Meta no longer returns for its own
    /// connection (IsActive = false) as no real claim, but that copy still holds the filtered
    /// unique index. Enabling here used to pass the check, subscribe the Page at Meta, and then
    /// be refused by the index. The stale copy is now released in the same save; an active
    /// enabled copy still blocks.
    /// </summary>
    [SqlServerFact]
    public async Task EnablingAPageWhoseOtherEnabledCopyIsNoLongerReturnedByMeta_TakesItOverInOneSave()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        var protector = new DAMS.Application.Tests.Integrations.PlaintextSecretProtector();
        int staleId, takingId, blockedId, takingConnectionId, blockedConnectionId;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            ExternalIntegrationConnection Connection(string account) => new()
            {
                Provider = IntegrationProviders.Meta, ExternalAccountId = account, DisplayName = account,
                Status = ExternalIntegrationConnectionStatus.Connected,
                AccessTokenProtected = protector.Protect("token-" + account)
            };
            var staleOwner = Connection("stale-owner");
            var taking = Connection("taking-over");
            var blocked = Connection("blocked");
            db.ExternalIntegrationConnections.AddRange(staleOwner, taking, blocked);
            await db.SaveChangesAsync();
            takingConnectionId = taking.Id;
            blockedConnectionId = blocked.Id;

            ExternalIntegrationResource Page(int connectionId, string pageId, bool enabled, bool active) => new()
            {
                ExternalIntegrationConnectionId = connectionId, Provider = IntegrationProviders.Meta,
                ResourceType = ExternalResourceTypes.FacebookPage, ExternalId = pageId,
                IsEnabled = enabled, IsActive = active, IsSubscribed = enabled
            };
            var stale = Page(staleOwner.Id, "moved-page", enabled: true, active: false);
            var takingPage = Page(taking.Id, "moved-page", enabled: false, active: true);
            // Control: a Page still actively owned elsewhere must keep blocking.
            var activeOwner = Page(staleOwner.Id, "owned-page", enabled: true, active: true);
            var blockedPage = Page(blocked.Id, "owned-page", enabled: false, active: true);
            db.ExternalIntegrationResources.AddRange(stale, takingPage, activeOwner, blockedPage);
            await db.SaveChangesAsync();
            staleId = stale.Id;
            takingId = takingPage.Id;
            blockedId = blockedPage.Id;
        }

        var graph = new DAMS.Application.Tests.Integrations.FakeMetaGraphClient();
        DAMS.Application.Services.Integrations.MetaIntegrationService Integration(AppDbContext db) => new(
            db, graph, protector,
            new DAMS.Application.Services.Integrations.MetaResourceSyncService(
                db, graph, protector, SqlMetaOptions,
                NullLogger<DAMS.Application.Services.Integrations.MetaResourceSyncService>.Instance),
            SqlMetaOptions, NullLogger<DAMS.Application.Services.Integrations.MetaIntegrationService>.Instance);

        await using (var db = new AppDbContext(options))
        {
            var result = await Integration(db).SetResourceEnabledAsync(takingConnectionId, takingId, isEnabled: true);
            Assert.True(result.IsEnabled);
            Assert.True(result.IsSubscribed);
        }

        await using (var db = new AppDbContext(options))
        {
            var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Integration(db).SetResourceEnabledAsync(blockedConnectionId, blockedId, isEnabled: true));
            Assert.Contains("already enabled through another", blocked.Message);
        }

        Assert.Equal(["moved-page"], graph.SubscribedPages);
        Assert.Equal(0, await ScalarAsync(database.ConnectionString,
            "SELECT CAST([IsEnabled] AS int) + CAST([IsSubscribed] AS int) FROM [ExternalIntegrationResources] WHERE [Id] = " + staleId));
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [ExternalIntegrationResources] WHERE [ExternalId] = 'moved-page' AND [IsEnabled] = 1 AND [Id] = " + takingId));
        Assert.Equal(0, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [ExternalIntegrationResources] WHERE [Id] = " + blockedId + " AND [IsEnabled] = 1"));
    }

    /// <summary>
    /// A webhook id longer than the event columns can hold used to fail the whole delivery's
    /// save, so Meta retried it forever and the valid lead beside it never got in. Only a
    /// provider that enforces column lengths can show that.
    /// </summary>
    [SqlServerFact]
    public async Task WebhookIntake_RecordsAnOversizedEventAsFailed_WithoutLosingTheValidEventBesideIt()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        await using (var db = new AppDbContext(options))
            await SeedMetaPageAsync(db, "page-oversized");

        var oversizedLeadgenId = new string('9', 400);
        var body = $$$"""
            {
              "object": "page",
              "entry": [{
                "id": "page-oversized",
                "changes": [
                  {"field": "leadgen", "value": {"page_id": "page-oversized", "leadgen_id": "{{{oversizedLeadgenId}}}"}},
                  {"field": "leadgen", "value": {"page_id": "page-oversized", "leadgen_id": "lead-valid"}}
                ]
              }]
            }
            """;

        await using (var db = new AppDbContext(options))
            Assert.Equal(2, await MetaIntake(db).RecordAsync(body));

        // A redelivery of the same body is recognised, the oversized event included.
        await using (var db = new AppDbContext(options))
            Assert.Equal(0, await MetaIntake(db).RecordAsync(body));

        await using (var db = new AppDbContext(options))
        {
            var events = await db.ExternalIntegrationEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
            Assert.Equal(2, events.Count);

            var oversized = events[0];
            Assert.Equal(ExternalIntegrationEventStatus.Failed, oversized.Status);
            Assert.Contains(oversizedLeadgenId, oversized.RawPayloadJson);
            Assert.NotNull(oversized.LastError);

            var valid = events[1];
            Assert.Equal(ExternalIntegrationEventStatus.Pending, valid.Status);
            Assert.EndsWith(":page-oversized:lead-valid", valid.EventKey);
        }
    }

    /// <summary>
    /// A lead whose write fails used to poison the worker: the failed Lead stayed tracked, so
    /// saving the event's retry state re-attempted the same insert, threw again, and took the
    /// rest of the batch down with it. The failure here is a real SQL Server constraint
    /// violation, removed afterwards to prove the parked event then recovers on its own.
    ///
    /// It deliberately fails the lead's second save, after the first has inserted the row:
    /// the whole write must roll back, not leave a half-made "LD-PENDING-…" lead behind that
    /// the retry would then treat as already ingested and never finish.
    /// </summary>
    [SqlServerFact]
    public async Task AMetaLeadWhoseWriteFails_IsParkedForRetry_WithoutBlockingTheNextLead_AndRecovers()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var graph = new DAMS.Application.Tests.Integrations.FakeMetaGraphClient();
        graph.Leads["lead-poison"] = DAMS.Application.Tests.Integrations.FakeMetaGraphClient.Lead(
            "lead-poison", [("full_name", "Poison Pill"), ("phone_number", "03001110001")]);
        graph.Leads["lead-good"] = DAMS.Application.Tests.Integrations.FakeMetaGraphClient.Lead(
            "lead-good", [("full_name", "Good Lead"), ("phone_number", "03001110002")]);

        await using (var db = new AppDbContext(options))
        {
            await SeedMetaPageAsync(db, "page-poison");
            // Recorded first, so it is claimed and processed ahead of the valid one.
            await MetaIntake(db).RecordAsync(
                DAMS.Application.Tests.Integrations.MetaIntegrationHarness.WebhookBody("page-poison", "lead-poison"));
            await MetaIntake(db).RecordAsync(
                DAMS.Application.Tests.Integrations.MetaIntegrationHarness.WebhookBody("page-poison", "lead-good"));
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE [Leads] ADD CONSTRAINT [CK_Test_RejectPoison] " +
                "CHECK ([FirstName] <> N'Poison' OR [LeadReference] LIKE N'LD-PENDING-%')");
        }

        await using (var db = new AppDbContext(options))
            Assert.Equal(1, await MetaProcessor(db, graph).ProcessPendingEventsAsync(10));

        await using (var db = new AppDbContext(options))
        {
            var poison = await db.ExternalIntegrationEvents.AsNoTracking().SingleAsync(e => e.EventKey.EndsWith(":lead-poison"));
            Assert.Equal(ExternalIntegrationEventStatus.Retry, poison.Status);
            Assert.Equal(1, poison.Attempts);
            Assert.NotNull(poison.LastError);
            Assert.Null(poison.LeadId);
            Assert.Null(poison.LockedUntil);

            var good = await db.ExternalIntegrationEvents.AsNoTracking().SingleAsync(e => e.EventKey.EndsWith(":lead-good"));
            Assert.Equal(ExternalIntegrationEventStatus.Processed, good.Status);
            Assert.NotNull(good.LeadId);

            Assert.Equal("Good", (await db.Leads.AsNoTracking().SingleAsync()).FirstName);
            Assert.Equal(1, await db.LeadExternalSubmissions.CountAsync());

            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Leads] DROP CONSTRAINT [CK_Test_RejectPoison]");
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [ExternalIntegrationEvents] SET [AvailableAt] = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE [Id] = {0}",
                poison.Id);
        }

        await using (var db = new AppDbContext(options))
            Assert.Equal(1, await MetaProcessor(db, graph).ProcessPendingEventsAsync(10));

        await using (var db = new AppDbContext(options))
        {
            var poison = await db.ExternalIntegrationEvents.AsNoTracking().SingleAsync(e => e.EventKey.EndsWith(":lead-poison"));
            Assert.Equal(ExternalIntegrationEventStatus.Processed, poison.Status);
            Assert.Equal(2, poison.Attempts);
            Assert.Null(poison.LastError);

            var leads = await db.Leads.AsNoTracking().OrderBy(l => l.Id).ToListAsync();
            Assert.Equal(["Good", "Poison"], leads.Select(l => l.FirstName));
            Assert.All(leads, l => Assert.StartsWith("LD-0", l.LeadReference));
            Assert.Equal(2, await db.LeadExternalSubmissions.CountAsync());
        }
    }

    /// <summary>
    /// When even the retry state cannot be saved, the event is left leased in Processing. The
    /// attempt must already be on record by then — it is saved before the lead is fetched — or
    /// every lease expiry would retry it afresh, with no backoff and no end. It is reclaimed
    /// once its lease lapses, and the lead behind it is never held up.
    /// </summary>
    [SqlServerFact]
    public async Task WhenSavingTheRetryStateFails_TheAttemptIsStillCounted_AndTheEventRecoversAfterItsLease()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var graph = new DAMS.Application.Tests.Integrations.FakeMetaGraphClient();
        graph.Leads["lead-poison"] = DAMS.Application.Tests.Integrations.FakeMetaGraphClient.Lead(
            "lead-poison", [("full_name", "Poison Pill"), ("phone_number", "03001110001")]);
        graph.Leads["lead-good"] = DAMS.Application.Tests.Integrations.FakeMetaGraphClient.Lead(
            "lead-good", [("full_name", "Good Lead"), ("phone_number", "03001110002")]);

        await using (var db = new AppDbContext(options))
        {
            await SeedMetaPageAsync(db, "page-outage");
            await MetaIntake(db).RecordAsync(
                DAMS.Application.Tests.Integrations.MetaIntegrationHarness.WebhookBody("page-outage", "lead-poison"));
            await MetaIntake(db).RecordAsync(
                DAMS.Application.Tests.Integrations.MetaIntegrationHarness.WebhookBody("page-outage", "lead-good"));
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE [Leads] ADD CONSTRAINT [CK_Test_RejectPoison] CHECK ([FirstName] <> N'Poison')");
        }

        // The lead write fails, and then so does saving its retry state.
        await using (var db = new AppDbContext(Options(database.ConnectionString, new FailRetryStateInterceptor())))
            Assert.Equal(1, await MetaProcessor(db, graph).ProcessPendingEventsAsync(10));

        int poisonId;
        await using (var db = new AppDbContext(options))
        {
            var poison = await db.ExternalIntegrationEvents.AsNoTracking().SingleAsync(e => e.EventKey.EndsWith(":lead-poison"));
            poisonId = poison.Id;
            Assert.Equal(ExternalIntegrationEventStatus.Processing, poison.Status);
            Assert.NotNull(poison.LockedUntil);
            Assert.Equal(1, poison.Attempts);

            Assert.Equal(ExternalIntegrationEventStatus.Processed, (await db.ExternalIntegrationEvents
                .AsNoTracking().SingleAsync(e => e.EventKey.EndsWith(":lead-good"))).Status);

            // Still leased: nothing reclaims it early.
            Assert.Equal(0, await MetaProcessor(db, graph).ProcessPendingEventsAsync(10));

            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [ExternalIntegrationEvents] SET [LockedUntil] = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE [Id] = {0}",
                poisonId);
        }

        // Lease lapsed: reclaimed, and this time the retry state is saved normally.
        await using (var db = new AppDbContext(options))
            Assert.Equal(0, await MetaProcessor(db, graph).ProcessPendingEventsAsync(10));

        await using (var db = new AppDbContext(options))
        {
            var poison = await db.ExternalIntegrationEvents.AsNoTracking().SingleAsync(e => e.Id == poisonId);
            Assert.Equal(ExternalIntegrationEventStatus.Retry, poison.Status);
            Assert.Equal(2, poison.Attempts);
            Assert.Null(poison.LockedUntil);

            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Leads] DROP CONSTRAINT [CK_Test_RejectPoison]");
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [ExternalIntegrationEvents] SET [AvailableAt] = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE [Id] = {0}",
                poisonId);
        }

        await using (var db = new AppDbContext(options))
            Assert.Equal(1, await MetaProcessor(db, graph).ProcessPendingEventsAsync(10));

        await using (var db = new AppDbContext(options))
        {
            var poison = await db.ExternalIntegrationEvents.AsNoTracking().SingleAsync(e => e.Id == poisonId);
            Assert.Equal(ExternalIntegrationEventStatus.Processed, poison.Status);
            Assert.Equal(3, poison.Attempts);
            Assert.Equal(2, await db.Leads.CountAsync());
        }
    }

    /// <summary>
    /// Meta decides how long a campaign name, an ad name or a form answer is. None of that may
    /// fail a write against DAMS's column limits — and whatever had to be shortened or dropped
    /// to fit is still on the submission, verbatim.
    /// </summary>
    [SqlServerFact]
    public async Task OversizedMetaValues_AreBoundedToStorageLimits_AndTheOriginalAnswersAreKept()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var longAnswer = new string('x', 5000);
        var longFirst = new string('F', 150);
        var longLast = new string('L', 150);
        var oversized = DAMS.Application.Tests.Integrations.FakeMetaGraphClient.Lead(
            "lead-oversized",
            [
                ("full_name", $"{longFirst} {longLast}"),
                ("phone_number", new string('3', 60)),
                ("email", $"{new string('e', 250)}@example.com"),
                ("city", new string('C', 150)),
                ("what_are_you_looking_for", longAnswer)
            ],
            campaignName: new string('c', 400),
            adName: new string('a', 400),
            formId: new string('9', 250));
        oversized.CampaignId = new string('8', 250);
        oversized.AdSetName = new string('s', 400);

        var graph = new DAMS.Application.Tests.Integrations.FakeMetaGraphClient();
        graph.Leads["lead-oversized"] = oversized;
        graph.Leads["lead-normal"] = DAMS.Application.Tests.Integrations.FakeMetaGraphClient.Lead(
            "lead-normal", [("full_name", "Normal Person"), ("phone_number", "03001110003")]);

        await using (var db = new AppDbContext(options))
        {
            await SeedMetaPageAsync(db, "page-bounded");
            await MetaIntake(db).RecordAsync(
                DAMS.Application.Tests.Integrations.MetaIntegrationHarness.WebhookBody("page-bounded", "lead-oversized"));
            await MetaIntake(db).RecordAsync(
                DAMS.Application.Tests.Integrations.MetaIntegrationHarness.WebhookBody("page-bounded", "lead-normal"));
        }

        await using (var db = new AppDbContext(options))
            Assert.Equal(2, await MetaProcessor(db, graph).ProcessPendingEventsAsync(10));

        await using (var db = new AppDbContext(options))
        {
            Assert.Equal(2, await db.ExternalIntegrationEvents
                .CountAsync(e => e.Status == ExternalIntegrationEventStatus.Processed));

            var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.ExternalLeadId == "lead-oversized");
            Assert.Equal(longFirst[..100], lead.FirstName);
            Assert.Equal(longLast[..100], lead.LastName);
            Assert.Equal(100, lead.City!.Length);
            Assert.Equal(200, lead.CampaignName!.Length);
            // A contact value or an id cut short would be a different, wrong value, so one that
            // cannot fit is left empty rather than truncated.
            Assert.Null(lead.Phone);
            Assert.Null(lead.Email);
            Assert.Null(lead.CampaignReference);
            Assert.Null(lead.ExternalFormReference);
            // One long campaign name does not crowd the ad and form out of the summary.
            Assert.Contains("campaign: ccc", lead.SourceDetails);
            Assert.Contains("ad: aaa", lead.SourceDetails);
            Assert.Contains("form: 999", lead.SourceDetails);

            var submission = await db.LeadExternalSubmissions.AsNoTracking().SingleAsync(s => s.ExternalLeadId == "lead-oversized");
            Assert.Equal(300, submission.CampaignName!.Length);
            Assert.Equal(300, submission.AdSetName!.Length);
            Assert.Equal(300, submission.AdName!.Length);
            Assert.Null(submission.CampaignExternalId);
            Assert.Null(submission.ExternalFormReference);
            Assert.Contains(longAnswer, submission.FieldDataJson);
            Assert.Contains($"{longFirst} {longLast}", submission.FieldDataJson);
            Assert.Contains(longAnswer, submission.RawPayloadJson);

            Assert.Equal(1, await db.Leads.CountAsync(l => l.ExternalLeadId == "lead-normal"));
        }
    }

    private static readonly MetaIntegrationOptions SqlMetaOptions = new()
    {
        AppId = "sql-app-id",
        AppSecret = "sql-app-secret",
        WebhookVerifyToken = "sql-verify-token",
        BaseRetryDelaySeconds = 60,
        MaxRetryDelayMinutes = 60,
        MaxAttempts = 3,
        AuthRetryDelayHours = 6
    };

    /// <summary>A connected Meta account with one page enabled for lead delivery.</summary>
    private static async Task SeedMetaPageAsync(AppDbContext db, string pageId)
    {
        var protector = new DAMS.Application.Tests.Integrations.PlaintextSecretProtector();
        var connection = new ExternalIntegrationConnection
        {
            Provider = "meta", ExternalAccountId = $"account-{pageId}", DisplayName = "SQL Meta Account",
            Status = ExternalIntegrationConnectionStatus.Connected,
            AccessTokenProtected = protector.Protect("user-token")
        };
        db.ExternalIntegrationConnections.Add(connection);
        await db.SaveChangesAsync();

        db.ExternalIntegrationResources.Add(new ExternalIntegrationResource
        {
            ExternalIntegrationConnectionId = connection.Id,
            Provider = "meta", ResourceType = "facebook_page", ExternalId = pageId, Name = $"Page {pageId}",
            IsEnabled = true, IsActive = true, IsSubscribed = true,
            ResourceTokenProtected = protector.Protect($"page-token-{pageId}")
        });
        await db.SaveChangesAsync();
    }

    private static DAMS.Application.Services.Integrations.MetaWebhookIntakeService MetaIntake(AppDbContext db) =>
        new(db, NullLogger<DAMS.Application.Services.Integrations.MetaWebhookIntakeService>.Instance);

    /// <summary>The event worker on the real lead ingestion path, wired the way the API wires it.</summary>
    private static DAMS.Application.Services.Integrations.MetaLeadEventProcessor MetaProcessor(
        AppDbContext db, DAMS.Application.Tests.Integrations.FakeMetaGraphClient graph,
        MetaIntegrationOptions? metaOptions = null)
    {
        var clock = new LeadTestHarness.FakeClock(DateTime.UtcNow);
        var dispatcher = new NotificationDispatcher(db, new NotificationSettingsStore(db), new NotificationRealtimeBroker(),
            clock, new NotificationEligibilityPolicy(db), NullLogger<NotificationDispatcher>.Instance);
        var customers = new CustomerService(db);
        var leads = new LeadService(db, customers, new BookingService(db, customers, new FinanceAccountService(db)),
            new LeadNotificationService(db, dispatcher),
            Microsoft.Extensions.Options.Options.Create(new LeadAlertOptions()),
            new CustomerAccountLinkService(db, clock));

        return new DAMS.Application.Services.Integrations.MetaLeadEventProcessor(
            db, graph, new DAMS.Application.Tests.Integrations.PlaintextSecretProtector(), leads, dispatcher,
            metaOptions ?? SqlMetaOptions,
            NullLogger<DAMS.Application.Services.Integrations.MetaLeadEventProcessor>.Instance);
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
        // happens to be flat in projects and buckets. One UNION ALL source read plus the deposit
        // balance (3), outstanding, overdue and the project-name lookup.
        Assert.True(eightProjects.Commands <= FinanceService.PeriodAggregateQueryCount + 8,
            $"dashboard issued {eightProjects.Commands} commands; the bound is "
            + $"{FinanceService.PeriodAggregateQueryCount + 8}");
        // Lower bound too: the combined financial-source command itself must still run.
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

    /// <summary>
    /// Account cards used to attach every balance aggregate as a correlated subquery to every
    /// account row. The command count looked harmless (one command), but SQL Server repeatedly
    /// scanned the finance tables for each account. The replacement must keep the base-page and
    /// grouped-movement reads flat while preserving the exact account math.
    /// </summary>
    [SqlServerFact]
    public async Task FinanceAccounts_AggregateInOneSetBasedCommand_WhateverTheAccountVolume()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var counter = new CommandCounter();
        var options = OptionsWith(database.ConnectionString, counter);
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var accounts = new FinanceAccountService(db);
        // This also exercises every system-role branch against the real provider.
        await accounts.SetupClientChartAsync();
        await SeedDashboardVolumeAsync(db, firstProject: 1, projects: 8);

        await accounts.GetPageAsync(null, null, null, null, 0, 200);
        var eightProjects = await MeasureAsync(counter,
            () => accounts.GetPageAsync(null, null, null, null, 0, 200));

        await SeedDashboardVolumeAsync(db, firstProject: 9, projects: 8);
        var sixteenProjects = await MeasureAsync(counter,
            () => accounts.GetPageAsync(null, null, null, null, 0, 200));
        var overview = await MeasureAsync(counter, () => accounts.GetOverviewAsync());

        Assert.Equal(2, eightProjects.Commands);       // base account page + grouped movements
        Assert.Equal(2, sixteenProjects.Commands);     // volume must not add database commands
        Assert.Equal(2, overview.Commands);

        var bank = Assert.Single(sixteenProjects.Result.Items, a => a.Name == "Bank 1");
        Assert.Equal(0m, bank.RevenueReceived);
        Assert.Equal(1_000_000m, bank.ExpensesPaid);   // 12 expenses + 4 asset purchases
        Assert.Equal(-1_000_000m, bank.CurrentBalance);
        Assert.Equal(16, bank.TransactionCount);

        var equipment = Assert.Single(sixteenProjects.Result.Items, a => a.Name == "Equipment 1");
        Assert.Equal(400_000m, equipment.RevenueReceived);
        Assert.Equal(0m, equipment.ExpensesPaid);
        Assert.Equal(400_000m, equipment.CurrentBalance);
        Assert.Equal(4, equipment.TransactionCount);

        Assert.Equal(-16_000_000m, overview.Result.TotalBalance);
        Assert.True(sixteenProjects.Elapsed < TimeSpan.FromSeconds(10),
            $"finance accounts took {sixteenProjects.Elapsed.TotalSeconds:0.00}s at 16 projects");
    }

    /// <summary>
    /// Historical Trial Balance columns used to rerun the complete account snapshot, P&amp;L and
    /// allocation query set once per month. Sixty prior months therefore multiplied roughly three
    /// dozen SQL commands by sixty-one. The batched implementation reads dated source aggregates
    /// once and folds them across cut-offs, so the command count must be independent of column count
    /// on both global and project paths.
    /// </summary>
    [SqlServerFact]
    public async Task TrialBalance_CommandCountIsFlat_AndBatchMatchesSingleColumns_OnRealSqlServer()
    {
        await using var database = await SqlTestDatabase.CreateAsync(commandTimeoutSeconds: 180);
        var counter = new CommandCounter();
        var options = OptionsWith(database.ConnectionString, counter);
        await using var db = new AppDbContext(options);
        // Creating every historical schema on developer SQL Express can exceed the ordinary
        // request timeout under load; the report measurements below restore the production limit.
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
        await db.Database.MigrateAsync();
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));

        var accounts = new FinanceAccountService(db);
        await accounts.SetupClientChartAsync();
        await SeedDashboardVolumeAsync(db, firstProject: 1, projects: 2);
        var projectId = await db.Projects.OrderBy(project => project.Id)
            .Select(project => project.Id).FirstAsync();
        var finance = new FinanceService(db, new NullPrivateStorage(), accounts,
            new WhtService(db, accounts), NullLogger<FinanceService>.Instance);
        var asAt = new DateTime(2026, 12, 31);

        // Warm both query shapes so model/query compilation is outside the measured commands.
        await finance.GetTrialBalanceAsync(null, asAt, 1);
        await finance.GetTrialBalanceAsync(projectId, asAt, 1);

        var global0 = await MeasureAsync(counter, () => finance.GetTrialBalanceAsync(null, asAt, 0));
        var global12 = await MeasureAsync(counter, () => finance.GetTrialBalanceAsync(null, asAt, 12));
        var global60 = await MeasureAsync(counter, () => finance.GetTrialBalanceAsync(null, asAt, 60));
        Assert.Equal(global0.Commands, global12.Commands);
        Assert.Equal(global0.Commands, global60.Commands);
        Assert.True(global60.Commands <= 32,
            $"61-column global trial balance issued {global60.Commands} commands");
        AssertTrialLatestEquivalent(global0.Result, global12.Result);
        AssertTrialLatestEquivalent(global0.Result, global60.Result);

        var project0 = await MeasureAsync(counter, () => finance.GetTrialBalanceAsync(projectId, asAt, 0));
        var project1 = await MeasureAsync(counter, () => finance.GetTrialBalanceAsync(projectId, asAt, 1));
        var project60 = await MeasureAsync(counter, () => finance.GetTrialBalanceAsync(projectId, asAt, 60));
        Assert.Equal(project0.Commands, project1.Commands);
        Assert.Equal(project0.Commands, project60.Commands);
        Assert.True(project60.Commands <= 26,
            $"61-column project trial balance issued {project60.Commands} commands");
        AssertTrialLatestEquivalent(project0.Result, project60.Result);
    }

    private static void AssertTrialLatestEquivalent(TrialBalanceDto single, TrialBalanceDto batch)
    {
        Assert.Single(single.ColumnDates);
        Assert.Equal(single.AsAt, batch.AsAt);
        Assert.Equal(single.TotalDebit, batch.TotalDebit);
        Assert.Equal(single.TotalCredit, batch.TotalCredit);
        Assert.Equal(single.IsBalanced, batch.IsBalanced);

        foreach (var expected in single.Rows)
        {
            var actual = Assert.Single(batch.Rows, row => row.AccountKey == expected.AccountKey);
            Assert.Equal(expected.AccountId, actual.AccountId);
            Assert.Equal(expected.LedgerCode, actual.LedgerCode);
            Assert.Equal(expected.AccountName, actual.AccountName);
            Assert.Equal(expected.Type, actual.Type);
            Assert.Equal(expected.Debit, actual.Debit);
            Assert.Equal(expected.Credit, actual.Credit);
        }

        // A historical virtual row legitimately remains in a multi-column report after it returns
        // to zero. Such a row is absent from the standalone final column, but its final cells must
        // still be exactly zero.
        var singleKeys = single.Rows.Select(row => row.AccountKey).ToHashSet(StringComparer.Ordinal);
        Assert.All(batch.Rows.Where(row => !singleKeys.Contains(row.AccountKey)), row =>
        {
            Assert.Equal(0m, row.Debit);
            Assert.Equal(0m, row.Credit);
        });
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
    /// Staff invitations make Users.Password nullable and add an account status. Every staff
    /// login that already existed must still be Active afterwards — a numbering or default-value
    /// mistake here locks every Admin, Manager and Employee out of the system.
    /// <para>
    /// Clients are the deliberate exception since AddClientEmailVerification: a login DAMS has
    /// never proven the mailbox for is moved to PendingEmailVerification and its session cleared,
    /// so it has to re-verify. That is the point of that migration, not a regression of this one —
    /// what this test still guards is that the move stops at the Client role.
    /// </para>
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

        // Both a staff and a client login carry a live session, so the migration's decision to
        // clear one and not the other is observable rather than assumed.
        await ExecuteAsync(database.ConnectionString, """
            INSERT INTO [Users] ([RoleId], [FullName], [Email], [Password], [RefreshToken], [RefreshTokenExpiresAt]) VALUES
                (1, N'Legacy Admin',    N'admin@dams.test',   N'$2a$11$legacyadminhash',   N'admin-session',  DATEADD(day, 7, SYSUTCDATETIME())),
                (2, N'Legacy Client',   N'client@dams.test',  N'$2a$11$legacyclienthash',  N'client-session', DATEADD(day, 7, SYSUTCDATETIME())),
                (3, N'Legacy Manager',  N'manager@dams.test', N'$2a$11$legacymanagerhash', NULL, NULL),
                (4, N'Legacy Employee', N'sales@dams.test',   N'$2a$11$legacysaleshash',   NULL, NULL);
            """);

        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        // The explicit backfill, not a CLR or column default, is what has to hold here. The one
        // row that is no longer Active is the Client, and only because a later migration moved it.
        Assert.Equal(0, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [Users] WHERE [RoleId] <> 2 AND [AccountStatus] <> 0"));
        Assert.Equal(0, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [Users] WHERE [RoleId] = 2 AND [AccountStatus] <> 3"));
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
            var legacy = await db.Users.ToListAsync();
            Assert.All(legacy.Where(u => u.RoleId != 2),
                u => Assert.Equal(UserAccountStatus.Active, u.AccountStatus));

            // The Client is pending, unverified, and holds no session it could refresh with.
            var legacyClient = Assert.Single(legacy, u => u.RoleId == 2);
            Assert.Equal(UserAccountStatus.PendingEmailVerification, legacyClient.AccountStatus);
            Assert.Null(legacyClient.EmailVerifiedAt);
            Assert.Null(legacyClient.RefreshToken);
            Assert.Null(legacyClient.RefreshTokenExpiresAt);

            // The staff session is untouched: this migration signs nobody on the staff side out.
            var legacyAdmin = Assert.Single(legacy, u => u.RoleId == 1);
            Assert.Equal("admin-session", legacyAdmin.RefreshToken);
            Assert.NotNull(legacyAdmin.RefreshTokenExpiresAt);

            adminUserId = legacyAdmin.UserId;

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
    /// Rolling back must never invent a credential. Restoring Users.Password to NOT NULL makes
    /// EF emit "UPDATE [Users] SET [Password] = N'' WHERE [Password] IS NULL" first, which does
    /// not fail — it stamps an empty string onto every login that was invited but has not
    /// activated, and the next statement drops the AccountStatus column that was the only thing
    /// recording that they were waiting. Down() refuses instead, and only while such a login
    /// exists: with none outstanding the rollback still runs, and real password hashes survive.
    /// </summary>
    [SqlServerFact]
    public async Task StaffInvitationMigration_RefusesRollbackRatherThanStampAPasswordOnAnUnactivatedLogin()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        const string previous = "20260821020414_AddEmployeeSalaryRowVersion";

        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        await ExecuteAsync(database.ConnectionString, """
            INSERT INTO [Users] ([RoleId], [FullName], [Email], [Password], [AccountStatus]) VALUES
                (1, N'Legacy Admin',  N'admin@dams.test',   N'$2a$11$legacyadminhash', 0),
                (4, N'Invited Sales', N'invited@dams.test', NULL,                      1);
            """);

        await using (var db = new AppDbContext(options))
        {
            var refused = await Assert.ThrowsAnyAsync<SqlException>(
                () => db.GetService<IMigrator>().MigrateAsync(previous));
            Assert.Contains("never activated", refused.Message, StringComparison.OrdinalIgnoreCase);
        }

        // The refusal rolled back cleanly: the schema and the waiting login are untouched, and
        // in particular no empty string was written over the missing password.
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StaffInvitations'"));
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [Users] WHERE [Email] = N'invited@dams.test' AND [Password] IS NULL AND [AccountStatus] = 1"));

        // Resolve the waiting invitation the way an operator would have to, and the rollback
        // proceeds — the guard blocks an unsafe rollback, not every rollback.
        await ExecuteAsync(database.ConnectionString,
            "DELETE FROM [Users] WHERE [Email] = N'invited@dams.test';");

        await using (var db = new AppDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync(previous);

        Assert.Equal(0, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StaffInvitations'"));
        Assert.Equal(0, await ScalarAsync(database.ConnectionString, """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'AccountStatus'
            """));
        // The account that always had a password still has exactly the one it had.
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [Users] WHERE [Email] = N'admin@dams.test' AND [Password] = N'$2a$11$legacyadminhash'"));
    }

    /// <summary>
    /// "At most one usable invitation per login" has to be a property of the database, not of
    /// the order two requests happen to arrive in. Two Admins pressing Resend at the same
    /// instant each read the outstanding set, each revoke what they saw, and each insert a
    /// replacement — leaving two live activation links for one account unless the read and the
    /// write are one serialisable step.
    /// </summary>
    [SqlServerFact]
    public async Task ConcurrentStaffIssuance_OnlyOneValidInvitationOutstandingPerUser()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int invitedUserId;
        int adminUserId;
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
            await db.SaveChangesAsync();

            invitedUserId = invited.UserId;
            adminUserId = admin.UserId;
        }

        async Task<StaffInvitationResult> IssueAsync()
        {
            await using var context = new AppDbContext(options);
            var service = new StaffInvitationService(
                context, new NotificationSettingsStore(context),
                new ThrowingEmailSender(), TimeProvider.System);
            return await service.IssueAsync(invitedUserId, adminUserId);
        }

        // Two concurrent requests to issue/resend invitations for the same user.
        // Only one should result in a valid outstanding token at commit time.
        var outcomes = await Task.WhenAll(IssueAsync(), IssueAsync());

        Assert.All(outcomes, o => Assert.True(o.Issued, "Both invitations should be committed"));

        await using (var db = new AppDbContext(options))
        {
            // Count outstanding invitations for this user: should be exactly one.
            var outstanding = await db.StaffInvitations
                .Where(i => i.UserId == invitedUserId && i.AcceptedAt == null && i.RevokedAt == null)
                .CountAsync();
            Assert.Equal(1, outstanding);
        }
    }

    /// <summary>
    /// The other half of the race: a resend that read the login as Invited must not be able to
    /// mint a link after an activation has made that login Active. Whichever order they land in,
    /// an Active account must be left with no usable invitation behind it.
    /// </summary>
    [SqlServerFact]
    public async Task ResendRacingActivation_NeverLeavesAUsableLinkOnAnActivatedAccount()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        const string rawToken = "resend-versus-activation-token";
        int invitedUserId;
        int adminUserId;
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
                TokenHash = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(rawToken))),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();

            invitedUserId = invited.UserId;
            adminUserId = admin.UserId;
        }

        async Task<StaffActivationResult> ActivateAsync()
        {
            await using var context = new AppDbContext(options);
            return await NewService(context).ActivateAsync(rawToken, "chosen-by-the-employee");
        }

        async Task<StaffInvitationResult> ResendAsync()
        {
            await using var context = new AppDbContext(options);
            return await NewService(context).ResendAsync(invitedUserId, adminUserId);
        }

        StaffInvitationService NewService(AppDbContext context) => new(
            context, new NotificationSettingsStore(context),
            new ThrowingEmailSender(), TimeProvider.System);

        var activation = ActivateAsync();
        var resend = ResendAsync();
        await Task.WhenAll((Task)activation, resend);

        await using (var db = new AppDbContext(options))
        {
            var user = await db.Users.SingleAsync(u => u.UserId == invitedUserId);

            // Whoever won, the invariant is the same: an Active account has nothing outstanding,
            // and an account still Invited has exactly one link to finish with.
            var usable = await db.StaffInvitations
                .CountAsync(i => i.UserId == invitedUserId && i.AcceptedAt == null && i.RevokedAt == null);

            if (user.AccountStatus == UserAccountStatus.Active)
            {
                Assert.NotNull(user.Password);
                Assert.Equal(0, usable);
                // The resend, if it ran second, must have been refused rather than minting a link
                // for an account that already has a password.
                Assert.False((await resend).Issued && usable > 0);
            }
            else
            {
                Assert.Equal(UserAccountStatus.Invited, user.AccountStatus);
                Assert.Null(user.Password);
                Assert.Equal(1, usable);
            }
        }
    }

    /// <summary>
    /// Activation refuses an employee who is no longer active, so issuing to one would email a
    /// credential that is dead on arrival. Both halves must agree.
    /// </summary>
    [SqlServerFact]
    public async Task AnInvitationIsRefusedForAnEmployeeWhoIsNoLongerActive()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        int invitedUserId;
        int adminUserId;
        await using (var db = new AppDbContext(options))
        {
            var admin = new User
            {
                RoleId = 1, FullName = "SQL Admin", Email = "sql-admin@dams.test",
                Password = "$2a$11$adminhash", AccountStatus = UserAccountStatus.Active
            };
            var invited = new User
            {
                RoleId = 4, FullName = "SQL Left", Email = "sql-left@dams.test",
                Password = null, AccountStatus = UserAccountStatus.Invited
            };
            db.Users.AddRange(admin, invited);
            await db.SaveChangesAsync();

            db.Employees.Add(new Employee
            {
                FullName = "SQL Left", UserId = invited.UserId, Email = invited.Email,
                JobTitle = "Sales Executive", JoinDate = new DateTime(2026, 1, 1),
                Status = EmployeeStatus.Terminated
            });
            await db.SaveChangesAsync();

            invitedUserId = invited.UserId;
            adminUserId = admin.UserId;
        }

        await using (var context = new AppDbContext(options))
        {
            var service = new StaffInvitationService(
                context, new NotificationSettingsStore(context),
                new ThrowingEmailSender(), TimeProvider.System);

            var issued = await service.IssueAsync(invitedUserId, adminUserId);

            Assert.False(issued.Issued);
            Assert.Equal(StaffInvitationFailure.EmployeeNotActive, issued.Failure);
        }

        await using (var db = new AppDbContext(options))
            Assert.Equal(0, await db.StaffInvitations.CountAsync(i => i.UserId == invitedUserId));
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

    // The migration immediately before commission accruals, so a test can start from the exact
    // state a real upgrade starts from and let this migration do the work.
    private const string BeforeCommissionAccruals = "20260905212713_AddRebateCreditAllocations";

    /// <summary>
    /// The upgrade reconstructs each commission's HISTORY, not just its current balance.
    /// <para>
    /// Stamping today's final amount at the creation date would have been wrong in the two ways that
    /// matter and invisible in both: a commission raised from 100,000 to 130,000 in a later month
    /// would report 130,000 in the earlier one and nothing in the later, and one cancelled in a later
    /// month would report no expense in the month it was actually agreed. Today's payable reconciles
    /// either way, which is exactly why this asserts each PERIOD rather than the closing balance.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task CommissionAccrualMigration_RebuildsEachPeriodFromTheAuditHistory()
    {
        await using var database = await SqlTestDatabase.CreateAsync(commandTimeoutSeconds: 180);
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
        {
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
            await db.GetService<IMigrator>().MigrateAsync(BeforeCommissionAccruals);
        }

        // Three commissions on one booking, written into the OLD schema: one raised in September,
        // one cancelled in September, one untouched — with the dated audit rows a real system would
        // have left behind. NewCommissionStatus is what separates an EDIT adjustment from the one
        // recorded as part of the original entry, which is the discriminator the backfill relies on.
        await using (var seed = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "Accrual History", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit { Project = project, UnitNumber = "H-1", UnitType = "Apartment", Price = 5_000_000m };
            var customer = new Customer { FullName = "History Buyer", Phone = "03001110000" };
            var booking = new Booking
            {
                BookingReference = "BK-HIST-1", Customer = customer, Unit = unit,
                Status = BookingStatus.PaymentPlanActive, Source = CustomerSource.Referral,
                AgreedSalePrice = 5_000_000m, BookingAmountRequired = 500_000m,
                BookingAmountReceived = 500_000m, BookingDate = new DateTime(2026, 8, 1)
            };
            // One partner each: a booking holds at most one live commission per partner.
            var partners = Enumerable.Range(1, 3).Select(n => new ThirdPartyPartner
            {
                Name = $"History Broker {n}", PartnerType = "Broker", InternalCode = $"HB-{n}", IsActive = true
            }).ToArray();
            seed.AddRange(project, unit, customer, booking);
            seed.ThirdPartyPartners.AddRange(partners);
            await seed.SaveChangesAsync();

            BookingCommission Commission(ThirdPartyPartner partner, decimal amount,
                BookingCommissionStatus status, DateTime createdAt) => new()
            {
                BookingId = booking.Id, PartnerId = partner.Id, PartnerNameSnapshot = partner.Name,
                PartnerTypeSnapshot = partner.PartnerType, PartnerInternalCodeSnapshot = partner.InternalCode,
                CalculationType = FinancialCalculationType.FixedAmount,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                FixedAmount = amount, BasisAmount = 5_000_000m, CalculatedAmount = amount,
                FinalAmount = amount, Status = status, CreatedAt = createdAt
            };
            var raised = Commission(partners[0], 130_000m, BookingCommissionStatus.Pending, new DateTime(2026, 8, 5, 6, 0, 0, DateTimeKind.Utc));
            var cancelled = Commission(partners[1], 70_000m, BookingCommissionStatus.Cancelled, new DateTime(2026, 8, 6, 6, 0, 0, DateTimeKind.Utc));
            var steady = Commission(partners[2], 40_000m, BookingCommissionStatus.Pending, new DateTime(2026, 8, 7, 6, 0, 0, DateTimeKind.Utc));
            seed.BookingCommissions.AddRange(raised, cancelled, steady);
            await seed.SaveChangesAsync();

            FinancialWorkflowAuditEntry Entry(BookingCommission commission, FinancialWorkflowAction action,
                decimal? previous, decimal? current, BookingCommissionStatus? newStatus, DateTime occurredAt) => new()
            {
                CommissionId = commission.Id, BookingId = booking.Id, Action = action,
                PreviousAmount = previous, NewAmount = current, NewCommissionStatus = newStatus,
                OccurredAt = occurredAt
            };
            seed.FinancialWorkflowAuditEntries.AddRange(
                Entry(raised, FinancialWorkflowAction.CommissionCreated, null, 100_000m, null, new DateTime(2026, 8, 5, 6, 0, 0, DateTimeKind.Utc)),
                Entry(raised, FinancialWorkflowAction.CommissionAdjusted, 100_000m, 130_000m, BookingCommissionStatus.Pending, new DateTime(2026, 9, 10, 6, 0, 0, DateTimeKind.Utc)),
                Entry(cancelled, FinancialWorkflowAction.CommissionCreated, null, 70_000m, null, new DateTime(2026, 8, 6, 6, 0, 0, DateTimeKind.Utc)),
                Entry(cancelled, FinancialWorkflowAction.CommissionCancelled, 70_000m, 70_000m, BookingCommissionStatus.Cancelled, new DateTime(2026, 9, 12, 6, 0, 0, DateTimeKind.Utc)),
                Entry(steady, FinancialWorkflowAction.CommissionCreated, null, 40_000m, null, new DateTime(2026, 8, 7, 6, 0, 0, DateTimeKind.Utc)));
            await seed.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
            await db.Database.MigrateAsync();
        }

        await using var context = new AppDbContext(options);
        var accounts = new FinanceAccountService(context);
        var finance = new FinanceService(context, new NullPrivateStorage(), accounts,
            new WhtService(context, accounts), NullLogger<FinanceService>.Instance);

        // August is the month all three were AGREED: 100,000 + 70,000 + 40,000. Not 130,000 for the
        // first, and not zero for the cancelled one.
        var august = await finance.GetProfitAndLossAsync(null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
        Assert.Equal(210_000m, Assert.Single(august.ExpenseLines, l => l.Name == "Partner Commissions").Amount);

        // September holds only what happened in September: +30,000 raised, −70,000 released.
        var september = await finance.GetProfitAndLossAsync(null, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));
        Assert.Equal(-40_000m, Assert.Single(september.ExpenseLines, l => l.Name == "Partner Commissions").Amount);

        // …and today's payable is still exactly what is owed: 130,000 + 40,000.
        var rows = context.CommissionAccruals.AsNoTracking().ToList();
        Assert.Equal(170_000m, rows.Sum(a => a.Amount));
        Assert.Contains(rows, a => a.Kind == CommissionAccrualKind.Adjustment && a.Amount == 30_000m);
        Assert.Contains(rows, a => a.Kind == CommissionAccrualKind.Release && a.Amount == -70_000m);
        // No cutover correction was needed: the audit history explained every commission in full.
        Assert.DoesNotContain(rows, a => a.Reason != null && a.Reason.StartsWith("Cutover correction"));
    }

    /// <summary>
    /// Before the approval ladder was simplified, an approver could settle a commission or rebate at
    /// an amount different from what it was calculated at — the payout's real target was
    /// <c>ApprovedAmount ?? FinalAmount</c>. The migration that removed the ladder dropped
    /// <c>ApprovedAmount</c> without folding it into <c>FinalAmount</c>, so a Paid commission from
    /// that era can have a <c>FinalAmount</c> that does not match what was ever actually paid — the
    /// exact defect this seeds directly against the pre-accrual schema, since nothing on today's
    /// application code path can recreate it any more.
    /// </summary>
    [SqlServerFact]
    public async Task CommissionAccrualMigration_RestoresFinalAmountForRecordsAnOldApprovalOverrideSettledDifferently()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync(BeforeCommissionAccruals);

        int bookingId, underApprovedId, overApprovedId, alreadyCorrectId, neverPaidId, rebateId;
        await using (var seed = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "Approval Override", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit { Project = project, UnitNumber = "AO-1", UnitType = "Apartment", Price = 5_000_000m };
            var customer = new Customer { FullName = "Override Buyer", Phone = "03001112222" };
            var booking = new Booking
            {
                BookingReference = "BK-OVERRIDE-1", Customer = customer, Unit = unit,
                Status = BookingStatus.PaymentPlanActive, Source = CustomerSource.Referral,
                AgreedSalePrice = 5_000_000m, BookingAmountRequired = 500_000m,
                BookingAmountReceived = 500_000m, BookingDate = new DateTime(2026, 7, 1)
            };
            var account = new FinanceAccount
            {
                Name = "Override Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true
            };
            var partners = Enumerable.Range(1, 4).Select(n => new ThirdPartyPartner
            {
                Name = $"Override Broker {n}", PartnerType = "Broker", InternalCode = $"OB-{n}", IsActive = true
            }).ToArray();
            seed.AddRange(project, unit, customer, booking, account);
            seed.ThirdPartyPartners.AddRange(partners);
            await seed.SaveChangesAsync();

            BookingCommission Commission(ThirdPartyPartner partner, decimal finalAmount) => new()
            {
                BookingId = booking.Id, PartnerId = partner.Id, PartnerNameSnapshot = partner.Name,
                PartnerTypeSnapshot = partner.PartnerType, PartnerInternalCodeSnapshot = partner.InternalCode,
                CalculationType = FinancialCalculationType.FixedAmount,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                FixedAmount = finalAmount, BasisAmount = 5_000_000m, CalculatedAmount = finalAmount,
                FinalAmount = finalAmount, Status = BookingCommissionStatus.Pending, CreatedAt = new DateTime(2026, 7, 5)
            };
            // Approved below the calculated figure: paid in full at 90,000, but FinalAmount was
            // never told and still says 100,000.
            var underApproved = Commission(partners[0], 100_000m); underApproved.Status = BookingCommissionStatus.Paid;
            // Approved above the calculated figure: paid in full at 110,000 against a FinalAmount
            // still reading 100,000 — the shape that produces a NEGATIVE payable.
            var overApproved = Commission(partners[1], 100_000m); overApproved.Status = BookingCommissionStatus.Paid;
            // A normal, never-overridden Paid commission: FinalAmount already matches its one
            // payout. The control that proves the fix leaves ordinary records alone.
            var alreadyCorrect = Commission(partners[2], 60_000m); alreadyCorrect.Status = BookingCommissionStatus.Paid;
            // Still open — a partial historical payout here does not reveal the true target, so
            // this must be left untouched no matter what its payouts total.
            var neverPaid = Commission(partners[3], 40_000m);
            seed.BookingCommissions.AddRange(underApproved, overApproved, alreadyCorrect, neverPaid);
            await seed.SaveChangesAsync();
            bookingId = booking.Id;
            underApprovedId = underApproved.Id; overApprovedId = overApproved.Id;
            alreadyCorrectId = alreadyCorrect.Id; neverPaidId = neverPaid.Id;

            CommissionPayout Payout(BookingCommission commission, decimal amount) => new()
            {
                CommissionId = commission.Id, FinanceAccountId = account.Id, Amount = amount,
                PaymentDate = new DateTime(2026, 7, 10), PaymentMethod = PaymentMethod.Cash,
                IdempotencyKey = $"seed-payout-{commission.Id}", RecordedAt = new DateTime(2026, 7, 10)
            };
            seed.CommissionPayouts.AddRange(
                Payout(underApproved, 90_000m), Payout(overApproved, 110_000m),
                Payout(alreadyCorrect, 60_000m), Payout(neverPaid, 15_000m));

            var rebate = new CustomerRebate
            {
                BookingId = booking.Id, CustomerId = customer.Id, CalculationType = FinancialCalculationType.FixedAmount,
                FixedAmount = 50_000m, CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                BasisAmount = 5_000_000m, CalculatedAmount = 50_000m, FinalAmount = 50_000m,
                Reason = "Loyalty rebate", Method = CustomerRebateMethod.OutstandingBalanceReduction,
                Status = CustomerRebateStatus.Applied, CreatedAt = new DateTime(2026, 7, 5)
            };
            seed.CustomerRebates.Add(rebate);
            await seed.SaveChangesAsync();
            rebateId = rebate.Id;
            seed.RebateDisbursements.Add(new RebateDisbursement
            {
                RebateId = rebate.Id, Method = CustomerRebateMethod.OutstandingBalanceReduction, Amount = 45_000m,
                AppliedAt = new DateTime(2026, 7, 10), IdempotencyKey = "seed-disbursement",
                RecordedAt = new DateTime(2026, 7, 10)
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        await using var verify = new AppDbContext(options);
        var under = await verify.BookingCommissions.SingleAsync(c => c.Id == underApprovedId);
        Assert.Equal(90_000m, under.FinalAmount);
        Assert.Equal(-10_000m, under.AdjustmentAmount);
        Assert.Contains("Data correction", under.AdjustmentReason);

        var over = await verify.BookingCommissions.SingleAsync(c => c.Id == overApprovedId);
        Assert.Equal(110_000m, over.FinalAmount);
        Assert.Equal(10_000m, over.AdjustmentAmount);

        var correct = await verify.BookingCommissions.SingleAsync(c => c.Id == alreadyCorrectId);
        Assert.Equal(60_000m, correct.FinalAmount);
        Assert.Equal(0m, correct.AdjustmentAmount);
        Assert.Null(correct.AdjustmentReason);

        var open = await verify.BookingCommissions.SingleAsync(c => c.Id == neverPaidId);
        Assert.Equal(40_000m, open.FinalAmount);
        Assert.Equal(0m, open.AdjustmentAmount);
        Assert.Null(open.AdjustmentReason);

        var fixedRebate = await verify.CustomerRebates.SingleAsync(r => r.Id == rebateId);
        Assert.Equal(45_000m, fixedRebate.FinalAmount);
        Assert.Equal(-5_000m, fixedRebate.AdjustmentAmount);

        // The backfill ran AFTER the correction, so each corrected commission's accrual — its own
        // Recognition, since none of these has an audit trail — already equals what was truly paid.
        // A correctly paid commission shows no remaining balance, and none goes negative.
        Assert.Equal(90_000m, await verify.CommissionAccruals.Where(a => a.CommissionId == underApprovedId).SumAsync(a => a.Amount));
        Assert.Equal(110_000m, await verify.CommissionAccruals.Where(a => a.CommissionId == overApprovedId).SumAsync(a => a.Amount));
        Assert.Equal(60_000m, await verify.CommissionAccruals.Where(a => a.CommissionId == alreadyCorrectId).SumAsync(a => a.Amount));
        // No cutover correction was needed for any of them post-fix: the corrected FinalAmount and
        // the one Recognition row it produced already agree.
        Assert.DoesNotContain(await verify.CommissionAccruals.Where(a => a.CommissionId == underApprovedId
            || a.CommissionId == overApprovedId || a.CommissionId == alreadyCorrectId).ToListAsync(),
            a => a.Reason != null && a.Reason.StartsWith("Cutover correction"));
    }

    /// <summary>
    /// The reconstruction reads what settled NET of reversals, and reaches records that are still
    /// open — in the period the decision was actually made.
    /// <para>
    /// Summing the payout rows alone counts a reversed payment as though it had stayed paid, so a
    /// commission paid, partly reversed and re-paid was restored as an obligation bigger than
    /// anything that ever left the bank: money still showing as owed on a commission that is square,
    /// and no payout able to clear it.
    /// </para>
    /// <para>
    /// A record still open was skipped altogether, on the grounds that a part payment cannot reveal
    /// its target. True of the payouts — but not of the audit log, where the approval that SET the
    /// target wrote it down. A commission calculated at 100,000, approved at 80,000 and paid 30,000
    /// owes 50,000, and was left claiming 70,000.
    /// </para>
    /// <para>
    /// The date matters as much as the amount: an approval that cut a commission in July belongs in
    /// July. Reconstructed at cutover instead, it takes the cut out of the current period, so the
    /// month that made the decision keeps overstating its profit for ever and today's understates
    /// its own — while every current balance still reconciles and hides it.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task CommissionAccrualMigration_NetsReversalsAndRebuildsOpenRecordsFromTheDatedApprovalLog()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync(BeforeCommissionAccruals);

        var agreed = new DateTime(2026, 7, 5, 9, 0, 0);
        var approved = new DateTime(2026, 7, 20, 9, 0, 0);
        var edited = new DateTime(2026, 7, 25, 9, 0, 0);
        int repaidId, openApprovedId, editedAfterApprovalId, approvedBelowPaidId, openRebateId, reversedRebateId;

        await using (var seed = new AppDbContext(options))
        {
            var project = new Project { ProjectName = "Net Of Reversals", Location = "Lahore", CreatedById = 1 };
            var unit = new Unit { Project = project, UnitNumber = "NR-1", UnitType = "Apartment", Price = 5_000_000m };
            var secondUnit = new Unit { Project = project, UnitNumber = "NR-2", UnitType = "Apartment", Price = 5_000_000m };
            var customer = new Customer { FullName = "Net Buyer", Phone = "03004445555" };
            Booking NewBooking(string reference, Unit on) => new()
            {
                BookingReference = reference, Customer = customer, Unit = on,
                Status = BookingStatus.PaymentPlanActive, Source = CustomerSource.Referral,
                AgreedSalePrice = 5_000_000m, BookingAmountRequired = 500_000m,
                BookingAmountReceived = 500_000m, BookingDate = new DateTime(2026, 7, 1)
            };
            var booking = NewBooking("BK-NETREV-1", unit);
            // A second booking only because one live rebate per booking is a unique index, and this
            // needs an open one and a settled one at the same time.
            var secondBooking = NewBooking("BK-NETREV-2", secondUnit);
            var account = new FinanceAccount
            {
                Name = "Reversal Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true
            };
            var partners = Enumerable.Range(1, 4).Select(n => new ThirdPartyPartner
            {
                Name = $"Net Broker {n}", PartnerType = "Broker", InternalCode = $"NB-{n}", IsActive = true
            }).ToArray();
            seed.AddRange(project, unit, secondUnit, customer, booking, secondBooking, account);
            seed.ThirdPartyPartners.AddRange(partners);
            await seed.SaveChangesAsync();

            BookingCommission Commission(ThirdPartyPartner partner, decimal finalAmount) => new()
            {
                BookingId = booking.Id, PartnerId = partner.Id, PartnerNameSnapshot = partner.Name,
                PartnerTypeSnapshot = partner.PartnerType, PartnerInternalCodeSnapshot = partner.InternalCode,
                CalculationType = FinancialCalculationType.FixedAmount,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                FixedAmount = finalAmount, BasisAmount = 5_000_000m, CalculatedAmount = finalAmount,
                FinalAmount = finalAmount, Status = BookingCommissionStatus.Pending, CreatedAt = agreed
            };
            // Settled at 90,000 the hard way: 100,000 paid, 20,000 of it reversed, 10,000 re-paid.
            // The payout rows sum to 110,000 and only the reversal says otherwise.
            var repaid = Commission(partners[0], 100_000m); repaid.Status = BookingCommissionStatus.Paid;
            // Open, part paid, and approved below its calculated figure. Only the log knows.
            var openApproved = Commission(partners[1], 100_000m);
            // Approved at 80,000, then EDITED to 120,000 — which cleared the override as it went, so
            // the approval is stale evidence and FinalAmount is already right.
            var editedAfterApproval = Commission(partners[2], 120_000m);
            // Approved at 20,000 but 30,000 has already gone out. Nothing here can explain that, and
            // trusting the approval would leave the partner owing money on an open commission.
            var approvedBelowPaid = Commission(partners[3], 100_000m);
            seed.BookingCommissions.AddRange(repaid, openApproved, editedAfterApproval, approvedBelowPaid);
            await seed.SaveChangesAsync();
            repaidId = repaid.Id; openApprovedId = openApproved.Id;
            editedAfterApprovalId = editedAfterApproval.Id; approvedBelowPaidId = approvedBelowPaid.Id;

            CommissionPayout Payout(BookingCommission commission, decimal amount, string suffix) => new()
            {
                CommissionId = commission.Id, FinanceAccountId = account.Id, Amount = amount,
                PaymentDate = new DateTime(2026, 7, 10), PaymentMethod = PaymentMethod.Cash,
                IdempotencyKey = $"net-payout-{commission.Id}-{suffix}", RecordedAt = new DateTime(2026, 7, 10)
            };
            var firstPayout = Payout(repaid, 100_000m, "a");
            seed.CommissionPayouts.AddRange(firstPayout, Payout(repaid, 10_000m, "b"),
                Payout(openApproved, 30_000m, "a"), Payout(approvedBelowPaid, 30_000m, "a"));
            await seed.SaveChangesAsync();
            seed.CommissionPayoutReversals.Add(new CommissionPayoutReversal
            {
                PayoutId = firstPayout.Id, Amount = 20_000m, Reason = "Overpaid in error",
                IdempotencyKey = "net-reversal-a", ReversedAt = new DateTime(2026, 7, 12)
            });

            FinancialWorkflowAuditEntry Entry(int commissionId, FinancialWorkflowAction action,
                decimal previous, decimal next, DateTime at, BookingCommissionStatus? status = null) => new()
            {
                CommissionId = commissionId, BookingId = booking.Id, CustomerId = customer.Id, Action = action,
                PreviousAmount = previous, NewAmount = next, OccurredAt = at, NewCommissionStatus = status,
                PerformedByName = "Legacy approver"
            };
            seed.FinancialWorkflowAuditEntries.AddRange(
                Entry(openApproved.Id, FinancialWorkflowAction.CommissionCreated, 0m, 100_000m, agreed),
                Entry(openApproved.Id, FinancialWorkflowAction.CommissionApproved, 100_000m, 80_000m, approved),
                Entry(editedAfterApproval.Id, FinancialWorkflowAction.CommissionCreated, 0m, 100_000m, agreed),
                Entry(editedAfterApproval.Id, FinancialWorkflowAction.CommissionApproved, 100_000m, 80_000m, approved),
                // The edit: a status pair is what separates it from the adjustment written at entry.
                Entry(editedAfterApproval.Id, FinancialWorkflowAction.CommissionAdjusted, 100_000m, 120_000m,
                    edited, BookingCommissionStatus.Pending),
                Entry(approvedBelowPaid.Id, FinancialWorkflowAction.CommissionApproved, 100_000m, 20_000m, approved));
            await seed.SaveChangesAsync();

            CustomerRebate Rebate(Booking on, decimal finalAmount, CustomerRebateStatus status) => new()
            {
                BookingId = on.Id, CustomerId = customer.Id, CalculationType = FinancialCalculationType.FixedAmount,
                FixedAmount = finalAmount, CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                BasisAmount = 5_000_000m, CalculatedAmount = finalAmount, FinalAmount = finalAmount,
                Reason = "Loyalty rebate", Method = CustomerRebateMethod.OutstandingBalanceReduction,
                Status = status, CreatedAt = agreed
            };
            var openRebate = Rebate(booking, 50_000m, CustomerRebateStatus.Pending);
            var reversedRebate = Rebate(secondBooking, 50_000m, CustomerRebateStatus.Applied);
            seed.CustomerRebates.AddRange(openRebate, reversedRebate);
            await seed.SaveChangesAsync();
            openRebateId = openRebate.Id; reversedRebateId = reversedRebate.Id;

            var reversedDisbursement = new RebateDisbursement
            {
                RebateId = reversedRebate.Id, Method = CustomerRebateMethod.OutstandingBalanceReduction,
                Amount = 45_000m, AppliedAt = new DateTime(2026, 7, 10), IdempotencyKey = "net-disbursement-b",
                RecordedAt = new DateTime(2026, 7, 10)
            };
            seed.RebateDisbursements.AddRange(new RebateDisbursement
            {
                RebateId = openRebate.Id, Method = CustomerRebateMethod.OutstandingBalanceReduction,
                Amount = 10_000m, AppliedAt = new DateTime(2026, 7, 10), IdempotencyKey = "net-disbursement-a",
                RecordedAt = new DateTime(2026, 7, 10)
            }, reversedDisbursement);
            await seed.SaveChangesAsync();
            seed.RebateDisbursementReversals.Add(new RebateDisbursementReversal
            {
                DisbursementId = reversedDisbursement.Id, Amount = 5_000m, Reason = "Credit withdrawn",
                IdempotencyKey = "net-disbursement-reversal", ReversedAt = new DateTime(2026, 7, 12)
            });
            seed.FinancialWorkflowAuditEntries.Add(new FinancialWorkflowAuditEntry
            {
                RebateId = openRebate.Id, BookingId = booking.Id, CustomerId = customer.Id,
                Action = FinancialWorkflowAction.RebateApproved, PreviousAmount = 50_000m, NewAmount = 35_000m,
                OccurredAt = approved, PerformedByName = "Legacy approver"
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        await using var verify = new AppDbContext(options);

        // 110,000 of payouts, 20,000 reversed: the obligation is the 90,000 that stayed paid.
        var settledNet = await verify.BookingCommissions.SingleAsync(c => c.Id == repaidId);
        Assert.Equal(90_000m, settledNet.FinalAmount);
        Assert.Equal(-10_000m, settledNet.AdjustmentAmount);
        Assert.Equal(90_000m, await verify.CommissionAccruals.Where(a => a.CommissionId == repaidId).SumAsync(a => a.Amount));

        // Open and part paid: the approval names the target, leaving 50,000 owed rather than 70,000.
        var open = await verify.BookingCommissions.SingleAsync(c => c.Id == openApprovedId);
        Assert.Equal(80_000m, open.FinalAmount);
        Assert.Equal(-20_000m, open.AdjustmentAmount);
        Assert.Contains("Data correction", open.AdjustmentReason);

        var openLedger = await verify.CommissionAccruals.Where(a => a.CommissionId == openApprovedId)
            .OrderBy(a => a.AccruedOn).ToListAsync();
        Assert.Equal(80_000m, openLedger.Sum(a => a.Amount));
        // Recognised in full when it was agreed, cut on the day it was approved — not at cutover.
        var recognition = Assert.Single(openLedger.Where(a => a.Kind == CommissionAccrualKind.Recognition));
        Assert.Equal(100_000m, recognition.Amount);
        Assert.Equal(agreed.Date, recognition.AccruedOn);
        var cut = Assert.Single(openLedger.Where(a => a.Kind == CommissionAccrualKind.Adjustment));
        Assert.Equal(-20_000m, cut.Amount);
        Assert.Equal(approved.Date, cut.AccruedOn);
        Assert.DoesNotContain(openLedger, a => a.Reason != null && a.Reason.StartsWith("Cutover correction"));

        // Edited after the decision: the edit recalculated the amount and cleared the override, so
        // the AMOUNT must not be restated from the approval.
        var reEdited = await verify.BookingCommissions.SingleAsync(c => c.Id == editedAfterApprovalId);
        Assert.Equal(120_000m, reEdited.FinalAmount);
        Assert.Equal(0m, reEdited.AdjustmentAmount);
        Assert.Null(reEdited.AdjustmentReason);

        // The HISTORY is a separate question, and the approval still happened. It cut the
        // obligation in July and the edit raised it in August, so all three movements are dated
        // where they were decided. Reading the edit's own PreviousAmount instead — the pre-override
        // figure, because the edit had cleared the override — dropped the approval entirely: July
        // kept reporting the uncut 100,000 and August moved by 20,000 rather than 40,000, while the
        // total still came to 120,000 and hid both.
        var editedLedger = await verify.CommissionAccruals.Where(a => a.CommissionId == editedAfterApprovalId)
            .OrderBy(a => a.AccruedOn).ThenBy(a => a.Id).ToListAsync();
        Assert.Equal(120_000m, editedLedger.Sum(a => a.Amount));
        Assert.Collection(editedLedger,
            first =>
            {
                Assert.Equal(100_000m, first.Amount);
                Assert.Equal(agreed.Date, first.AccruedOn);
                Assert.Equal(CommissionAccrualKind.Recognition, first.Kind);
            },
            cutInJuly =>
            {
                Assert.Equal(-20_000m, cutInJuly.Amount);
                Assert.Equal(approved.Date, cutInJuly.AccruedOn);
                Assert.Equal(CommissionAccrualKind.Adjustment, cutInJuly.Kind);
            },
            raisedInAugust =>
            {
                // From the 80,000 the approval left it at, not from the 100,000 the edit's own
                // audit row claims it was moving from.
                Assert.Equal(40_000m, raisedInAugust.Amount);
                Assert.Equal(edited.Date, raisedInAugust.AccruedOn);
                Assert.Equal(CommissionAccrualKind.Adjustment, raisedInAugust.Kind);
            });
        Assert.DoesNotContain(editedLedger, a => a.Reason != null && a.Reason.StartsWith("Cutover correction"));

        // More has gone out than the approval allows for. Unexplainable, so left exactly as found
        // rather than restated into a negative payable.
        var belowPaid = await verify.BookingCommissions.SingleAsync(c => c.Id == approvedBelowPaidId);
        Assert.Equal(100_000m, belowPaid.FinalAmount);
        Assert.Equal(0m, belowPaid.AdjustmentAmount);
        Assert.Null(belowPaid.AdjustmentReason);

        var openRebateRow = await verify.CustomerRebates.SingleAsync(r => r.Id == openRebateId);
        Assert.Equal(35_000m, openRebateRow.FinalAmount);
        Assert.Equal(-15_000m, openRebateRow.AdjustmentAmount);

        var reversedRebateRow = await verify.CustomerRebates.SingleAsync(r => r.Id == reversedRebateId);
        Assert.Equal(40_000m, reversedRebateRow.FinalAmount);
        Assert.Equal(-10_000m, reversedRebateRow.AdjustmentAmount);
    }

    /// <summary>
    /// The upgrade refuses to adopt a Commission Payable that already carries an opening balance.
    /// <para>
    /// From this migration the payable is derived per commission, so an aggregate figure entered for
    /// the same unpaid commissions is counted twice and can never be cleared: pay the commission in
    /// full and the payout settles only its own accrual, leaving the opening amount owed for ever on
    /// a sheet that still balances. Silently zeroing the accountant's figure is not a migration's
    /// decision, so it stops the deployment instead.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task CommissionAccrualMigration_RefusesAnAccountThatAlreadyCarriesAnOpeningBalance()
    {
        await using var database = await SqlTestDatabase.CreateAsync(commandTimeoutSeconds: 180);
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
        {
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
            await db.GetService<IMigrator>().MigrateAsync(BeforeCommissionAccruals);
        }

        await using (var seed = new AppDbContext(options))
        {
            seed.FinanceAccounts.Add(new FinanceAccount
            {
                Name = "Commission Payable", Type = FinanceAccountType.Liability,
                AccountHolderName = "Seven Ventures", OpeningBalance = 100_000m,
                DisplayOrder = 517, IsActive = true
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
            var refused = await Assert.ThrowsAnyAsync<SqlException>(() => db.Database.MigrateAsync());
            Assert.Contains("counted twice", refused.Message, StringComparison.OrdinalIgnoreCase);
        }

        // The refusal rolled back cleanly: no table, and the accountant's figure is untouched.
        Assert.Equal(0, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CommissionAccruals'"));
        Assert.Equal(1, await ScalarAsync(database.ConnectionString,
            "SELECT COUNT(*) FROM [FinanceAccounts] WHERE [Name] = N'Commission Payable' AND [OpeningBalance] = 100000"));
    }

    /// <summary>
    /// The commission obligation is reported by the REAL provider, end to end.
    /// <para>
    /// The in-memory store evaluates in C# things SQL Server cannot translate at all, and every one
    /// of these paths reaches through an accrual into its commission, booking, unit and project.
    /// The account ledger is the sharpest of them: it is one UNION of a dozen projections, EF aligns
    /// a union on the FIRST branch's bindings, and a mis-shaped branch fails only against SQL.
    /// </para>
    /// <para>
    /// It also proves the accounting the ledger exists for: the commission is a cost the day it is
    /// agreed, the payout settles Commission Payable rather than costing anything a second time, and
    /// the Balance Sheet balances at both points.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task CommissionAccrual_IsReportedByTheRealProvider_AcrossPnlSheetTrialBalanceAndLedger()
    {
        await using var database = await SqlTestDatabase.CreateAsync(commandTimeoutSeconds: 180);
        var options = Options(database.ConnectionString);
        await using var db = new AppDbContext(options);
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
        await db.Database.MigrateAsync();
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));

        var accounts = new FinanceAccountService(db);
        await accounts.SetupClientChartAsync();
        var payable = await db.FinanceAccounts.AsNoTracking()
            .SingleAsync(a => a.SystemRole == FinanceSystemAccountRole.CommissionPayable);
        var bank = await db.FinanceAccounts.AsNoTracking()
            .OrderBy(a => a.DisplayOrder).FirstAsync(a => a.Type == FinanceAccountType.Bank);

        var project = new Project { ProjectName = "Accrual SQL", Location = "Karachi", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "S-1", UnitType = "Apartment", Price = 5_000_000m };
        var customer = new Customer { FullName = "SQL Buyer", Phone = "03007778888" };
        var booking = new Booking
        {
            BookingReference = "BK-ACCRUAL-1", Customer = customer, Unit = unit,
            Status = BookingStatus.PaymentPlanActive, Source = CustomerSource.Referral,
            AgreedSalePrice = 5_000_000m, BookingAmountRequired = 500_000m,
            BookingAmountReceived = 500_000m, BookingDate = new DateTime(2026, 7, 1)
        };
        var partner = new ThirdPartyPartner
        {
            Name = "SQL Broker", PartnerType = "Broker", InternalCode = "SQLBR-1", IsActive = true,
            BankName = "Test Bank", AccountTitle = "SQL Broker", AccountNumber = "00123456789"
        };
        db.AddRange(project, unit, customer, booking, partner);
        await db.SaveChangesAsync();

        var service = new CommissionRebateService(db, accounts, new NullPrivateStorage());
        var actor = new FinancialWorkflowActor(1, "SQL Admin");
        var commission = Assert.Single((await service.CreateCommissionAsync(booking.Id,
            new CreateBookingCommissionDto
            {
                PartnerId = partner.Id, IsManual = true,
                ManualCalculationType = FinancialCalculationType.FixedAmount,
                ManualCalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                ManualFixedAmount = 150_000m
            }, actor)).Commissions);

        var finance = new FinanceService(db, new NullPrivateStorage(), accounts,
            new WhtService(db, accounts), NullLogger<FinanceService>.Instance);
        var today = PakistanTime.Today;

        var beforePayout = await finance.GetProfitAndLossAsync(null, today, today);
        Assert.Equal(150_000m,
            Assert.Single(beforePayout.ExpenseLines, l => l.Name == "Partner Commissions").Amount);
        var sheetBefore = await finance.GetBalanceSheetAsync(null, today);
        Assert.True(sheetBefore.IsBalanced, $"out by {sheetBefore.Imbalance}");
        Assert.Equal(150_000m, PayableLine(sheetBefore));

        await service.RecordPayoutAsync(booking.Id, commission.Id, new RecordCommissionPayoutDto
        {
            FinanceAccountId = bank.Id, Amount = 90_000m, PaymentDate = today,
            PaymentMethod = PaymentMethod.BankTransfer, PaymentReference = "TT-ACCRUAL-1",
            IdempotencyKey = "sql-accrual-payout", CommissionConcurrencyToken = commission.ConcurrencyToken
        }, actor);

        // Paying it settles the payable and does not charge profit a second time.
        var afterPayout = await finance.GetProfitAndLossAsync(null, today, today);
        Assert.Equal(150_000m,
            Assert.Single(afterPayout.ExpenseLines, l => l.Name == "Partner Commissions").Amount);
        var sheetAfter = await finance.GetBalanceSheetAsync(null, today);
        Assert.True(sheetAfter.IsBalanced, $"out by {sheetAfter.Imbalance}");
        Assert.Equal(60_000m, PayableLine(sheetAfter));

        // Trial Balance, drill-down and the account ledger all translate and agree.
        var trial = await finance.GetTrialBalanceAsync(null, today, 0);
        var payableRow = Assert.Single(trial.Rows, r => r.AccountName == "Commission Payable");
        // A liability is credit-normal, so the payable stands in the Credit column.
        Assert.Equal(60_000m, payableRow.CreditBalances[^1]);
        var drill = await finance.GetTrialBalanceDetailsAsync("E:commission-expense", null, today, today);
        Assert.Equal(150_000m, drill.TotalDebit - drill.TotalCredit);
        Assert.Single(drill.Rows);

        var ledger = await accounts.GetTransactionLedgerSliceAsync(
            payable.Id, null, today.AddYears(-1), today, 0, 50);
        Assert.Contains(ledger.Items, i => i.Kind == "Commission accrued" && i.Amount == 150_000m);
        Assert.Contains(ledger.Items, i => i.Kind == "Commission paid" && i.Amount == -90_000m);

        var card = await accounts.GetByIdAsync(payable.Id);
        Assert.Equal(60_000m, card.CurrentBalance);
    }

    private static decimal PayableLine(DTOs.FinanceDtos.BalanceSheetDto sheet) =>
        sheet.LiabilityGroups.SelectMany(g => g.Lines)
            .Where(l => l.Name == "Commission Payable").Sum(l => l.Amount);

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

    /// <summary>Turns the first command that writes a lead's final reference into a SQL error the
    /// test's execution strategy is told to treat as transient — a deadlock victim, in effect.</summary>
    private sealed class FailOnceTransientlyOnLeadReference : DbCommandInterceptor
    {
        public const int ErrorNumber = 50001;
        public bool Failed { get; private set; }

        public void Arm() => Failed = false;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!Failed && command.CommandText.Contains("UPDATE [Leads]") && command.CommandText.Contains("[LeadReference]"))
            {
                Failed = true;
                command.CommandText = $"THROW {ErrorNumber}, 'Simulated transient failure.', 1;";
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>Runs a competing action once, just before the first save that matches — the
    /// moment a real concurrent request would slip in between this one's read and its write.</summary>
    private sealed class RunBeforeSavingInterceptor(Func<DbContext, bool> when, Func<Task> race) : SaveChangesInterceptor
    {
        private bool _raced;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_raced && eventData.Context is { } context && when(context))
            {
                _raced = true;
                await race();
            }

            return result;
        }
    }

    private sealed class FailLeadFinalizationInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Lead>()
                    .Any(e => e.State == EntityState.Modified && e.Entity.LeadReference.StartsWith("LD-")) == true)
                throw new InvalidOperationException("Simulated failure after initial lead persistence.");

            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailMetaCompletionInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<ExternalIntegrationEvent>()
                    .Any(e => e.Entity.Status == ExternalIntegrationEventStatus.Processed) == true)
                throw new InvalidOperationException("Simulated failure while completing the Meta event.");

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Fails any save that would park a Meta event for retry — a database that gives out
    /// at exactly the moment the worker records a failure.</summary>
    private sealed class FailRetryStateInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<ExternalIntegrationEvent>()
                    .Any(entry => entry.State == EntityState.Modified
                                  && entry.Entity.Status == ExternalIntegrationEventStatus.Retry) == true)
                throw new InvalidOperationException("Simulated database failure while saving retry state.");

            return ValueTask.FromResult(result);
        }
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
        private readonly int _commandTimeoutSeconds;
        public string ConnectionString { get; }

        private SqlTestDatabase(
            string masterConnection, string databaseName, string connectionString,
            int commandTimeoutSeconds)
        {
            _masterConnection = masterConnection;
            _databaseName = databaseName;
            _commandTimeoutSeconds = commandTimeoutSeconds;
            ConnectionString = connectionString;
        }

        public static async Task<SqlTestDatabase> CreateAsync(int commandTimeoutSeconds = 30)
        {
            var configured = Environment.GetEnvironmentVariable("DAMS_SQLSERVER_TEST_CONNECTION")
                ?? throw new InvalidOperationException("DAMS_SQLSERVER_TEST_CONNECTION is required.");
            var databaseName = $"DamsProductionTests_{Guid.NewGuid():N}";
            var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
            var test = new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName };
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{databaseName}]", connection);
            command.CommandTimeout = commandTimeoutSeconds;
            await command.ExecuteNonQueryAsync();
            return new SqlTestDatabase(
                master.ConnectionString, databaseName, test.ConnectionString, commandTimeoutSeconds);
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
            command.CommandTimeout = _commandTimeoutSeconds;
            await command.ExecuteNonQueryAsync();
        }
    }
}
