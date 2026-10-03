using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The opening balance is typed once, on the account form, at any time. These tests cover what the
/// form may and may not accept, the overview that tells the page when the openings do not match, and
/// the history row every change leaves behind.
/// </summary>
public sealed class FinanceAccountOpeningBalanceTests
{
    private static readonly DateTime GoLive = new(2026, 8, 1);

    [Fact]
    public async Task AnOpeningBalance_CanBeTypedOnANewAccount_AfterGoLive()
    {
        await using var context = Context();
        await GoLiveSeed.SetAsync(context, GoLive);
        await context.SaveChangesAsync();

        var created = await new FinanceAccountService(context).CreateAsync(Form("Meezan Bank", FinanceAccountType.Bank, 12_000_000m));

        Assert.Equal(12_000_000m, created.OpeningBalance);
    }

    [Fact]
    public async Task AnOpeningBalance_CanBeChanged_AfterGoLive_AndLeavesOneHistoryRowWithFromAndTo()
    {
        await using var context = Context();
        context.ActorUserId = 9;
        var service = new FinanceAccountService(context);
        await GoLiveSeed.SetAsync(context, GoLive);
        var created = await service.CreateAsync(Form("Meezan Bank", FinanceAccountType.Bank, 12_000_000m));

        var body = Update(created, 12_500_000m);
        var updated = await service.UpdateAsync(created.Id, body);

        Assert.Equal(12_500_000m, updated.OpeningBalance);
        // The creation left its own 0 -> 12,000,000 row; the change is the second.
        var row = Assert.Single((await Trail(context, created.Id)).Where(a => a.Action == "Updated"));
        Assert.Equal(9, row.ActorUserId);
        Assert.Contains("OpeningBalance", row.Changes);
        Assert.Contains("12000000.00", row.Changes);
        Assert.Contains("12500000.00", row.Changes);
        Assert.DoesNotContain("UpdatedAt", row.Changes);
    }

    [Fact]
    public async Task ANewAccountWithAnOpening_LeavesAHistoryRowFromZero_AndOneWithoutAnOpeningLeavesNone()
    {
        await using var context = Context();
        context.ActorUserId = 9;
        var service = new FinanceAccountService(context);

        var created = await service.CreateAsync(Form("Meezan Bank", FinanceAccountType.Bank, 12_000_000m));
        var spare = await service.CreateAsync(Form("Spare", FinanceAccountType.Cash, 0m));

        var row = Assert.Single(await Trail(context, created.Id));
        Assert.Equal("Created", row.Action);
        Assert.Equal(9, row.ActorUserId);
        Assert.Contains("\"from\":\"0.00\"", row.Changes);
        Assert.Contains("\"to\":\"12000000.00\"", row.Changes);
        Assert.Empty(await Trail(context, spare.Id));
    }

    [Fact]
    public async Task AnIdenticalResave_WritesNoHistoryRow()
    {
        await using var context = Context();
        var service = new FinanceAccountService(context);
        var created = await service.CreateAsync(Form("Meezan Bank", FinanceAccountType.Bank, 500m));

        await service.UpdateAsync(created.Id, Update(created, 500m));

        Assert.DoesNotContain(await Trail(context, created.Id), a => a.Action == "Updated");
    }

    [Theory]
    [InlineData(FinanceSystemAccountRole.CustomerDeposits)]
    [InlineData(FinanceSystemAccountRole.CustomerReceivables)]
    [InlineData(FinanceSystemAccountRole.CommissionPayable)]
    public async Task TheBookingDrivenSystemAccounts_RefuseANewTypedOpening_ButAcceptALegacyOneUnchanged(
        FinanceSystemAccountRole role)
    {
        await using var context = Context();
        var account = new FinanceAccount
        {
            Name = "Worked out", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Liability,
            IsActive = true, SystemRole = role, OpeningBalance = 0m, LedgerCode = "2101"
        };
        context.FinanceAccounts.Add(account);
        await context.SaveChangesAsync();
        var service = new FinanceAccountService(context);
        var current = await service.GetByIdAsync(account.Id);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(account.Id, Update(current, 1_000m)));
        Assert.Contains("worked out from bookings and commissions", error.Message);

        // A legacy figure written before this rule is saved again unchanged, and typing 0 is allowed.
        account.OpeningBalance = 750m;
        await context.SaveChangesAsync();
        current = await service.GetByIdAsync(account.Id);
        Assert.Equal(750m, (await service.UpdateAsync(account.Id, Update(current, 750m))).OpeningBalance);
        current = await service.GetByIdAsync(account.Id);
        Assert.Equal(0m, (await service.UpdateAsync(account.Id, Update(current, 0m))).OpeningBalance);
    }

    [Fact]
    public async Task AccountingForRefundsPayable_StillTakesATypedOpening()
    {
        await using var context = Context();
        var account = new FinanceAccount
        {
            Name = "Refunds payable", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Liability,
            IsActive = true, SystemRole = FinanceSystemAccountRole.CustomerRefundPayable, LedgerCode = "2104"
        };
        context.FinanceAccounts.Add(account);
        await context.SaveChangesAsync();
        var service = new FinanceAccountService(context);
        var current = await service.GetByIdAsync(account.Id);

        var updated = await service.UpdateAsync(account.Id, Update(current, 1_000m));

        Assert.Equal(1_000m, updated.OpeningBalance);
    }

    [Fact]
    public async Task ALoanAccount_CannotOpenBelowZero()
    {
        await using var context = Context();
        var service = new FinanceAccountService(context);
        var account = await service.CreateAsync(Form("Bank Alfalah loan", FinanceAccountType.Liability, 3_000_000m));
        context.Loans.Add(new Loan
        {
            Name = "Alfalah loan", LenderName = "Bank Alfalah", FinanceAccountId = account.Id, IsActive = true
        });
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(account.Id, Update(account, -1m)));

        Assert.Contains("negative opening balance", error.Message);
    }

    [Fact]
    public async Task AnAccountWithANonZeroOpening_CountsAsHavingHistory_SoItsTypeAndDeleteAreRefused()
    {
        await using var context = Context();
        var service = new FinanceAccountService(context);
        var account = await service.CreateAsync(Form("Cash in office", FinanceAccountType.Cash, 500_000m));

        var typeError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            var body = Update(account, 500_000m);
            body.Type = FinanceAccountType.Bank;
            return service.UpdateAsync(account.Id, body);
        });
        Assert.Contains("cannot change type", typeError.Message);

        var deleteError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteUnusedAsync(account.Id));
        Assert.Contains("cannot be deleted", deleteError.Message);

        // At zero, with nothing else behind it, the account is still free to change type or go.
        var zero = await service.CreateAsync(Form("Spare", FinanceAccountType.Cash, 0m));
        await service.DeleteUnusedAsync(zero.Id);
        Assert.False(await context.FinanceAccounts.AnyAsync(a => a.Id == zero.Id));
    }

    [Fact]
    public async Task TheOverview_SumsOpeningDebitsAndCredits_OnEachAccountsNormalSide_WithNegativeFigures()
    {
        await using var context = Context();
        var service = new FinanceAccountService(context);
        await GoLiveSeed.SetAsync(context, GoLive);
        await context.SaveChangesAsync();
        await service.CreateAsync(Form("Bank", FinanceAccountType.Bank, 1_000m));
        await service.CreateAsync(Form("Overdrawn bank", FinanceAccountType.Bank, -200m, holder: "Other"));
        await service.CreateAsync(Form("Loan", FinanceAccountType.Liability, 500m));
        await service.CreateAsync(Form("Negative liability", FinanceAccountType.Liability, -50m));
        await service.CreateAsync(Form("Capital", FinanceAccountType.Capital, 300m));

        var overview = await service.GetOverviewAsync();

        // Debits: bank 1,000 and the negative liability 50. Credits: overdraft 200, loan 500, capital 300.
        Assert.Equal(1_050m, overview.OpeningDebitTotal);
        Assert.Equal(1_000m, overview.OpeningCreditTotal);
        Assert.Equal(GoLive, overview.GoLiveDate);
    }

    [Fact]
    public async Task TheOverview_ListsEveryHolder_SortedAndDistinct_IncludingStaffFloatAndCapital()
    {
        await using var context = Context();
        var service = new FinanceAccountService(context);
        await service.CreateAsync(Form("Cash", FinanceAccountType.Cash, 0m, holder: "Riaz Junaid"));
        await service.CreateAsync(Form("Float", FinanceAccountType.StaffFloat, 0m, holder: "Adeel Satti"));
        await service.CreateAsync(Form("Capital", FinanceAccountType.Capital, 0m, holder: "riaz junaid"));
        await service.CreateAsync(Form("Furniture", FinanceAccountType.FixedAsset, 0m, holder: "Seven Ventures"));

        var overview = await service.GetOverviewAsync();

        Assert.Equal(["Adeel Satti", "Riaz Junaid", "Seven Ventures"], overview.HolderNames);
        Assert.Null(overview.GoLiveDate);
        Assert.Equal(0m, overview.OpeningDebitTotal);
        Assert.Equal(0m, overview.OpeningCreditTotal);
    }

    [Fact]
    public async Task ThePageWithOverview_CarriesTheNewOverviewFields_WhateverTheFilter()
    {
        await using var context = Context();
        var service = new FinanceAccountService(context);
        await service.CreateAsync(Form("Bank", FinanceAccountType.Bank, 100m, holder: "A"));
        await service.CreateAsync(Form("Capital", FinanceAccountType.Capital, 40m, holder: "B"));

        var page = await service.GetPageWithOverviewAsync("Capital", null, null, null, 0, 200);

        Assert.Single(page.Items);
        Assert.Equal(100m, page.Overview.OpeningDebitTotal);
        Assert.Equal(40m, page.Overview.OpeningCreditTotal);
        Assert.Equal(["A", "B"], page.Overview.HolderNames);
    }

    [Fact]
    public async Task AnOpeningBalance_BeyondTheSupportedRange_IsRefused()
    {
        await using var context = Context();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FinanceAccountService(context).CreateAsync(Form("Huge", FinanceAccountType.Bank, 1_000_000_000_000_000m)));

        Assert.Contains("outside the supported range", error.Message);
    }

    private static CreateFinanceAccountDto Form(string name, FinanceAccountType type, decimal opening, string holder = "Seven Ventures") => new()
    {
        Name = name, Type = type, AccountHolderName = holder, OpeningBalance = opening
    };

    private static UpdateFinanceAccountDto Update(FinanceAccountResponseDto account, decimal opening) => new()
    {
        Name = account.Name, Type = account.Type, AccountHolderName = account.AccountHolderName,
        OpeningBalance = opening, LedgerCode = account.LedgerCode, DisplayOrder = account.DisplayOrder,
        BankOrWalletName = account.BankOrWalletName, Description = account.Description,
        ConcurrencyToken = account.ConcurrencyToken
    };

    private static Task<List<FinanceRecordAudit>> Trail(AppDbContext context, int accountId) =>
        context.FinanceRecordAudits.AsNoTracking()
            .Where(a => a.RecordType == nameof(FinanceAccount) && a.RecordId == accountId).ToListAsync();

    private static AppDbContext Context()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Database.EnsureCreated();
        return context;
    }
}
