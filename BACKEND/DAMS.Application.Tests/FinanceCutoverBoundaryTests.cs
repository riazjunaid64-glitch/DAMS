using DAMS.Application.Common;
using DAMS.Application.DTOs.FinanceDtos;
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
/// A committed opening balance is read by every report as an AS-AT position: the figure the
/// accountant typed already contains everything that happened before its date, so the reports start
/// their windows there and add movements on top. That makes the baseline a boundary with two halves
/// to defend. Nothing may be POSTED behind it — <see cref="FinanceDateRules"/> — and nothing may
/// already BE behind it when it is committed, which no amount of posting validation can achieve
/// because those rows were legal when they were written.
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
    public async Task ACapitalContribution_CannotBeDatedBeforeTheCommittedBaseline()
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

        Assert.Contains("before the committed opening balance date", error.Message);
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
    public async Task Possession_CannotRecogniseASaleBehindTheCommittedBaseline()
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

        Assert.Contains("before the committed opening balance date", error.Message);
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

    // ── Nothing may already be behind the baseline when it is committed ──

    [Fact]
    public async Task CommittingABaseline_IsRefusedWhileEarlierFinancialRecordsExist()
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

        var service = new OpeningBalanceService(context);
        var set = await Draft(service, GoLive, accounts.BankId, accounts.CapitalAccountId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CommitAsync(set.Id, set.ConcurrencyToken, 1));

        Assert.Contains("1 expenses", error.Message);
        Assert.Contains("1 capital transactions", error.Message);
        // Names the earliest so the operator can act on it rather than go hunting.
        Assert.Contains("20 Jul 2026", error.Message);
        Assert.False(context.OpeningBalanceSets.Single().IsCommitted);
        // And the accounts keep their untouched opening balances: a refused commit writes nothing.
        Assert.All(await context.FinanceAccounts.ToListAsync(), a => Assert.Equal(0m, a.OpeningBalance));
    }

    [Fact]
    public async Task CommittingABaseline_IsAllowedWhenTheOnlyRecordsAreOnOrAfterIt()
    {
        await using var context = Context();
        var accounts = await SeedAccountsAsync(context);
        // On the boundary day, which belongs to the new period, so it is not inside the figures.
        context.Expenses.Add(new Expense
        {
            FinanceAccountId = accounts.BankId, Amount = 40_000m, Category = "Rent", Date = GoLive
        });
        await context.SaveChangesAsync();

        var service = new OpeningBalanceService(context);
        var set = await Draft(service, GoLive, accounts.BankId, accounts.CapitalAccountId);

        var committed = await service.CommitAsync(set.Id, set.ConcurrencyToken, 1);

        Assert.True(committed.IsCommitted);
        Assert.Equal(1_000_000m, (await context.FinanceAccounts.SingleAsync(a => a.Id == accounts.BankId)).OpeningBalance);
    }

    [Fact]
    public async Task TheCutoverCheck_LooksAtEveryKindOfFinancialRecord_NotJustExpenses()
    {
        // The double count does not care which table the movement is in, so neither does the check.
        // A receipt alone must be enough to stop the commit.
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

        var service = new OpeningBalanceService(context);
        var set = await Draft(service, GoLive, accounts.BankId, accounts.CapitalAccountId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CommitAsync(set.Id, set.ConcurrencyToken, 1));
        Assert.Contains("1 customer receipts", error.Message);
    }

    // ── Helpers ──

    private static async Task<OpeningBalanceSetDto> Draft(
        OpeningBalanceService service, DateTime asAt, int bankId, int capitalId)
    {
        var set = await service.CreateAsync(asAt, 1);
        return await service.SaveAsync(set.Id, new SaveOpeningBalanceSetDto
        {
            ConcurrencyToken = set.ConcurrencyToken,
            Entries =
            [
                new() { FinanceAccountId = bankId, DebitAmount = 1_000_000m },
                new() { FinanceAccountId = capitalId, CreditAmount = 1_000_000m }
            ]
        }, 1);
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

    /// <summary>Accounts, a partner, a booking on a payment plan, and a COMMITTED 1 August baseline.</summary>
    private static async Task<World> SeedAsync(AppDbContext context)
    {
        var accounts = await SeedAccountsAsync(context);
        var bookingId = await SeedBookingAsync(context);
        // Committed directly: CommitAsync would now refuse this world, because the booking it needs
        // for the possession cases is dated before the cutover. The baseline row is all the date
        // rules read, so writing it is enough and keeps the two halves of the boundary independent.
        context.OpeningBalanceSets.Add(new OpeningBalanceSet
        {
            AsAtDate = GoLive, IsCommitted = true, CommittedAt = DateTime.UtcNow, CommittedByUserId = 1
        });
        await context.SaveChangesAsync();
        return new World(accounts.BankId, accounts.PartnerId, bookingId);
    }

    private static CapitalPartnerService Capital(AppDbContext context) =>
        new(context, new FinanceAccountService(context));

    private static AppDbContext Context()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Database.EnsureCreated();
        return context;
    }
}
