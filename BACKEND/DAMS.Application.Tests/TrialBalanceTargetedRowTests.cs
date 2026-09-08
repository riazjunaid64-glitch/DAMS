using DAMS.Application.DTOs.FinanceDtos;
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
/// Opening one account's Details used to rebuild the whole Trial Balance so that a single row could
/// be read out of it. It now calculates that one row directly, which is only safe while the targeted
/// calculation and the report agree on every kind of row the report can produce — physical accounts,
/// the system-derived ones that are folded out of other sources, both virtual P&amp;L families and the
/// allocation row. These tests hold the two together.
/// <para>
/// Details refuses to answer unless its expected row reconciles with an independently-built ledger,
/// so asserting that the returned closing equals the FULL report's cell pins the targeted row from
/// both sides at once: report == targeted row == ledger.
/// </para>
/// </summary>
public sealed class TrialBalanceTargetedRowTests
{
    private static readonly DateTime Day = new(2026, 8, 20);

    [Fact]
    public async Task EveryRowTheReportProduces_MatchesItsTargetedCalculation()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var finance = Finance(context);

        var trial = await finance.GetTrialBalanceAsync(null, Day, 0);

        // The scenario is only worth anything if it actually reached each family of row.
        Assert.Contains(trial.Rows, row => row.AccountKey == $"A:{world.Bank.Id}");
        Assert.Contains(trial.Rows, row => row.AccountKey == $"A:{world.TaxPayable.Id}");
        Assert.Contains(trial.Rows, row => row.AccountKey == $"A:{world.Deposits.Id}");
        Assert.Contains(trial.Rows, row => row.AccountKey == $"A:{world.Receivables.Id}");
        Assert.Contains(trial.Rows, row => row.AccountKey == $"A:{world.RefundPayable.Id}");
        Assert.Contains(trial.Rows, row => row.AccountKey == $"A:{world.CommissionPayable.Id}");
        Assert.Contains(trial.Rows, row => row.AccountKey == $"A:{world.AssetAccount.Id}");
        Assert.Contains(trial.Rows, row => row.AccountKey == $"A:{world.CapitalAccount.Id}");
        Assert.Contains(trial.Rows, row => row.AccountKey == "I:unit-sales");
        Assert.Contains(trial.Rows, row => row.AccountKey == "I:cancellation-retained");
        Assert.Contains(trial.Rows, row => row.AccountKey.StartsWith("I:", StringComparison.Ordinal)
            && row.AccountKey.Contains(':', StringComparison.Ordinal) && row.AccountName == "Consulting");
        Assert.Contains(trial.Rows, row => row.AccountKey == "E:commission-expense");
        Assert.Contains(trial.Rows, row => row.AccountName == "Office Cost");
        Assert.Contains(trial.Rows, row => row.AccountKey == "EQ:allocated");

        foreach (var row in trial.Rows)
            await AssertRowMatchesDetailsAsync(finance, row, null);
    }

    [Fact]
    public async Task EveryRowMatches_UnderAProjectFilter()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var finance = Finance(context);

        var trial = await finance.GetTrialBalanceAsync(world.ProjectId, Day, 0);

        Assert.NotEmpty(trial.Rows);
        // A project filter suppresses the allocation row, exactly as the report does.
        Assert.DoesNotContain(trial.Rows, row => row.AccountKey == "EQ:allocated");
        foreach (var row in trial.Rows)
            await AssertRowMatchesDetailsAsync(finance, row, world.ProjectId);
    }

    [Fact]
    public async Task EveryRowMatches_UnderACommittedOpeningBaseline()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        // Dated before every seeded movement, which is the only shape a committed baseline can take:
        // FinanceDateRules refuses to record anything earlier, so the opening balances stand as the
        // whole of the history before this date. With one present, the accounts carry their opening
        // figure and the P&L accumulates from the baseline rather than from the SQL floor — and the
        // targeted row has to reach the same cell the report does under both rules.
        var baseline = Day.AddDays(-10);
        context.OpeningBalanceSets.Add(new OpeningBalanceSet
        {
            AsAtDate = baseline, IsCommitted = true, CommittedAt = baseline.ToUniversalTime()
        });
        await context.SaveChangesAsync();
        var finance = Finance(context);

        var trial = await finance.GetTrialBalanceAsync(null, Day, 0);

        Assert.NotEmpty(trial.Rows);
        // The opening balance is only carried when there is no project filter, and it is carried here.
        var bankRow = Assert.Single(trial.Rows, row => row.AccountKey == $"A:{world.Bank.Id}");
        Assert.True(bankRow.Debit > 0m);
        foreach (var row in trial.Rows)
            await AssertRowMatchesDetailsAsync(finance, row, null);
    }

    [Fact]
    public async Task AnUnknownAccountKey_IsRefusedWithoutBuildingAnything()
    {
        await using var context = Context();
        await SeedAsync(context);
        var finance = Finance(context);

        foreach (var key in new[] { "A:999999", "I:", "E:", "X:1", "I:not-a-real-head", "E:12345:Nothing" })
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => finance.GetTrialBalanceDetailsAsync(key, null, Day, Day));
            Assert.Equal(
                "The selected Trial Balance account is not available for these filters.", error.Message);
        }
    }

    [Fact]
    public async Task TheAllocationRow_IsNotOfferedUnderAProjectFilter()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var finance = Finance(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => finance.GetTrialBalanceDetailsAsync("EQ:allocated", world.ProjectId, Day, Day));
        Assert.Equal(
            "The selected Trial Balance account is not available for these filters.", error.Message);
    }

    /// <summary>
    /// Details answers only if its expected Trial Balance row reconciled against a ledger built from
    /// different queries, so equality with the report's own cell chains the three together.
    /// </summary>
    private static async Task AssertRowMatchesDetailsAsync(
        IFinanceService finance, TrialBalanceRowDto row, int? projectId)
    {
        var details = await finance.GetTrialBalanceDetailsAsync(row.AccountKey, projectId, Day, Day);
        var debit = details.ClosingBalanceType == "Debit" ? details.ClosingBalance : 0m;
        var credit = details.ClosingBalanceType == "Credit" ? details.ClosingBalance : 0m;

        Assert.Equal(row.Debit, debit);
        Assert.Equal(row.Credit, credit);
        Assert.Equal(row.AccountName, details.AccountName);
        Assert.Equal(row.LedgerCode, details.LedgerCode);
    }

    private sealed record World(
        int ProjectId,
        FinanceAccount Bank,
        FinanceAccount TaxPayable,
        FinanceAccount Deposits,
        FinanceAccount Receivables,
        FinanceAccount RefundPayable,
        FinanceAccount CommissionPayable,
        FinanceAccount AssetAccount,
        FinanceAccount CapitalAccount);

    /// <summary>
    /// One day that touches every path the Trial Balance knows: cash in and out, withheld tax and its
    /// deposit, a recognised sale and the receivable behind it, a cancellation with a refund, an
    /// accrued and part-paid commission, a fixed asset, capital and its profit/loss allocation.
    /// </summary>
    private static async Task<World> SeedAsync(AppDbContext context)
    {
        var bank = new FinanceAccount
        {
            Name = "Collection Bank", LedgerCode = "BANK-1", AccountHolderName = "DAMS",
            Type = FinanceAccountType.Bank, OpeningBalance = 250_000m, IsActive = true
        };
        var taxPayable = new FinanceAccount
        {
            Name = "Tax Payable", AccountHolderName = "DAMS", Type = FinanceAccountType.Liability,
            SystemRole = FinanceSystemAccountRole.TaxPayable, DisplayOrder = 510, IsActive = true
        };
        var deposits = new FinanceAccount
        {
            Name = "Customer Deposits", AccountHolderName = "DAMS", Type = FinanceAccountType.Liability,
            SystemRole = FinanceSystemAccountRole.CustomerDeposits, DisplayOrder = 500, IsActive = true
        };
        var receivables = new FinanceAccount
        {
            Name = "Customer Receivables", AccountHolderName = "DAMS", Type = FinanceAccountType.Receivable,
            SystemRole = FinanceSystemAccountRole.CustomerReceivables, DisplayOrder = 420, IsActive = true
        };
        var refundPayable = new FinanceAccount
        {
            Name = "Customer Refund Payable", AccountHolderName = "DAMS", Type = FinanceAccountType.Liability,
            SystemRole = FinanceSystemAccountRole.CustomerRefundPayable, DisplayOrder = 515, IsActive = true
        };
        var commissionPayable = new FinanceAccount
        {
            Name = "Commission Payable", AccountHolderName = "DAMS", Type = FinanceAccountType.Liability,
            SystemRole = FinanceSystemAccountRole.CommissionPayable, DisplayOrder = 517, IsActive = true
        };
        var assetAccount = new FinanceAccount
        {
            Name = "Office Equipment", AccountHolderName = "DAMS", Type = FinanceAccountType.FixedAsset,
            DisplayOrder = 300, IsActive = true
        };
        var capitalAccount = new FinanceAccount
        {
            Name = "Partner Capital", AccountHolderName = "DAMS", Type = FinanceAccountType.Capital,
            DisplayOrder = 600, IsActive = true
        };

        var project = new Project { ProjectName = "Parity Heights", Location = "Islamabad", CreatedById = 1 };
        var soldUnit = new Unit
        {
            Project = project, UnitNumber = "A-1", UnitType = "Apartment",
            Price = 4_000_000m, Status = UnitStatus.Sold
        };
        var cancelledUnit = new Unit
        {
            Project = project, UnitNumber = "A-2", UnitType = "Apartment",
            Price = 2_000_000m, Status = UnitStatus.Available
        };
        var buyer = new Customer { FullName = "Parity Buyer", Phone = "03001112222", Status = CustomerStatus.Active };
        var leaver = new Customer { FullName = "Parity Leaver", Phone = "03003334444", Status = CustomerStatus.Active };
        var soldBooking = new Booking
        {
            BookingReference = "BK-SOLD", Customer = buyer, Unit = soldUnit, Source = CustomerSource.Referral,
            Status = BookingStatus.SaleCompleted, AgreedSalePrice = 4_000_000m, DiscountAmount = 0m,
            BookingAmountRequired = 0m, BookingAmountReceived = 0m, BookingDate = Day.AddDays(-10)
        };
        var cancelledBooking = new Booking
        {
            BookingReference = "BK-CANC", Customer = leaver, Unit = cancelledUnit, Source = CustomerSource.WalkIn,
            Status = BookingStatus.Cancelled, AgreedSalePrice = 2_000_000m, DiscountAmount = 0m,
            BookingAmountRequired = 0m, BookingAmountReceived = 0m, BookingDate = Day.AddDays(-10)
        };
        var partner = new ThirdPartyPartner
        {
            Name = "Parity Broker", PartnerType = "Broker", InternalCode = "PB-1", IsActive = true,
            BankName = "Test Bank", AccountTitle = "Parity Broker", AccountNumber = "00123456789"
        };
        var capitalPartner = new CapitalPartner
        {
            Name = "Parity Partner", FinanceAccount = capitalAccount, IsActive = true
        };
        var revenueCategory = new RevenueCategory { Name = "Consulting", Code = "consulting", DisplayOrder = 1 };
        var expenseCategory = new ExpenseCategory { Name = "Office Cost", Code = "office-cost", DisplayOrder = 1 };

        context.AddRange(bank, taxPayable, deposits, receivables, refundPayable, commissionPayable,
            assetAccount, capitalAccount, project, soldUnit, cancelledUnit, buyer, leaver,
            soldBooking, cancelledBooking, partner, capitalPartner, revenueCategory, expenseCategory);
        await context.SaveChangesAsync();

        // Cash collected on both bookings, and the sale that turns one of them into a receivable.
        context.Payments.AddRange(
            new Payment
            {
                BookingId = soldBooking.Id, FinanceAccountId = bank.Id, Amount = 1_500_000m,
                Type = PaymentType.Installment, PaymentMethod = PaymentMethod.BankTransfer,
                PaidAt = Day.AddDays(-5), CreatedAt = Day.AddDays(-5)
            },
            new Payment
            {
                BookingId = cancelledBooking.Id, FinanceAccountId = bank.Id, Amount = 600_000m,
                Type = PaymentType.BookingAmount, PaymentMethod = PaymentMethod.Cash,
                PaidAt = Day.AddDays(-4), CreatedAt = Day.AddDays(-4)
            });
        context.BookingSaleRecognitions.Add(new BookingSaleRecognition
        {
            BookingId = soldBooking.Id, RecognitionDate = Day.AddDays(-2),
            NetSaleValue = 4_000_000m, RecognizedAt = Day.AddDays(-2)
        });

        // The cancellation raises the refund liability and retains the rest as income; the refund
        // itself then clears part of that liability out of the bank.
        var settlement = new BookingCancellationSettlement
        {
            BookingId = cancelledBooking.Id, CustomerCashReceivedSnapshot = 600_000m,
            RefundAmount = 400_000m, RetainedAmount = 200_000m,
            RefundDecision = CancellationRefundDecision.PayLater,
            RefundPayableAccountId = refundPayable.Id, Reason = "Buyer withdrew",
            IdempotencyKey = "cancel-1", CancelledByUserId = 1, CancelledByName = "Admin",
            CancelledAt = Day.AddDays(-3), CancellationDate = Day.AddDays(-3)
        };
        context.BookingCancellationSettlements.Add(settlement);
        await context.SaveChangesAsync();
        context.BookingCancellationRefunds.Add(new BookingCancellationRefund
        {
            SettlementId = settlement.Id, FinanceAccountId = bank.Id, Amount = 150_000m,
            PaidAt = Day.AddDays(-1), PaymentMethod = PaymentMethod.BankTransfer,
            IdempotencyKey = "refund-1", RecordedByUserId = 1, RecordedByName = "Admin",
            RecordedAt = Day.AddDays(-1)
        });

        // Manual revenue, an expense that withholds tax, the deposit of that tax, and a fixed asset.
        context.ManualRevenues.Add(new ManualRevenue
        {
            ProjectId = project.Id, FinanceAccountId = bank.Id, RevenueCategoryId = revenueCategory.Id,
            RevenueType = revenueCategory.Name, RevenueTypeName = revenueCategory.Name,
            Amount = 90_000m, Date = Day.AddDays(-6), CreatedAt = Day.AddDays(-6), Reference = "MR-1"
        });
        context.Expenses.Add(new Expense
        {
            ProjectId = project.Id, FinanceAccountId = bank.Id, CategoryId = expenseCategory.Id,
            Category = expenseCategory.Name, Amount = 100_000m, WhtAmount = 6_000m, WhtRate = 6m,
            Date = Day.AddDays(-6), CreatedAt = Day.AddDays(-6), Vendor = "Supplies Ltd"
        });
        context.WhtDeposits.Add(new WhtDeposit
        {
            FinanceAccountId = bank.Id, Amount = 2_000m, DepositDate = Day.AddDays(-1),
            ChallanNumber = "CH-1", CreatedAt = Day.AddDays(-1)
        });
        context.AssetPurchases.Add(new AssetPurchase
        {
            ProjectId = project.Id, FinanceAccountId = bank.Id, AssetAccountId = assetAccount.Id,
            ItemName = "Server", Category = expenseCategory.Name, CategoryId = expenseCategory.Id,
            Amount = 300_000m, WhtAmount = 15_000m, WhtRate = 5m,
            Date = Day.AddDays(-4), CreatedAt = Day.AddDays(-4)
        });

        // Capital: a contribution through the bank, plus a profit and a loss allocation.
        context.CapitalTransactions.AddRange(
            new CapitalTransaction
            {
                CapitalPartnerId = capitalPartner.Id, FinanceAccountId = bank.Id,
                Type = CapitalTransactionType.Contribution, Amount = 500_000m,
                Date = Day.AddDays(-7), CreatedAt = Day.AddDays(-7), Reference = "CAP-1"
            },
            new CapitalTransaction
            {
                CapitalPartnerId = capitalPartner.Id, Type = CapitalTransactionType.ProfitShare,
                Amount = 120_000m, Date = Day.AddDays(-2), CreatedAt = Day.AddDays(-2), Reference = "PS-1"
            },
            new CapitalTransaction
            {
                CapitalPartnerId = capitalPartner.Id, Type = CapitalTransactionType.LossShare,
                Amount = 20_000m, Date = Day.AddDays(-2), CreatedAt = Day.AddDays(-2), Reference = "LS-1"
            });
        await context.SaveChangesAsync();

        // A commission agreed and part-paid: the accrual is the cost and the payable, the payout
        // settles part of it out of the bank.
        var commission = new BookingCommission
        {
            BookingId = soldBooking.Id, PartnerId = partner.Id,
            PartnerNameSnapshot = partner.Name, PartnerTypeSnapshot = partner.PartnerType,
            PartnerInternalCodeSnapshot = partner.InternalCode,
            CalculationType = FinancialCalculationType.Percentage,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            PercentageRate = 2m, BasisAmount = 4_000_000m, CalculatedAmount = 80_000m,
            FinalAmount = 80_000m
        };
        context.BookingCommissions.Add(commission);
        await context.SaveChangesAsync();
        context.CommissionAccruals.Add(new CommissionAccrual
        {
            CommissionId = commission.Id, Amount = 80_000m, AccruedOn = Day.AddDays(-2),
            Kind = CommissionAccrualKind.Recognition, RecordedAt = Day.AddDays(-2)
        });
        context.CommissionPayouts.Add(new CommissionPayout
        {
            CommissionId = commission.Id, FinanceAccountId = bank.Id, Amount = 30_000m,
            PaymentDate = Day.AddDays(-1), PaymentMethod = PaymentMethod.BankTransfer,
            IdempotencyKey = "payout-1", RecordedAt = Day.AddDays(-1)
        });
        await context.SaveChangesAsync();

        return new World(project.Id, bank, taxPayable, deposits, receivables, refundPayable,
            commissionPayable, assetAccount, capitalAccount);
    }

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts,
            new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
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
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) =>
            Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
