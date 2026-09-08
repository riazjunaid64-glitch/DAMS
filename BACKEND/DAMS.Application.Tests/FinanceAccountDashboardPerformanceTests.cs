using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class FinanceAccountDashboardPerformanceTests
{
    private static readonly DateTime Day = new(2026, 8, 20);

    [Fact]
    public async Task PeriodMovement_IncludesEveryCashSource_AndPreservesDateBounds()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);

        // Each source has a distinct amount so omitting a movement or using its gross/net or
        // principal/interest counterpart changes the answer. The two extra revenues bracket the
        // selected day and make both the inclusive lower and exclusive upper bound observable.
        var daily = await service.GetDashboardAsync(world.ProjectId, Day, Day, world.BankId);
        Assert.Equal(15m, daily.Summary.AccountNetMovement);
        Assert.Equal(715m, daily.Summary.AccountCurrentBalance);
        Assert.Equal(500m, daily.Summary.AccountOpeningBalance);

        var throughDay = await service.GetSummaryAsync(null, null, Day, world.BankId);
        var fromDay = await service.GetSummaryAsync(null, Day, null, world.BankId);
        var allTime = await service.GetSummaryAsync(null, null, null, world.BankId);
        var reversed = await service.GetSummaryAsync(null, Day.AddDays(1), Day.AddDays(-1), world.BankId);
        var empty = await service.GetSummaryAsync(null, Day.AddDays(1), Day, world.BankId);

        Assert.Equal(215m, throughDay.AccountNetMovement);
        Assert.Equal(315m, fromDay.AccountNetMovement);
        Assert.Equal(515m, allTime.AccountNetMovement);
        Assert.Equal(-15m, reversed.AccountNetMovement);
        Assert.Equal(0m, empty.AccountNetMovement);

        // Other account sides keep their original cash-movement meaning. Loan liabilities and
        // partner capital are balance movements, while this card counts only their cash side.
        Assert.Equal(127m, (await service.GetSummaryAsync(null, Day, Day, world.AssetId)).AccountNetMovement);
        Assert.Equal(34m, (await service.GetSummaryAsync(null, Day, Day, world.StaffId)).AccountNetMovement);
        Assert.Equal(0m, (await service.GetSummaryAsync(null, Day, Day, world.LoanId)).AccountNetMovement);
        Assert.Equal(0m, (await service.GetSummaryAsync(null, Day, Day, world.CapitalId)).AccountNetMovement);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SelectedSnapshot_MatchesWholeBalanceSheet_ForEveryAccountRole(int daysAfterActivity)
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        context.OpeningBalanceSets.Add(new OpeningBalanceSet
        {
            AsAtDate = Day, IsCommitted = true, CommittedAt = Day
        });
        await context.SaveChangesAsync();
        var service = Finance(context);
        var date = Day.AddDays(daysAfterActivity);
        var sheet = await service.GetBalanceSheetAsync(null, date);
        var balances = sheet.AssetGroups.Concat(sheet.LiabilityGroups).SelectMany(g => g.Lines)
            .Concat(sheet.CapitalLines).Where(l => l.AccountId > 0)
            .ToDictionary(l => l.AccountId, l => l.Amount);
        foreach (var account in await context.FinanceAccounts.AsNoTracking().ToListAsync())
        {
            // The selected account's position remains account-wide even when the chart is scoped
            // to a project with none of that account's activity.
            var selected = await service.GetSummaryAsync(world.OtherProjectId, Day.AddDays(-2), date, account.Id);
            Assert.Equal(balances.GetValueOrDefault(account.Id), selected.AccountCurrentBalance);
            Assert.Equal(daysAfterActivity >= 0 ? account.OpeningBalance : 0m, selected.AccountOpeningBalance);
        }
    }

    [Fact]
    public async Task UnrelatedAccountsAndHistory_DoNotChangeSelectedResults()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);
        var before = await service.GetDashboardAsync(null, Day, Day, world.BankId);
        for (var i = 0; i < 100; i++)
        {
            var account = Account($"Unrelated {i}", FinanceAccountType.Bank);
            context.ManualRevenues.Add(new ManualRevenue
            {
                FinanceAccount = account, Amount = 99_999m, Date = Day,
                RevenueType = "Other income", RevenueTypeName = "Other income"
            });
        }
        await context.SaveChangesAsync();
        var after = await service.GetDashboardAsync(null, Day, Day, world.BankId);
        Assert.Equal(before.Summary.AccountCurrentBalance, after.Summary.AccountCurrentBalance);
        Assert.Equal(before.Summary.AccountOpeningBalance, after.Summary.AccountOpeningBalance);
        Assert.Equal(before.Summary.AccountNetMovement, after.Summary.AccountNetMovement);
        Assert.Equal(before.Summary.TotalRevenue, after.Summary.TotalRevenue);
        Assert.Equal(before.Summary.TotalExpenses, after.Summary.TotalExpenses);
    }

    private sealed record World(int BankId, int AssetId, int StaffId, int LoanId, int CapitalId,
        int ProjectId, int OtherProjectId);

    private static async Task<World> SeedAsync(AppDbContext context)
    {
        var bank = Account("Bank", FinanceAccountType.Bank);
        bank.OpeningBalance = 500m;
        var asset = Account("Equipment", FinanceAccountType.FixedAsset);
        var staff = Account("Staff", FinanceAccountType.StaffFloat);
        var liability = Account("Loan", FinanceAccountType.Liability);
        var capital = Account("Partner", FinanceAccountType.Capital);
        var tax = Account("Tax", FinanceAccountType.Liability, FinanceSystemAccountRole.TaxPayable);
        var deposits = Account("Deposits", FinanceAccountType.Liability, FinanceSystemAccountRole.CustomerDeposits);
        var receivables = Account("Receivables", FinanceAccountType.Receivable, FinanceSystemAccountRole.CustomerReceivables);
        var refunds = Account("Refunds", FinanceAccountType.Liability, FinanceSystemAccountRole.CustomerRefundPayable);
        var payable = Account("Commissions", FinanceAccountType.Liability, FinanceSystemAccountRole.CommissionPayable);
        var project = new Project { ProjectName = "Activity", Location = "Karachi", CreatedById = 1 };
        var otherProject = new Project { ProjectName = "Empty", Location = "Karachi", CreatedById = 1 };
        var customer = new Customer { FullName = "Customer", Phone = "03001112222", Status = CustomerStatus.Active };
        Booking Booking(string reference, BookingStatus status) => new()
        {
            BookingReference = reference, Customer = customer, Status = status,
            Unit = new Unit { Project = project, UnitNumber = reference, UnitType = "Apartment", Price = 1000m },
            BookingDate = Day.AddDays(-3), AgreedSalePrice = 1000m
        };
        var sold = Booking("SOLD", BookingStatus.PossessionGiven);
        var cancelled = Booking("CANCELLED", BookingStatus.Cancelled);
        var pending = Booking("PENDING", BookingStatus.PaymentPlanActive);
        var broker = new ThirdPartyPartner { Name = "Broker", PartnerType = "Broker", InternalCode = "BR" };
        var partner = new CapitalPartner { Name = "Partner", FinanceAccount = capital };
        var loan = new Loan { Name = "Loan", FinanceAccount = liability };
        context.AddRange(bank, asset, staff, liability, capital, tax, deposits, receivables, refunds, payable,
            project, otherProject, sold, cancelled, pending, broker, partner, loan);
        await context.SaveChangesAsync();

        foreach (var (booking, amount) in new[] { (sold, 101m), (pending, 67m), (cancelled, 73m) })
            context.Payments.Add(new Payment
            {
                BookingId = booking.Id, FinanceAccountId = bank.Id, Amount = amount,
                PaidAt = Day.AddHours(12), CreatedAt = Day.AddHours(12), Type = PaymentType.BookingAmount
            });
        context.BookingSaleRecognitions.Add(new BookingSaleRecognition
        {
            BookingId = sold.Id, RecognitionDate = Day, RecognizedAt = Day, NetSaleValue = 1000m
        });
        var settlement = new BookingCancellationSettlement
        {
            BookingId = cancelled.Id, CancellationDate = Day, CancelledAt = Day,
            CustomerCashReceivedSnapshot = 73m, RefundAmount = 251m, RetainedAmount = 0m,
            RefundPayableAccountId = refunds.Id, Reason = "Cancelled", IdempotencyKey = "settlement",
            CancelledByName = "Admin", RefundDecision = CancellationRefundDecision.PayLater
        };
        var commission = new BookingCommission
        {
            BookingId = sold.Id, PartnerId = broker.Id, FinalAmount = 257m,
            PartnerNameSnapshot = "Broker", PartnerTypeSnapshot = "Broker", PartnerInternalCodeSnapshot = "BR"
        };
        var rebate = new CustomerRebate
        {
            BookingId = sold.Id, CustomerId = customer.Id, FinalAmount = 174m, Reason = "Rebate",
            Method = CustomerRebateMethod.CashOrBankPayment
        };
        context.AddRange(settlement, commission, rebate);
        await context.SaveChangesAsync();

        context.ManualRevenues.AddRange(
            new ManualRevenue { FinanceAccountId = bank.Id, ProjectId = project.Id, Date = Day, Amount = 103m },
            new ManualRevenue { FinanceAccountId = bank.Id, Date = Day.AddDays(-1).AddHours(23), Amount = 200m },
            new ManualRevenue { FinanceAccountId = bank.Id, Date = Day.AddDays(1), Amount = 300m });
        context.Expenses.Add(new Expense
        {
            FinanceAccountId = bank.Id, ProjectId = project.Id, Date = Day, Category = "Expense",
            Amount = 107m, WhtAmount = 11m
        });
        context.AssetPurchases.Add(new AssetPurchase
        {
            FinanceAccountId = bank.Id, AssetAccountId = asset.Id, ProjectId = project.Id,
            Date = Day, Amount = 127m, WhtAmount = 19m, ItemName = "Asset", Category = "Equipment"
        });
        context.WhtDeposits.Add(new WhtDeposit { FinanceAccountId = bank.Id, DepositDate = Day, Amount = 23m, ChallanNumber = "WHT" });
        context.LoanTransactions.AddRange(
            new LoanTransaction { LoanId = loan.Id, FinanceAccountId = bank.Id, Date = Day, Type = LoanTransactionType.Drawdown, PrincipalAmount = 131m },
            new LoanTransaction { LoanId = loan.Id, FinanceAccountId = bank.Id, Date = Day, Type = LoanTransactionType.Repayment, PrincipalAmount = 29m, InterestAmount = 31m });
        context.CapitalTransactions.AddRange(
            new CapitalTransaction { CapitalPartnerId = partner.Id, FinanceAccountId = bank.Id, Date = Day, Type = CapitalTransactionType.Contribution, Amount = 137m },
            new CapitalTransaction { CapitalPartnerId = partner.Id, FinanceAccountId = bank.Id, Date = Day, Type = CapitalTransactionType.Withdrawal, Amount = 37m },
            new CapitalTransaction { CapitalPartnerId = partner.Id, Date = Day, Type = CapitalTransactionType.ProfitShare, Amount = 53m },
            new CapitalTransaction { CapitalPartnerId = partner.Id, Date = Day, Type = CapitalTransactionType.LossShare, Amount = 59m });
        context.StaffCashTransfers.AddRange(
            new StaffCashTransfer { StaffFinanceAccountId = staff.Id, CounterpartyFinanceAccountId = bank.Id, Date = Day, Type = StaffCashMovementType.FundsGiven, Amount = 41m },
            new StaffCashTransfer { StaffFinanceAccountId = staff.Id, CounterpartyFinanceAccountId = bank.Id, Date = Day, Type = StaffCashMovementType.FundsReturned, Amount = 7m });
        context.BookingCancellationRefunds.Add(new BookingCancellationRefund
        {
            SettlementId = settlement.Id, FinanceAccountId = bank.Id, PaidAt = Day, Amount = 47m,
            RecordedByName = "Admin", IdempotencyKey = "refund"
        });
        context.CommissionAccruals.Add(new CommissionAccrual
        {
            CommissionId = commission.Id, AccruedOn = Day, Amount = 257m, Kind = CommissionAccrualKind.Recognition
        });
        var payout = new CommissionPayout
        {
            CommissionId = commission.Id, FinanceAccountId = bank.Id, PaymentDate = Day,
            Amount = 109m, IdempotencyKey = "payout"
        };
        var cashRebate = new RebateDisbursement
        {
            RebateId = rebate.Id, FinanceAccountId = bank.Id, AppliedAt = Day,
            Method = CustomerRebateMethod.CashOrBankPayment, Amount = 113m, IdempotencyKey = "cash-rebate"
        };
        var credit = new RebateDisbursement
        {
            RebateId = rebate.Id, AppliedAt = Day, Method = CustomerRebateMethod.CreditNote,
            Amount = 61m, IdempotencyKey = "credit"
        };
        context.AddRange(payout, cashRebate, credit);
        await context.SaveChangesAsync();
        context.CommissionPayoutReversals.Add(new CommissionPayoutReversal
        {
            PayoutId = payout.Id, ReversedAt = Day, Amount = 13m, Reason = "Reversed", IdempotencyKey = "payout-reversal"
        });
        context.RebateDisbursementReversals.AddRange(
            new RebateDisbursementReversal { DisbursementId = cashRebate.Id, ReversedAt = Day, Amount = 17m, Reason = "Reversed", IdempotencyKey = "rebate-reversal" },
            new RebateDisbursementReversal { DisbursementId = credit.Id, ReversedAt = Day, Amount = 5m, Reason = "Reversed", IdempotencyKey = "credit-reversal" });
        await context.SaveChangesAsync();
        return new World(bank.Id, asset.Id, staff.Id, liability.Id, capital.Id, project.Id, otherProject.Id);
    }

    private static FinanceAccount Account(string name, FinanceAccountType type,
        FinanceSystemAccountRole role = FinanceSystemAccountRole.None) => new()
    {
        Name = name, AccountHolderName = "DAMS", Type = type, SystemRole = role, IsActive = true
    };

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts,
            new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
    }

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
