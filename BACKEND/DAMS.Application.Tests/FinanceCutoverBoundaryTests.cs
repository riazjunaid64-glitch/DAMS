using DAMS.Application.Common;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.DTOs.WhtDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The go-live cutover, from both sides.
/// <para>
/// The opening balances are read by every report as an AS-AT position at the go-live date: the
/// figure the accountant typed already contains everything that happened before it, so the reports
/// start their windows there and add movements on top. That makes the go-live date a boundary with
/// two halves to defend. Nothing may be POSTED behind it — <see cref="FinanceDateRules"/> — and
/// nothing may already BE behind it when the date is set, which no amount of posting validation can
/// achieve because those rows were legal when they were written.
/// </para>
/// <para>
/// Both halves fail the same silent way: the pre-baseline amount is counted twice, once inside the
/// opening figure and once as a movement, and because both halves are individually correct the sheet
/// still balances while being wrong. There is no downstream check that can catch it, which is why
/// these are preconditions rather than report warnings.
/// </para>
/// </summary>
public sealed class FinanceCutoverBoundaryTests
{
    private static readonly DateTime GoLive = new(2026, 8, 1);

    // ── Nothing may be posted behind the baseline ──

    [Fact]
    public async Task ACapitalContribution_CannotBeDatedBeforeTheGoLiveDate()
    {
        // Capital was the one posting path still validating only that a date had been supplied. A
        // contribution moves the bank account the day it is saved and the partner's Capital balance
        // on the Balance Sheet, so it is a financial posting in every sense the rule cares about.
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Capital(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordTransactionAsync(world.PartnerId, new SaveCapitalTransactionDto
            {
                Type = CapitalTransactionType.Contribution, Amount = 500_000m,
                Date = GoLive.AddDays(-1), FinanceAccountId = world.BankId
            }, userId: 1));

        Assert.Contains("before the go-live date", error.Message);
        Assert.Empty(context.CapitalTransactions);
    }

    [Fact]
    public async Task ACapitalContribution_CannotBeDatedInTheFuture()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Capital(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordTransactionAsync(world.PartnerId, new SaveCapitalTransactionDto
            {
                Type = CapitalTransactionType.Contribution, Amount = 500_000m,
                Date = PakistanTime.Today.AddDays(1), FinanceAccountId = world.BankId
            }, userId: 1));

        Assert.Contains("cannot be in the future", error.Message);
        Assert.Empty(context.CapitalTransactions);
    }

    [Fact]
    public async Task ACapitalContribution_OnTheBaselineDateItself_IsAccepted()
    {
        // The boundary day belongs to the new period: the opening figures are the position at its
        // START, so activity dated on it is new and is added on top. If this ever starts failing, the
        // three places that agree on that convention have drifted apart.
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Capital(context);

        var recorded = await service.RecordTransactionAsync(world.PartnerId, new SaveCapitalTransactionDto
        {
            Type = CapitalTransactionType.Contribution, Amount = 500_000m,
            Date = GoLive, FinanceAccountId = world.BankId
        }, userId: 1);

        Assert.Equal(GoLive, recorded.Date);
    }

    [Fact]
    public async Task Possession_CannotRecogniseASaleBehindTheGoLiveDate()
    {
        // Possession is the largest posting DAMS makes — it books the sale as revenue and raises the
        // whole receivable — and it was checking only "not future" and "not before the booking". A
        // July booking could therefore recognise into July after a 1 August cutover, putting the
        // receivable both inside the opening balances and on top of them.
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = new BookingService(context, new CustomerService(context), new FinanceAccountService(context));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GivePossessionAsync(world.BookingId, GoLive.AddDays(-2), adminUserId: 5));

        Assert.Contains("before the go-live date", error.Message);
        Assert.Empty(context.BookingSaleRecognitions);
        Assert.Equal(BookingStatus.PaymentPlanActive, context.Bookings.Single().Status);
    }

    [Fact]
    public async Task Possession_OnOrAfterTheBaseline_StillRecognisesTheSale()
    {
        // The guard must not close the ordinary path: the point is to reject the cutover breach, not
        // to make possession unusable.
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = new BookingService(context, new CustomerService(context), new FinanceAccountService(context));

        await service.GivePossessionAsync(world.BookingId, GoLive.AddDays(3), adminUserId: 5);

        var recognition = Assert.Single(context.BookingSaleRecognitions);
        Assert.Equal(GoLive.AddDays(3), recognition.RecognitionDate);
        Assert.Equal(1_000_000m, recognition.NetSaleValue);
    }

    // ── Nothing may already be behind the go-live date when it is set ──

    [Fact]
    public async Task SettingTheGoLiveDate_IsRefusedWhileEarlierFinancialRecordsExist()
    {
        await using var context = Context();
        var accounts = await SeedAccountsAsync(context);
        // A pilot month already in DAMS: a payment and an expense that were perfectly legal when
        // they were entered, and that the accountant's 1 August figures also contain.
        context.Expenses.Add(new Expense
        {
            FinanceAccountId = accounts.BankId, Amount = 40_000m, Category = "Rent",
            Date = new DateTime(2026, 7, 25)
        });
        context.CapitalTransactions.Add(new CapitalTransaction
        {
            CapitalPartnerId = accounts.PartnerId, Type = CapitalTransactionType.Contribution,
            Amount = 2_000_000m, Date = new DateTime(2026, 7, 20), FinanceAccountId = accounts.BankId
        });
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Apply(context, GoLive));

        Assert.Contains("1 expenses", error.Message);
        Assert.Contains("1 capital transactions", error.Message);
        // Names the earliest so the operator can act on it rather than go hunting.
        Assert.Contains("20 Jul 2026", error.Message);
        Assert.Null(await FinanceDateRules.BaselineAsync(context, default));
    }

    [Fact]
    public async Task SettingTheGoLiveDate_IsAllowedWhenTheOnlyRecordsAreOnOrAfterIt()
    {
        await using var context = Context();
        var accounts = await SeedAccountsAsync(context);
        // On the boundary day, which belongs to the new period, so it is not inside the figures.
        context.Expenses.Add(new Expense
        {
            FinanceAccountId = accounts.BankId, Amount = 40_000m, Category = "Rent", Date = GoLive
        });
        await context.SaveChangesAsync();

        var saved = await Apply(context, GoLive);

        Assert.Equal(GoLive, saved.GoLiveDate);
        Assert.Equal(GoLive, await FinanceDateRules.BaselineAsync(context, default));
    }

    [Fact]
    public async Task TheCutoverCheck_LooksAtEveryKindOfFinancialRecord_NotJustExpenses()
    {
        // The double count does not care which table the movement is in, so neither does the check.
        // A receipt alone must be enough to stop the date being set.
        await using var context = Context();
        var accounts = await SeedAccountsAsync(context);
        var world = await SeedBookingAsync(context);
        context.Payments.Add(new Payment
        {
            BookingId = world, Amount = 300_000m, Type = PaymentType.BookingAmount,
            PaymentMethod = PaymentMethod.Cash, FinanceAccountId = accounts.BankId,
            PaidAt = new DateTime(2026, 7, 15, 14, 30, 0)
        });
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Apply(context, GoLive));
        Assert.Contains("1 customer receipts", error.Message);
    }

    // ── The go-live date itself ──

    /// <summary>
    /// A go-live date in the future locks the business out of its own finance module: the balances
    /// are the position at the start of a day that has not happened, and every entry between today
    /// and then is refused for being before the go-live date.
    /// </summary>
    [Fact]
    public async Task AGoLiveDate_CannotBeInTheFuture()
    {
        await using var context = Context();
        await SeedAccountsAsync(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Apply(context, PakistanTime.Today.AddDays(12)));

        Assert.Contains("cannot be in the future", error.Message);
        Assert.Null(await FinanceDateRules.BaselineAsync(context, default));
    }

    [Fact]
    public async Task AGoLiveDate_CannotPredateWhatTheReportsCanAddress()
    {
        await using var context = Context();
        await SeedAccountsAsync(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Apply(context, new DateTime(1752, 12, 31)));

        Assert.Contains("cannot be before", error.Message);
        Assert.Null(await FinanceDateRules.BaselineAsync(context, default));
    }

    [Fact]
    public async Task SavingWithoutAGoLiveDate_LeavesItAsItWas()
    {
        await using var context = Context();
        await Apply(context, GoLive);

        var saved = await Apply(context, null);

        Assert.Equal(GoLive, saved.GoLiveDate);
        Assert.Equal(GoLive, await FinanceDateRules.BaselineAsync(context, default));
    }

    [Fact]
    public async Task TheFirstGoLiveDate_IsStoredAsADate_AndReturned()
    {
        await using var context = Context();

        var saved = await Apply(context, GoLive.AddHours(15));

        Assert.Equal(GoLive, saved.GoLiveDate);
        Assert.Equal(GoLive, (await Wht(context).GetSettingsAsync()).GoLiveDate);
    }

    [Fact]
    public async Task TheGoLiveDate_CanMoveEarlier_AndLaterOnlyWhenNothingIsBeforeTheNewDate()
    {
        await using var context = Context();
        var accounts = await SeedAccountsAsync(context);
        await Apply(context, GoLive);
        context.Expenses.Add(new Expense
        {
            FinanceAccountId = accounts.BankId, Amount = 40_000m, Category = "Rent", Date = GoLive.AddDays(10)
        });
        await context.SaveChangesAsync();

        // Earlier always passes.
        var earlier = await Apply(context, GoLive.AddDays(-20));
        Assert.Equal(GoLive.AddDays(-20), earlier.GoLiveDate);

        // Later is refused once the expense would fall before it...
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Apply(context, GoLive.AddDays(11)));
        Assert.Equal(GoLive.AddDays(-20), await FinanceDateRules.BaselineAsync(context, default));

        // ...and allowed up to the day of the expense itself.
        var later = await Apply(context, GoLive.AddDays(10));
        Assert.Equal(GoLive.AddDays(10), later.GoLiveDate);
    }

    [Fact]
    public async Task WithNoGoLiveDate_ThereIsNoPostingLimit()
    {
        await using var context = Context();
        var world = await SeedAsyncWithoutGoLive(context);
        var service = Capital(context);

        var recorded = await service.RecordTransactionAsync(world.PartnerId, new SaveCapitalTransactionDto
        {
            Type = CapitalTransactionType.Contribution, Amount = 500_000m,
            Date = new DateTime(2025, 1, 5), FinanceAccountId = world.BankId
        }, userId: 1);

        Assert.Equal(new DateTime(2025, 1, 5), recorded.Date);
    }

    [Fact]
    public async Task ChangingTheGoLiveDate_WritesOneHistoryRow_AndAnIdenticalResaveWritesNone()
    {
        await using var context = Context();
        context.ActorUserId = 7;

        await Apply(context, GoLive);
        await Apply(context, GoLive);

        var trail = await context.FinanceRecordAudits.AsNoTracking()
            .Where(a => a.RecordType == nameof(FinanceSetting)).ToListAsync();
        var row = Assert.Single(trail);
        Assert.Equal(7, row.ActorUserId);
        Assert.Contains("GoLiveDate", row.Changes);
        Assert.Contains("2026-08-01", row.Changes);
    }

    // ── Helpers ──

    private static WhtService Wht(AppDbContext context) => new(context, new FinanceAccountService(context));

    /// <summary>Saves the settings the way the page does: with the version it last read.</summary>
    private static async Task<FinanceSettingsDto> Apply(AppDbContext context, DateTime? goLive)
    {
        var wht = Wht(context);
        var current = await wht.GetSettingsAsync();
        return await wht.UpdateSettingsAsync(new SaveFinanceSettingsDto
        {
            FinancialYearStartMonth = 7, GoLiveDate = goLive, ConcurrencyToken = current.ConcurrencyToken
        }, "admin");
    }

    private sealed record Accounts(int BankId, int CapitalAccountId, int PartnerId);

    private static async Task<Accounts> SeedAccountsAsync(AppDbContext context)
    {
        var bank = new FinanceAccount
        {
            Name = "HBL Bank", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Bank, IsActive = true
        };
        var capital = new FinanceAccount
        {
            Name = "Partner Capital", AccountHolderName = "Partner One",
            Type = FinanceAccountType.Capital, IsActive = true
        };
        context.FinanceAccounts.AddRange(bank, capital);
        await context.SaveChangesAsync();
        var partner = new CapitalPartner
        {
            Name = "Partner One", ProfitSharePercent = 100m, FinanceAccountId = capital.Id, IsActive = true
        };
        context.CapitalPartners.Add(partner);
        await context.SaveChangesAsync();
        return new Accounts(bank.Id, capital.Id, partner.Id);
    }

    private static async Task<int> SeedBookingAsync(AppDbContext context)
    {
        var project = new Project { ProjectName = "Cutover", Location = "Karachi", CreatedById = 1 };
        var unit = new Unit
        {
            Project = project, UnitNumber = "CU-1", UnitType = "Apartment",
            Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan
        };
        var customer = new Customer { FullName = "Cutover Buyer", Phone = "03004445555", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = "BK-CUTOVER", Customer = customer, Unit = unit,
            Status = BookingStatus.PaymentPlanActive, Source = CustomerSource.Referral,
            AgreedSalePrice = 1_000_000m, DiscountAmount = 0m,
            BookingAmountRequired = 200_000m, BookingAmountReceived = 200_000m,
            BookingDate = new DateTime(2026, 7, 10)
        };
        context.AddRange(project, unit, customer, booking);
        await context.SaveChangesAsync();
        return booking.Id;
    }

    private sealed record World(int BankId, int PartnerId, int BookingId);

    /// <summary>Accounts, a partner, a booking on a payment plan, and a 1 August go-live date.</summary>
    private static async Task<World> SeedAsync(AppDbContext context)
    {
        var world = await SeedAsyncWithoutGoLive(context);
        // Set directly: UpdateSettingsAsync would refuse this world, because the booking it needs
        // for the possession cases is dated before the go-live date. The date is all the posting
        // rules read, so writing it keeps the two halves of the boundary independent.
        await GoLiveSeed.SetAsync(context, GoLive);
        await context.SaveChangesAsync();
        return world;
    }

    private static async Task<World> SeedAsyncWithoutGoLive(AppDbContext context)
    {
        var accounts = await SeedAccountsAsync(context);
        var bookingId = await SeedBookingAsync(context);
        return new World(accounts.BankId, accounts.PartnerId, bookingId);
    }

    private static CapitalPartnerService Capital(AppDbContext context) =>
        new(context, new FinanceAccountService(context), TestAttachments.Writer());

    private static AppDbContext Context()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Database.EnsureCreated();
        // The in-memory store generates no row version, so give the seeded settings row one to round-trip.
        context.FinanceSettings.Single().RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }
}
