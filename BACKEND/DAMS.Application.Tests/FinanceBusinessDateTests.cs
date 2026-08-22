using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.DTOs.InstallmentDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// Every finance record that dates itself, dates itself on the PAKISTAN business day.
/// <para>
/// DAMS' business day is UTC+5. Between 19:00 and 23:59 UTC the two calendars disagree, so a record
/// that defaulted its date from <c>DateTime.UtcNow</c> was filed on the previous day for the whole
/// of the local early morning — a payment and the expense it funded could land in different months
/// while the operator saw one evening's work, and a month that looked closed at 23:00 could still
/// gain entries.
/// </para>
/// <para>
/// These assertions hold at every hour, and they FAIL during that five-hour window if the default
/// ever goes back to UTC. They are written against <see cref="PakistanTime.Today"/> rather than a
/// frozen clock because the production code reads the real clock: pinning a fake time here would
/// prove the fake was applied consistently and nothing about the bug.
/// </para>
/// </summary>
public sealed class FinanceBusinessDateTests
{
    [Fact]
    public async Task ARecordSavedWithNoDate_IsFiledOnThePakistanBusinessDay()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);
        var today = PakistanTime.Today;

        var revenue = await service.CreateManualRevenueAsync(new CreateManualRevenueDto
        {
            FinanceAccountId = world.Bank.Id, Amount = 50_000m, RevenueType = "Transfer charges"
        }, adminUserId: 1);
        var expense = await service.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = world.Bank.Id, Amount = 30_000m, CategoryId = world.Head.Id
        }, adminUserId: 1);
        var purchase = await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Equipment.Id, FinanceAccountId = world.Bank.Id, Amount = 20_000m,
            ItemName = "Printer", CategoryId = world.Head.Id
        }, adminUserId: 1);

        Assert.Equal(today, revenue.Date);
        Assert.Equal(today, expense.Date);
        Assert.Equal(today, purchase.Date);
        // Midnight, not a time of day. Every report reads these columns as dates, and a stored
        // 14:32 makes an "as at" comparison depend on the hour the row was typed.
        Assert.Equal(TimeSpan.Zero, revenue.Date.TimeOfDay);
        Assert.Equal(TimeSpan.Zero, expense.Date.TimeOfDay);
        Assert.Equal(TimeSpan.Zero, purchase.Date.TimeOfDay);
    }

    [Fact]
    public async Task TodaysReportSeesTodaysUndatedRecords()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);
        var today = PakistanTime.Today;

        await service.CreateManualRevenueAsync(new CreateManualRevenueDto
        {
            FinanceAccountId = world.Bank.Id, Amount = 50_000m, RevenueType = "Transfer charges"
        }, adminUserId: 1);
        await service.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = world.Bank.Id, Amount = 30_000m, CategoryId = world.Head.Id
        }, adminUserId: 1);

        // The point of the whole fix: a single-day report for the day the operator is working on
        // finds the work they just did. Under the UTC default this returned nothing all local
        // morning, which reads as data loss rather than as a timezone bug.
        var pnl = await service.GetProfitAndLossAsync(null, today, today);
        Assert.Equal(50_000m, pnl.TotalIncome);
        Assert.Equal(30_000m, pnl.TotalExpenses);

        var summary = await service.GetSummaryAsync(null, today, today);
        Assert.Equal(50_000m, summary.TotalRevenue);
        Assert.Equal(30_000m, summary.TotalExpenses);

        // And yesterday's report does not — the entry belongs to one business day, not two.
        var yesterday = await service.GetProfitAndLossAsync(null, today.AddDays(-1), today.AddDays(-1));
        Assert.Equal(0m, yesterday.TotalIncome);
        Assert.Equal(0m, yesterday.TotalExpenses);
    }

    /// <summary>
    /// Customer cash keeps its time of day — a receipt is a moment, and two payments on one day must
    /// stay in the order they were taken — but the moment it is stamped with is the Pakistan one, so
    /// the deposit it creates lands on the business day the money actually arrived.
    /// </summary>
    [Fact]
    public async Task ACustomerPaymentWithNoTimestamp_LandsOnThePakistanBusinessDay()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var today = PakistanTime.Today;

        var bookings = new BookingService(context, new CustomerService(context), new FinanceAccountService(context));
        await bookings.RecordBookingAmountPaymentAsync(world.BookingId, new RecordBookingAmountPaymentDto
        {
            Amount = 100_000m, PaymentMethod = PaymentMethod.BankTransfer,
            FinanceAccountId = world.Bank.Id
        }, adminUserId: 1);

        var payment = Assert.Single(context.Payments);
        Assert.Equal(today, payment.PaidAt.Date);

        // The deposit liability it created is visible in a report for that same day.
        var finance = Finance(context);
        var deposits = await finance.GetCustomerDepositPageAsync(null, today, 0, 20);
        Assert.Equal(100_000m, Assert.Single(deposits.Items).DepositBalance);
        Assert.Equal(100_000m, (await new FinanceAccountService(context).GetByIdAsync(world.Deposits.Id)).CurrentBalance);
        // Still not revenue — the date fix must not have changed what the money IS.
        Assert.Equal(0m, (await finance.GetProfitAndLossAsync(null, today.AddDays(-30), today)).TotalIncome);
    }

    [Fact]
    public async Task AnInstallmentPaymentWithNoTimestamp_LandsOnThePakistanBusinessDay()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var today = PakistanTime.Today;

        var booking = await context.Bookings.SingleAsync(b => b.Id == world.BookingId);
        booking.Status = BookingStatus.PaymentPlanActive;
        booking.BookingAmountReceived = booking.BookingAmountRequired;
        var installment = new Installment
        {
            BookingId = world.BookingId, SequenceNumber = 1, Type = InstallmentType.Regular,
            DueDate = today, Amount = 200_000m, Status = InstallmentStatus.Pending
        };
        context.Installments.Add(installment);
        await context.SaveChangesAsync();

        await new InstallmentService(context, new FinanceAccountService(context))
            .RecordInstallmentPaymentAsync(world.BookingId, installment.Id, new RecordInstallmentPaymentDto
            {
                Amount = 200_000m, PaymentMethod = PaymentMethod.Cash, FinanceAccountId = world.Bank.Id
            }, adminUserId: 1);

        Assert.Equal(today, Assert.Single(context.Payments).PaidAt.Date);
    }

    /// <summary>
    /// A date SQL Server will accept but the Trial Balance cannot address is worse than a rejected
    /// one: the row saves, balances nothing, and appears in no report at all.
    /// </summary>
    [Fact]
    public async Task ADateBeforeTheReportingFloor_IsRejectedRatherThanSavedInvisibly()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);

        var expense = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateExpenseAsync(
            new CreateExpenseDto
            {
                FinanceAccountId = world.Bank.Id, Amount = 1_000m, CategoryId = world.Head.Id,
                Date = new DateTime(1600, 1, 1)
            }, adminUserId: 1));
        Assert.Contains("Expense date", expense.Message);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateManualRevenueAsync(
            new CreateManualRevenueDto
            {
                FinanceAccountId = world.Bank.Id, Amount = 1_000m, RevenueType = "Transfer charges",
                Date = new DateTime(1600, 1, 1)
            }, adminUserId: 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAssetPurchaseAsync(
            new CreateAssetPurchaseDto
            {
                AssetAccountId = world.Equipment.Id, FinanceAccountId = world.Bank.Id, Amount = 1_000m,
                ItemName = "Printer", CategoryId = world.Head.Id, Date = new DateTime(1600, 1, 1)
            }, adminUserId: 1));

        Assert.Empty(context.Expenses);
        Assert.Empty(context.ManualRevenues);
        Assert.Empty(context.AssetPurchases);
    }

    /// <summary>
    /// The concurrency token has to reach the client on every surface it will be sent back from —
    /// the create/update response AND the paged list the edit button is on. A token the UI cannot see
    /// is a protection the UI cannot use, and the server would then have to accept its absence.
    /// <para>
    /// The stale-token race itself is proved against real SQL Server
    /// (<c>TwoAdminsEditingTheSameExpenseOrRevenueRow_LoseTheStaleOne_OnRealSqlServer</c>): the
    /// in-memory provider does not generate <c>rowversion</c> values, so there is nothing here for a
    /// version to go stale against.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EveryEditableFinanceRow_CarriesItsConcurrencyToken_AndAMalformedOneIsRefused()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);

        var expense = await service.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = world.Bank.Id, Amount = 30_000m, CategoryId = world.Head.Id
        }, adminUserId: 1);
        var revenue = await service.CreateManualRevenueAsync(new CreateManualRevenueDto
        {
            FinanceAccountId = world.Bank.Id, Amount = 50_000m, RevenueType = "Transfer charges"
        }, adminUserId: 1);

        // Present on the response…
        Assert.NotNull(expense.ConcurrencyToken);
        Assert.NotNull(revenue.ConcurrencyToken);
        // …and on the rows the tables render, which is where Edit and Delete are actually clicked.
        var expensePage = await service.GetExpensePageAsync(null, null, null, 0, 20);
        Assert.NotNull(Assert.Single(expensePage.Items).ConcurrencyToken);
        var revenuePage = await service.GetRevenuePageAsync(null, null, null, 0, 20);
        var revenueRow = Assert.Single(revenuePage.Items, r => r.ManualRevenueId == revenue.Id);
        Assert.NotNull(revenueRow.ConcurrencyToken);

        // A token that is not base64 is a client bug, not a reason to save anyway.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.UpdateExpenseAsync(
            expense.Id, new UpdateExpenseDto
            {
                FinanceAccountId = world.Bank.Id, Amount = 1_000m, CategoryId = world.Head.Id,
                ConcurrencyToken = "not-a-token"
            }));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.UpdateManualRevenueAsync(
            revenue.Id, new UpdateManualRevenueDto
            {
                FinanceAccountId = world.Bank.Id, Amount = 1_000m, RevenueType = "Transfer charges",
                ConcurrencyToken = "not-a-token"
            }));

        // Nothing was written by either refused attempt.
        Assert.Equal(30_000m, (await context.Expenses.SingleAsync()).Amount);
        Assert.Equal(50_000m, (await context.ManualRevenues.SingleAsync()).Amount);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private sealed record World(
        FinanceAccount Bank, FinanceAccount Equipment, FinanceAccount Deposits,
        ExpenseCategory Head, int BookingId);

    private static async Task<World> SeedAsync(AppDbContext context)
    {
        var bank = new FinanceAccount { Name = "HBL Bank", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Bank, IsActive = true };
        var equipment = new FinanceAccount { Name = "Office Equipment", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.FixedAsset, IsActive = true };
        var deposits = new FinanceAccount
        {
            Name = "Customer General Account / Customer Deposits", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Liability, SystemRole = FinanceSystemAccountRole.CustomerDeposits, IsActive = true
        };
        var head = new ExpenseCategory { Name = "Miscellaneous", Code = "miscellaneous", IsWhtApplicable = false, DisplayOrder = 10 };
        var project = new Project { ProjectName = "Business Dates", Location = "Karachi", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "BD-1", UnitType = "Apartment", Price = 1_000_000m, Status = UnitStatus.Booked };
        var customer = new Customer { FullName = "Date Buyer", Phone = "03001112222", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = $"BK-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
            Status = BookingStatus.AwaitingBookingAmount, Source = CustomerSource.Referral,
            AgreedSalePrice = 1_000_000m, BookingAmountRequired = 100_000m,
            // Well in the past, so a possession or payment dated "today" is never before the booking.
            BookingDate = PakistanTime.Today.AddYears(-1)
        };
        context.AddRange(bank, equipment, deposits, head, project, unit, customer, booking);
        await context.SaveChangesAsync();
        return new World(bank, equipment, deposits, head, booking.Id);
    }

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts,
            new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
