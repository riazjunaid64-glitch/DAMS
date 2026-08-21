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
/// Collecting what is still owed after possession.
/// <para>
/// Possession recognises the whole sale as revenue and turns whatever is unpaid into an Accounts
/// Receivable. That receivable has exactly one collection route in DAMS — the installment schedule —
/// so both halves of it have to keep working after the status changes: recording a receipt against
/// an installment, and building a schedule for a receivable that has none. If either is refused, a
/// real balance is stranded with no way to bring the money in, and the Balance Sheet carries a
/// receivable for a customer nobody can be paid by.
/// </para>
/// <para>
/// The other half of these tests is what must NOT move: a collection after possession is cash in
/// against a receivable, never a second helping of revenue.
/// </para>
/// </summary>
public sealed class PostPossessionReceivableTests
{
    private static readonly DateTime Jan = new(2026, 1, 15);
    private static readonly DateTime Feb = new(2026, 2, 15);
    private static readonly DateTime Mar = new(2026, 3, 15);

    /// <summary>
    /// Codex finding A. Half the price collected on a schedule, possession given, and the rest still
    /// owed: the remaining installment must still be collectable, and the money must land on the
    /// receivable rather than being recognised all over again.
    /// </summary>
    [Fact]
    public async Task AfterPossession_TheRemainingInstallmentIsStillCollectable_AndClearsTheReceivable()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 10_000_000m);
        var installments = Installments(context);

        // Two installments of 5,000,000 agreed while the plan was active.
        var schedule = await installments.GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, count: 2), adminUserId: 1);
        var first = schedule.Items.Single(i => i.SequenceNumber == 1);
        var second = schedule.Items.Single(i => i.SequenceNumber == 2);

        await installments.RecordInstallmentPaymentAsync(world.BookingId, first.Id, Receipt(world, 5_000_000m, Jan), 1);
        await Bookings(context).GivePossessionAsync(world.BookingId, Feb, 1);

        var accounts = new FinanceAccountService(context);
        var finance = Finance(context);
        Assert.Equal(BookingStatus.PossessionGiven, (await context.Bookings.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(5_000_000m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        Assert.Equal(10_000_000m, (await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar)).TotalIncome);

        // The collection the UI used to hide. It is accepted, and it is not revenue.
        await installments.RecordInstallmentPaymentAsync(world.BookingId, second.Id, Receipt(world, 5_000_000m, Mar), 1);

        Assert.Equal(10_000_000m, (await accounts.GetByIdAsync(world.Bank.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Deposits.Id)).CurrentBalance);
        var pnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar);
        Assert.Equal(10_000_000m, pnl.TotalIncome);
        Assert.Equal(BookingStatus.PossessionGiven, (await context.Bookings.AsNoTracking().SingleAsync()).Status);
        await AssertBalancedAsync(finance, Mar);
    }

    /// <summary>
    /// Codex finding B. Possession given with no schedule at all — the booking amount was taken and
    /// the balance was never structured. The receivable is real, so a schedule has to be creatable
    /// for it, and collecting on it has to work.
    /// </summary>
    [Fact]
    public async Task AfterPossession_AScheduleCanBeBuiltForAReceivableThatHasNone_AndCollected()
    {
        await using var context = Context();
        // 4,000,000 of the 10,000,000 taken as the booking amount before possession.
        var world = await SeedAsync(context, netSalePrice: 10_000_000m, bookingAmount: 4_000_000m);
        await Bookings(context).GivePossessionAsync(world.BookingId, Feb, 1);

        var accounts = new FinanceAccountService(context);
        var finance = Finance(context);
        Assert.Equal(6_000_000m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        Assert.Empty(context.Installments);

        var installments = Installments(context);
        // The backend allows this; the schedule form used to be hidden by the status check.
        var loaded = await installments.GetScheduleAsync(world.BookingId);
        Assert.True(loaded.CanGenerate, "A recognised receivable with no schedule must still be plannable.");

        var schedule = await installments.GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, count: 3), adminUserId: 1);
        // The schedule covers exactly the receivable, not the whole sale: the booking amount is
        // already collected and already out of the deposit.
        Assert.Equal(6_000_000m, schedule.Items.Sum(i => i.Amount));

        foreach (var item in schedule.Items)
            await installments.RecordInstallmentPaymentAsync(world.BookingId, item.Id, Receipt(world, item.Amount, Mar), 1);

        Assert.Equal(10_000_000m, (await accounts.GetByIdAsync(world.Bank.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        Assert.Equal(10_000_000m, (await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar)).TotalIncome);
        await AssertBalancedAsync(finance, Mar);
    }

    /// <summary>
    /// The states that are genuinely closed stay closed. Widening the two collectable statuses must
    /// not have widened them to everything.
    /// </summary>
    [Fact]
    public async Task ACancelledBooking_StillRefusesBothCollectionAndPlanning()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 10_000_000m);
        var installments = Installments(context);
        var schedule = await installments.GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, count: 2), adminUserId: 1);
        var item = schedule.Items[0];

        var booking = await context.Bookings.SingleAsync();
        booking.Status = BookingStatus.Cancelled;
        await context.SaveChangesAsync();

        var payError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installments.RecordInstallmentPaymentAsync(world.BookingId, item.Id, Receipt(world, 100m, Mar), 1));
        Assert.Contains("cancelled booking", payError.Message);

        var planError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installments.GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, count: 2, regenerate: true), 1));
        Assert.Contains("cancelled booking", planError.Message);

        Assert.False((await installments.GetScheduleAsync(world.BookingId)).CanGenerate);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private sealed record World(int BookingId, FinanceAccount Bank, FinanceAccount Deposits, FinanceAccount Receivables);

    private static GenerateInstallmentPlanDto Plan(decimal price, int count, bool regenerate = false) => new()
    {
        AgreedSalePrice = price, DiscountPercent = 0m, Frequency = InstallmentFrequency.Monthly,
        NumberOfInstallments = count, InstallmentStartDate = Jan, Regenerate = regenerate
    };

    private static RecordInstallmentPaymentDto Receipt(World world, decimal amount, DateTime paidAt) => new()
    {
        Amount = amount, FinanceAccountId = world.Bank.Id, PaymentMethod = PaymentMethod.BankTransfer,
        PaidAt = paidAt, PaymentReference = $"TT-{Guid.NewGuid():N}"[..12]
    };

    private static async Task<World> SeedAsync(AppDbContext context, decimal netSalePrice, decimal bookingAmount = 0m)
    {
        var bank = new FinanceAccount { Name = "Collection Bank", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Bank, IsActive = true };
        var deposits = new FinanceAccount
        {
            Name = "Customer Deposits", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Liability,
            SystemRole = FinanceSystemAccountRole.CustomerDeposits, DisplayOrder = 500, IsActive = true
        };
        var receivables = new FinanceAccount
        {
            Name = "Customer Receivables", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Receivable,
            SystemRole = FinanceSystemAccountRole.CustomerReceivables, DisplayOrder = 420, IsActive = true
        };
        var project = new Project { ProjectName = "Floria Heights", Location = "Islamabad", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "A-1", UnitType = "Apartment", Price = netSalePrice, Status = UnitStatus.OnPaymentPlan };
        var customer = new Customer { FullName = "Buyer", Phone = "03001112222", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = "BK-AR", Customer = customer, Unit = unit, Source = CustomerSource.Referral,
            Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = netSalePrice, DiscountAmount = 0m,
            BookingAmountRequired = bookingAmount, BookingAmountReceived = bookingAmount, BookingDate = Jan
        };
        if (bookingAmount > 0m)
            booking.Payments.Add(new Payment
            {
                Booking = booking, FinanceAccountId = bank.Id, Amount = bookingAmount, Type = PaymentType.BookingAmount,
                PaymentMethod = PaymentMethod.BankTransfer, PaidAt = Jan
            });
        context.AddRange(bank, deposits, receivables, project, unit, customer, booking);
        await context.SaveChangesAsync();
        // The payment is added with the booking, so its account id is only real once both are saved.
        foreach (var payment in booking.Payments) payment.FinanceAccountId = bank.Id;
        await context.SaveChangesAsync();
        return new World(booking.Id, bank, deposits, receivables);
    }

    private static async Task AssertBalancedAsync(FinanceService finance, DateTime asAt)
    {
        var sheet = await finance.GetBalanceSheetAsync(null, asAt);
        Assert.True(sheet.IsBalanced, $"Balance sheet out by {sheet.Imbalance}: {string.Join(", ", sheet.UnbalancedAccounts ?? [])}");
        var trial = await finance.GetTrialBalanceAsync(null, new DateTime(asAt.Year, asAt.Month, DateTime.DaysInMonth(asAt.Year, asAt.Month)), 0);
        Assert.True(Assert.Single(trial.ColumnBalanced));
    }

    private static BookingService Bookings(AppDbContext context) =>
        new(context, new CustomerService(context), new FinanceAccountService(context));

    private static InstallmentService Installments(AppDbContext context) =>
        new(context, new FinanceAccountService(context));

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts, new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
    }

    private static AppDbContext Context()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
