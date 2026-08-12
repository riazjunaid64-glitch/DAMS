using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class FinanceAccountTests
{
    [Fact]
    public async Task Balance_IsOpeningPlusRevenueMinusExpenses()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(Account());
        context.ManualRevenues.Add(new ManualRevenue { FinanceAccountId = 1, Amount = 500, RevenueType = "Income" });
        context.Expenses.Add(new Expense { FinanceAccountId = 1, Amount = 200, Category = "Office" });
        await context.SaveChangesAsync();

        var result = await new FinanceAccountService(context).GetByIdAsync(1);
        Assert.Equal(1300, result.CurrentBalance);
        Assert.Equal(300, result.NetMovement);
        Assert.Equal(2, result.TransactionCount);
    }

    /// <summary>
    /// Customer payments are the largest inflow in the business. Before they were linked to an
    /// account the balance omitted them entirely, so an account that had taken in far more than it
    /// spent still reported a negative number.
    /// </summary>
    [Fact]
    public async Task Balance_CountsCustomerPayments()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(Account());
        Seed(context, payment: 8000, expense: 2000);
        await context.SaveChangesAsync();

        var result = await new FinanceAccountService(context).GetByIdAsync(1);

        // Opening 1000 + 8000 received - 2000 spent. Without the payment link this read -1000.
        Assert.Equal(7000, result.CurrentBalance);
        Assert.Equal(6000, result.NetMovement);
        Assert.Equal(8000, result.RevenueReceived);
        Assert.Equal(2, result.TransactionCount);
    }

    [Fact]
    public async Task AccountLedger_ShowsCustomerPayments_AndTheAccountCannotThenBeDeleted()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(Account());
        Seed(context, payment: 8000);
        await context.SaveChangesAsync();
        var accounts = new FinanceAccountService(context);

        var page = await accounts.GetTransactionsAsync(1, 0, 20);
        var row = Assert.Single(page.Items, t => t.Kind == "Customer payment");
        Assert.Equal(8000, row.Amount);
        Assert.Equal("Buyer", row.Label);

        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.DeleteUnusedAsync(1));
    }

    /// <summary>
    /// Filtering the dashboard by account used to report zero revenue, because payments had no
    /// account to match on and were excluded outright rather than filtered.
    /// </summary>
    [Fact]
    public async Task AccountFilteredRevenue_IncludesPaymentsMadeIntoThatAccount()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(Account());
        Seed(context, payment: 8000);
        await context.SaveChangesAsync();
        var finance = Finance(context);

        var summary = await finance.GetSummaryAsync(null, null, null, accountId: 1);
        Assert.Equal(8000, summary.AutomaticRevenue);
        Assert.Equal(8000, summary.TotalRevenue);
        Assert.Equal(9000, summary.AccountCurrentBalance);

        var rows = await finance.GetRevenuePageAsync(null, null, null, 0, 20, accountId: 1);
        Assert.Single(rows.Items, x => x.Source == "Payment" && x.FinanceAccountId == 1);
    }

    /// <summary>
    /// "Unassigned" must mean payments with no account. It previously matched none of them, so the
    /// filter returned every payment ever taken.
    /// </summary>
    [Fact]
    public async Task UnassignedFilter_ShowsOnlyPaymentsWithNoAccount()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(Account());
        Seed(context, payment: 8000);
        context.Payments.Add(new Payment { Id = 99, BookingId = 1, Amount = 250, FinanceAccountId = null });
        await context.SaveChangesAsync();
        var finance = Finance(context);

        var summary = await finance.GetSummaryAsync(null, null, null, unassigned: true);
        Assert.Equal(250, summary.AutomaticRevenue);

        var rows = await finance.GetRevenuePageAsync(null, null, null, 0, 20, unassigned: true);
        Assert.Single(rows.Items, x => x.Source == "Payment");
    }

    [Fact]
    public async Task RecordingAPayment_RequiresAnAccount()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(Account());
        Seed(context);
        await context.SaveChangesAsync();
        var bookings = new BookingService(context, new CustomerService(context), new FinanceAccountService(context));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bookings.RecordBookingAmountPaymentAsync(1,
                new DTOs.BookingDtos.RecordBookingAmountPaymentDto { Amount = 100m, PaymentMethod = PaymentMethod.Cash }, 1));
        Assert.Contains("Received In Account is required", error.Message);
    }

    [Fact]
    public async Task InactiveAccount_CannotBeNewlyAssigned_ButCanRemainOnExistingRecord()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(Account(isActive: false));
        await context.SaveChangesAsync();
        var accounts = new FinanceAccountService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.EnsureSelectableAsync(1));
        await accounts.EnsureSelectableAsync(1, currentAccountId: 1);
    }

    [Fact]
    public async Task NewTransactionsRequireAnAccount_AndLegacyUnassignedFilteringStillWorks()
    {
        await using var context = Context();
        context.ManualRevenues.Add(new ManualRevenue { Amount = 10, RevenueType = "Legacy" });
        await context.SaveChangesAsync();
        var finance = Finance(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => finance.CreateExpenseAsync(new CreateExpenseDto { Amount = 1, Category = "Office" }, 1));
        var rows = await finance.GetRevenuePageAsync(null, null, null, 0, 20, unassigned: true);
        Assert.Contains(rows.Items, x => x.RevenueType == "Legacy" && x.FinanceAccountId == null);
    }

    [Fact]
    public async Task AccountFilteredSummary_DoesNotIncludeUnassignedBookingReceivables()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(Account());
        await context.SaveChangesAsync();
        var finance = Finance(context);

        var summary = await finance.GetSummaryAsync(null, null, null, accountId: 1);

        Assert.Equal(0, summary.OutstandingAmount);
        Assert.Equal(0, summary.OverdueAmount);
    }

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts, new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
    }

    /// <summary>
    /// A booking deep enough for the payment projections, which reach through to the customer and
    /// the unit's project. Amounts of zero mean "skip this row".
    /// </summary>
    private static void Seed(AppDbContext context, decimal payment = 0, decimal expense = 0)
    {
        context.Projects.Add(new Project { Id = 1, ProjectName = "Floria", Location = "Lahore", CreatedById = 1 });
        context.Units.Add(new Unit { Id = 1, ProjectId = 1, UnitNumber = "A-1", UnitType = "Apartment", Price = 100_000m });
        context.Customers.Add(new Customer { Id = 1, FullName = "Buyer", Phone = "03001112222", Status = CustomerStatus.Active });
        context.Bookings.Add(new Booking
        {
            Id = 1, BookingReference = "BK-1", CustomerId = 1, UnitId = 1,
            Status = BookingStatus.AwaitingBookingAmount, AgreedSalePrice = 100_000m,
            BookingAmountRequired = 50_000m, BookingDate = DateTime.UtcNow
        });
        if (payment > 0)
            context.Payments.Add(new Payment { Id = 1, BookingId = 1, FinanceAccountId = 1, Amount = payment });
        if (expense > 0)
            context.Expenses.Add(new Expense { Id = 1, FinanceAccountId = 1, Amount = expense, Category = "Office" });
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static FinanceAccount Account(bool isActive = true) => new() { Id = 1, Name = "Main Cash", Type = FinanceAccountType.Cash, AccountHolderName = "Admin", OpeningBalance = 1000, IsActive = isActive };

    private sealed class NoopStorage : DAMS.Application.Interfaces.IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
