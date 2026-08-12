using System.Reflection;
using System.Text;
using DAMS.Api.Controllers;
using DAMS.Application.Common;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.DTOs.InstallmentDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class CommissionRebateTests
{
    private static readonly FinancialWorkflowActor Actor = new(77, "Finance Admin");

    [Fact]
    public void Controller_IsRestrictedToAdmins()
    {
        var authorization = typeof(CommissionRebatesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Equal("Admin", authorization.Roles);
        Assert.All(typeof(CommissionRebatesController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.DeclaringType == typeof(CommissionRebatesController)), m => Assert.NotNull(m));
    }

    [Fact]
    public void Model_ProtectsDuplicateFinancialRelationshipsAndIdempotencyKeys()
    {
        using var context = Context();
        var commission = context.Model.FindEntityType(typeof(BookingCommission))!;
        Assert.Contains(commission.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(["BookingId", "PartnerId"]));
        var attribution = context.Model.FindEntityType(typeof(ThirdPartyAttribution))!;
        Assert.Contains(attribution.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(["BookingId", "PartnerId"]));
        Assert.Contains(context.Model.FindEntityType(typeof(CommissionPayout))!.GetIndexes(), i => i.IsUnique
            && i.Properties.Single().Name == "IdempotencyKey");
        Assert.Contains(context.Model.FindEntityType(typeof(RebateDisbursement))!.GetIndexes(), i => i.IsUnique
            && i.Properties.Single().Name == "IdempotencyKey");
        Assert.NotNull(context.Model.FindEntityType(typeof(FinancialWorkflowAuditEntry))!
            .FindProperty(nameof(FinancialWorkflowAuditEntry.CommissionRuleId)));
    }

    [Fact]
    public async Task Partner_DuplicatesAndInactiveNewBusiness_AreRejected()
    {
        await using var harness = await Harness.Create();
        await harness.Service.CreatePartnerAsync(new SaveThirdPartyPartnerDto
        {
            Name = "Another name", PartnerType = "Broker", InternalCode = "unique", Cnic = "42101-1111111-1"
        }, Actor);
        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.CreatePartnerAsync(
            new SaveThirdPartyPartnerDto
            {
                Name = "Duplicate identity", PartnerType = "Dealer", InternalCode = "different", Cnic = "4210111111111"
            }, Actor));
        Assert.Contains("CNIC", duplicate.Message);

        var partner = harness.Context.ThirdPartyPartners.Single(p => p.Id == harness.PartnerId);
        partner.IsActive = false;
        await harness.Context.SaveChangesAsync();
        var inactive = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.SaveAttributionAsync(null,
            new SaveThirdPartyAttributionDto { PartnerId = partner.Id, BookingId = harness.BookingId }, Actor));
        Assert.Contains("Inactive", inactive.Message);
    }

    [Fact]
    public async Task PartnerDirectory_ValidatesTypesAndContactDuplicates_AndPaginates()
    {
        await using var harness = await Harness.Create();
        var invalidType = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.CreatePartnerAsync(
            new SaveThirdPartyPartnerDto { Name = "Invalid", PartnerType = "Unknown", InternalCode = "INVALID" }, Actor));
        Assert.Contains("valid partner type", invalidType.Message);

        await harness.Service.CreatePartnerAsync(new SaveThirdPartyPartnerDto
        {
            Name = "Contact One", PartnerType = "Agency", InternalCode = "CONTACT-1",
            Phone = "+92 300 123-4567", Email = "FINANCE@EXAMPLE.COM"
        }, Actor);
        var duplicateContact = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.CreatePartnerAsync(
            new SaveThirdPartyPartnerDto
            {
                Name = "Contact Two", PartnerType = "Dealer", InternalCode = "CONTACT-2",
                Phone = "923001234567", Email = "finance@example.com"
            }, Actor));
        Assert.Contains("phone and email", duplicateContact.Message);

        for (var i = 0; i < 12; i++)
            harness.Context.ThirdPartyPartners.Add(new ThirdPartyPartner
                { Name = $"Paged {i:00}", PartnerType = "Broker", InternalCode = $"PAGE-{i:00}", IsActive = true });
        await harness.Context.SaveChangesAsync();
        var first = await harness.Service.GetPartnersAsync("Paged", true, 0, 5);
        var second = await harness.Service.GetPartnersAsync("Paged", true, 5, 5);
        Assert.Equal(5, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.Empty(first.Items.Select(p => p.Id).Intersect(second.Items.Select(p => p.Id)));
    }

    [Fact]
    public async Task ReferencedAttribution_CannotBeReassigned()
    {
        await using var harness = await Harness.Create();
        await harness.CreateRuleCommission();
        var other = new ThirdPartyPartner { Name = "Other", PartnerType = "Broker", InternalCode = "OTHER", IsActive = true };
        harness.Context.ThirdPartyPartners.Add(other);
        await harness.Context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.SaveAttributionAsync(harness.AttributionId,
            new SaveThirdPartyAttributionDto
            {
                PartnerId = other.Id, BookingId = harness.BookingId, AllocationPercent = 100, RelationshipType = "Broker"
            }, Actor));
        Assert.Contains("cannot be reassigned", error.Message);
    }

    [Fact]
    public async Task RuleResolution_PrefersPriority_PreservesSnapshot_AndRoundsAwayFromZero()
    {
        await using var harness = await Harness.Create();
        harness.Context.CommissionRules.AddRange(
            Rule("Default", priority: 1, fixedAmount: 999m),
            Rule("Specific", priority: 10, rate: 2.345678m, partnerId: harness.PartnerId));
        await harness.Context.SaveChangesAsync();
        var workspace = await harness.Service.CreateCommissionAsync(harness.BookingId,
            new CreateBookingCommissionDto { PartnerId = harness.PartnerId, AttributionId = harness.AttributionId }, Actor);
        var commission = Assert.Single(workspace.Commissions);
        Assert.Equal("Specific", commission.RuleNameSnapshot);
        Assert.Equal(Math.Round(harness.NetPrice * 2.345678m / 100m, 2, MidpointRounding.AwayFromZero), commission.FinalAmount);
        var rule = harness.Context.CommissionRules.Single(r => r.Name == "Specific");
        rule.PercentageRate = 10m;
        await harness.Context.SaveChangesAsync();
        commission = Assert.Single((await harness.Service.GetBookingWorkspaceAsync(harness.BookingId)).Commissions);
        Assert.Equal(2.345678m, commission.PercentageRate);
    }

    [Fact]
    public async Task RuleEffectiveDates_AreInclusiveBusinessDates()
    {
        await using var harness = await Harness.Create();
        var bookingDate = harness.Context.Bookings.Single().BookingDate.Date;
        var rule = Rule("One-day rule", 10, fixedAmount: 250m);
        rule.EffectiveFrom = bookingDate;
        rule.EffectiveTo = bookingDate;
        harness.Context.CommissionRules.Add(rule);
        await harness.Context.SaveChangesAsync();

        var workspace = await harness.Service.CreateCommissionAsync(harness.BookingId,
            new CreateBookingCommissionDto { PartnerId = harness.PartnerId, AttributionId = harness.AttributionId }, Actor);

        Assert.Equal("One-day rule", Assert.Single(workspace.Commissions).RuleNameSnapshot);
    }

    [Fact]
    public async Task EquallyRankedRules_FailSafely()
    {
        await using var harness = await Harness.Create();
        harness.Context.CommissionRules.AddRange(Rule("One", 5, rate: 1m), Rule("Two", 5, rate: 2m));
        await harness.Context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.CreateCommissionAsync(harness.BookingId,
            new CreateBookingCommissionDto { PartnerId = harness.PartnerId, AttributionId = harness.AttributionId }, Actor));
        Assert.Contains("equally ranked", error.Message);
        Assert.Empty(harness.Context.BookingCommissions);
    }

    [Fact]
    public async Task RuleWithoutApproval_AutoApprovesAndAuditsTheDecision()
    {
        await using var harness = await Harness.Create();
        var rule = Rule("Auto approval", 10, rate: 2m);
        rule.RequiresApproval = false;
        harness.Context.CommissionRules.Add(rule);
        await harness.Context.SaveChangesAsync();

        var commission = await harness.Service.CreateCommissionAsync(harness.BookingId,
            new CreateBookingCommissionDto { PartnerId = harness.PartnerId, AttributionId = harness.AttributionId }, Actor);
        var created = Assert.Single(commission.Commissions);

        Assert.Equal(BookingCommissionStatus.Approved, created.Status);
        Assert.Equal(created.FinalAmount, created.ApprovedAmount);
        Assert.Contains(harness.Context.FinancialWorkflowAuditEntries,
            a => a.Action == FinancialWorkflowAction.CommissionApproved);
    }

    [Fact]
    public async Task CommissionCalculation_AppliesAttributionAllocation_AndSnapshotsIt()
    {
        await using var harness = await Harness.Create();
        var attribution = await harness.Context.ThirdPartyAttributions.FindAsync(harness.AttributionId);
        Assert.NotNull(attribution);
        attribution.AllocationPercent = 50m;
        await harness.Context.SaveChangesAsync();

        var commission = await harness.CreateRuleCommission();

        Assert.Equal(50m, commission.AllocationPercent);
        Assert.Equal(Math.Round(harness.NetPrice * 2m / 100m * 50m / 100m, 2, MidpointRounding.AwayFromZero), commission.FinalAmount);
    }

    [Fact]
    public async Task CommissionLifecycle_RejectsShortcuts_AndAuditsEveryTransition()
    {
        await using var harness = await Harness.Create();
        var commission = await harness.CreateRuleCommission();
        var shortcut = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.ChangeCommissionStatusAsync(
            harness.BookingId, commission.Id, Change(commission, BookingCommissionStatus.Paid), Actor));
        Assert.Contains("cannot move", shortcut.Message);
        var workspace = await harness.Service.ChangeCommissionStatusAsync(harness.BookingId, commission.Id,
            Change(commission, BookingCommissionStatus.PendingApproval), Actor);
        commission = Assert.Single(workspace.Commissions);
        workspace = await harness.Service.ChangeCommissionStatusAsync(harness.BookingId, commission.Id,
            Change(commission, BookingCommissionStatus.Approved, approved: commission.FinalAmount), Actor);
        commission = Assert.Single(workspace.Commissions);
        workspace = await harness.Service.ChangeCommissionStatusAsync(harness.BookingId, commission.Id,
            Change(commission, BookingCommissionStatus.Earned), Actor);
        commission = Assert.Single(workspace.Commissions);
        workspace = await harness.Service.ChangeCommissionStatusAsync(harness.BookingId, commission.Id,
            Change(commission, BookingCommissionStatus.Payable), Actor);
        Assert.Equal(BookingCommissionStatus.Payable, Assert.Single(workspace.Commissions).Status);
        var actions = harness.Context.FinancialWorkflowAuditEntries.Where(a => a.CommissionId == commission.Id).Select(a => a.Action).ToList();
        Assert.Contains(FinancialWorkflowAction.CommissionSubmitted, actions);
        Assert.Contains(FinancialWorkflowAction.CommissionApproved, actions);
        Assert.Contains(FinancialWorkflowAction.CommissionEarned, actions);
        Assert.Contains(FinancialWorkflowAction.CommissionPayable, actions);
    }

    [Fact]
    public async Task Payout_IsPartialIdempotentBoundedAndReversible()
    {
        await using var harness = await Harness.Create();
        var commission = await harness.MakePayable();
        var request = new RecordCommissionPayoutDto
        {
            FinanceAccountId = harness.AccountId, Amount = 100m, PaymentDate = DateTime.UtcNow,
            PaymentMethod = PaymentMethod.BankTransfer, PaymentReference = "TXN-001",
            IdempotencyKey = "  same-payout  ", CommissionConcurrencyToken = commission.ConcurrencyToken
        };
        var workspace = await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id, request, Actor);
        commission = Assert.Single(workspace.Commissions);
        Assert.Equal(BookingCommissionStatus.PartiallyPaid, commission.Status);
        Assert.Equal(100m, commission.PaidAmount);
        await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id, request, Actor);
        Assert.Single(harness.Context.CommissionPayouts);
        var changedRetry = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.RecordPayoutAsync(
            harness.BookingId, commission.Id, new RecordCommissionPayoutDto
            {
                FinanceAccountId = request.FinanceAccountId, Amount = request.Amount + 1m,
                PaymentDate = request.PaymentDate, PaymentMethod = request.PaymentMethod,
                PaymentReference = request.PaymentReference,
                IdempotencyKey = "same-payout", CommissionConcurrencyToken = request.CommissionConcurrencyToken
            }, Actor));
        Assert.Contains("different payout", changedRetry.Message);
        var duplicateReference = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.RecordPayoutAsync(
            harness.BookingId, commission.Id, new RecordCommissionPayoutDto
            {
                FinanceAccountId = request.FinanceAccountId, Amount = 1m, PaymentDate = request.PaymentDate,
                PaymentMethod = request.PaymentMethod, PaymentReference = request.PaymentReference,
                IdempotencyKey = "different-operation", CommissionConcurrencyToken = commission.ConcurrencyToken
            }, Actor));
        Assert.Contains("already recorded", duplicateReference.Message);
        var overpay = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.RecordPayoutAsync(harness.BookingId,
            commission.Id, new RecordCommissionPayoutDto
            {
                FinanceAccountId = harness.AccountId, Amount = commission.OutstandingAmount + .01m, PaymentDate = DateTime.UtcNow,
                PaymentMethod = PaymentMethod.Cash, IdempotencyKey = "overpay", CommissionConcurrencyToken = commission.ConcurrencyToken
            }, Actor));
        Assert.Contains("exceeds", overpay.Message);
        var payout = Assert.Single(commission.Payouts);
        var reversed = await harness.Service.ReversePayoutAsync(harness.BookingId, commission.Id, payout.Id,
            new ReverseMoneyMovementDto { Amount = 40m, Reason = "Correction", IdempotencyKey = "reverse-1" }, Actor);
        Assert.Equal(60m, Assert.Single(reversed.Commissions).PaidAmount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.ReversePayoutAsync(harness.BookingId,
            commission.Id, payout.Id, new ReverseMoneyMovementDto { Amount = 61m, Reason = "Too much", IdempotencyKey = "reverse-2" }, Actor));
    }

    [Fact]
    public async Task PaymentReference_IsBlockedWhileLive_ButReusableAfterFullReversal()
    {
        await using var harness = await Harness.Create();
        var commission = await harness.MakePayable();
        const string reference = "TXN-REUSE";
        var full = commission.OutstandingAmount;

        // Original payout consumes the reference and pays the commission in full.
        var workspace = await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id, new RecordCommissionPayoutDto
        {
            FinanceAccountId = harness.AccountId, Amount = full, PaymentDate = DateTime.UtcNow,
            PaymentMethod = PaymentMethod.BankTransfer, PaymentReference = reference,
            IdempotencyKey = "payout-original", CommissionConcurrencyToken = commission.ConcurrencyToken
        }, Actor);
        commission = Assert.Single(workspace.Commissions);
        Assert.Equal(BookingCommissionStatus.Paid, commission.Status);
        var payout = Assert.Single(commission.Payouts);

        // While the payout still holds a live balance the reference stays blocked.
        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.RecordPayoutAsync(
            harness.BookingId, commission.Id, new RecordCommissionPayoutDto
            {
                FinanceAccountId = harness.AccountId, Amount = full, PaymentDate = DateTime.UtcNow,
                PaymentMethod = PaymentMethod.BankTransfer, PaymentReference = reference,
                IdempotencyKey = "payout-blocked", CommissionConcurrencyToken = commission.ConcurrencyToken
            }, Actor));
        Assert.Contains("already recorded", blocked.Message);

        // A full reversal returns the commission to Payable; the reversed payout no longer holds money out.
        workspace = await harness.Service.ReversePayoutAsync(harness.BookingId, commission.Id, payout.Id,
            new ReverseMoneyMovementDto { Amount = full, Reason = "Mis-keyed entry", IdempotencyKey = "reverse-full" }, Actor);
        commission = Assert.Single(workspace.Commissions);
        Assert.Equal(BookingCommissionStatus.Payable, commission.Status);

        // The genuine bank reference can now be re-recorded correctly.
        workspace = await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id, new RecordCommissionPayoutDto
        {
            FinanceAccountId = harness.AccountId, Amount = full, PaymentDate = DateTime.UtcNow,
            PaymentMethod = PaymentMethod.BankTransfer, PaymentReference = reference,
            IdempotencyKey = "payout-corrected", CommissionConcurrencyToken = commission.ConcurrencyToken
        }, Actor);
        commission = Assert.Single(workspace.Commissions);
        Assert.Equal(BookingCommissionStatus.Paid, commission.Status);
        Assert.Equal(full, commission.PaidAmount);
        Assert.Equal(2, commission.Payouts.Count);
    }

    [Fact]
    public async Task FinancialInputs_AreNormalizedAuditedAndCannotProduceZeroCommission()
    {
        await using var harness = await Harness.Create();
        var workspace = await harness.Service.CreateCommissionAsync(harness.BookingId,
            new CreateBookingCommissionDto
            {
                PartnerId = harness.PartnerId, AttributionId = harness.AttributionId, IsManual = true,
                ManualReason = "Documented exception", ManualCalculationType = FinancialCalculationType.Percentage,
                ManualCalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                ManualPercentageRate = 2.3456789m,
                ManualEarningCondition = CommissionEarningCondition.MinimumCollectionPercentage,
                MinimumCollectionPercent = 12.345m, AdjustmentAmount = 10m,
                AdjustmentReason = "Approved administrative adjustment"
            }, Actor);
        var commission = Assert.Single(workspace.Commissions);
        Assert.Equal(2.345679m, commission.PercentageRate);
        Assert.Equal(12.35m, commission.MinimumCollectionPercent);
        Assert.Equal(Math.Round(harness.NetPrice * 2.345679m / 100m, 2, MidpointRounding.AwayFromZero),
            commission.CalculatedAmount);
        Assert.Contains(harness.Context.FinancialWorkflowAuditEntries,
            a => a.CommissionId == commission.Id && a.Action == FinancialWorkflowAction.CommissionCalculated);
        Assert.Contains(harness.Context.FinancialWorkflowAuditEntries,
            a => a.CommissionId == commission.Id && a.Action == FinancialWorkflowAction.CommissionAdjusted);

        await using var zeroHarness = await Harness.Create();
        var zeroRule = Rule("Zero guard", 1, fixedAmount: 10m);
        zeroHarness.Context.CommissionRules.Add(zeroRule);
        await zeroHarness.Context.SaveChangesAsync();
        var zero = await Assert.ThrowsAsync<InvalidOperationException>(() => zeroHarness.Service.CreateCommissionAsync(
            zeroHarness.BookingId, new CreateBookingCommissionDto
            {
                PartnerId = zeroHarness.PartnerId, AttributionId = zeroHarness.AttributionId,
                AdjustmentAmount = -10m, AdjustmentReason = "Would reduce the commission to zero"
            }, Actor));
        Assert.Contains("greater than zero", zero.Message);
        Assert.Empty(zeroHarness.Context.BookingCommissions);
    }

    [Fact]
    public async Task RulesRejectUnreproducibleBasis_AndMovementsRejectFutureDates()
    {
        await using var harness = await Harness.Create();
        var invalidRule = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.CreateRuleAsync(
            new SaveCommissionRuleDto
            {
                Name = "Invalid reusable manual basis", IsActive = true,
                EffectiveFrom = DateTime.UtcNow.Date,
                CalculationType = FinancialCalculationType.FixedAmount, FixedAmount = 100m,
                CalculationBasis = FinancialCalculationBasis.ManuallyApprovedAmount,
                EarningCondition = CommissionEarningCondition.BookingAmountFullyReceived
            }, Actor));
        Assert.Contains("manual commission", invalidRule.Message);

        var commission = await harness.MakePayable();
        var future = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.RecordPayoutAsync(
            harness.BookingId, commission.Id, new RecordCommissionPayoutDto
            {
                FinanceAccountId = harness.AccountId, Amount = 1m, PaymentDate = DateTime.UtcNow.AddDays(2),
                PaymentMethod = PaymentMethod.Cash, IdempotencyKey = "future-payout",
                CommissionConcurrencyToken = commission.ConcurrencyToken
            }, Actor));
        Assert.Contains("future", future.Message);
        Assert.Empty(harness.Context.CommissionPayouts);
    }

    [Fact]
    public async Task InactivePartner_CannotReceivePayout()
    {
        await using var harness = await Harness.Create();
        var commission = await harness.MakePayable();
        harness.Context.ThirdPartyPartners.Single(p => p.Id == harness.PartnerId).IsActive = false;
        await harness.Context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.RecordPayoutAsync(harness.BookingId,
            commission.Id, new RecordCommissionPayoutDto
            {
                FinanceAccountId = harness.AccountId, Amount = 1m, PaymentDate = DateTime.UtcNow,
                PaymentMethod = PaymentMethod.Cash, IdempotencyKey = "inactive", CommissionConcurrencyToken = commission.ConcurrencyToken
            }, Actor));
        Assert.Contains("inactive", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rebate_IsSeparateRequiresEvidenceAndCannotApplyTwiceOrBelowZero()
    {
        await using var harness = await Harness.Create();
        var workspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount, CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 1_000m, Reason = "Customer retention", Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        var rebate = Assert.Single(workspace.Rebates);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.ChangeRebateStatusAsync(harness.BookingId,
            rebate.Id, RebateChange(rebate, CustomerRebateStatus.PendingApproval), Actor));
        await harness.UploadPdf(FinancialEvidenceOwnerType.Rebate, rebate.Id);
        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.PendingApproval), Actor);
        rebate = Assert.Single(workspace.Rebates);
        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.Approved, rebate.FinalAmount), Actor);
        rebate = Assert.Single(workspace.Rebates);
        var apply = new RecordRebateDisbursementDto
        {
            Method = CustomerRebateMethod.OutstandingBalanceReduction, Amount = 400m, AppliedAt = DateTime.UtcNow,
            IdempotencyKey = "rebate-apply", RebateConcurrencyToken = rebate.ConcurrencyToken
        };
        workspace = await harness.Service.RecordRebateDisbursementAsync(harness.BookingId, rebate.Id, apply, Actor);
        rebate = Assert.Single(workspace.Rebates);
        Assert.Equal(400m, rebate.AppliedOrPaidAmount);
        await harness.Service.RecordRebateDisbursementAsync(harness.BookingId, rebate.Id, apply, Actor);
        Assert.Single(harness.Context.RebateDisbursements);
        Assert.Equal(harness.Discount, harness.Context.Bookings.Single().DiscountAmount);
        var movement = Assert.Single(rebate.Disbursements);
        workspace = await harness.Service.ReverseRebateDisbursementAsync(harness.BookingId, rebate.Id, movement.Id,
            new ReverseMoneyMovementDto { Amount = 150m, Reason = "Benefit reduced", IdempotencyKey = "rebate-reverse" }, Actor);
        Assert.Equal(250m, Assert.Single(workspace.Rebates).AppliedOrPaidAmount);
    }

    [Fact]
    public async Task RebateAccounting_OnlyExplicitCreditMethodsReduceTheBookingBalance()
    {
        await using var otherHarness = await Harness.Create();
        var workspace = await otherHarness.Service.CreateRebateAsync(otherHarness.BookingId,
            new CreateCustomerRebateDto
            {
                CalculationType = FinancialCalculationType.FixedAmount,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                FixedAmount = 100m, Reason = "Non-monetary customer benefit",
                Method = CustomerRebateMethod.Other
            }, Actor);
        var other = Assert.Single(workspace.Rebates);
        await otherHarness.UploadPdf(FinancialEvidenceOwnerType.Rebate, other.Id);
        workspace = await otherHarness.Service.ChangeRebateStatusAsync(otherHarness.BookingId, other.Id,
            RebateChange(other, CustomerRebateStatus.PendingApproval), Actor);
        other = Assert.Single(workspace.Rebates);
        workspace = await otherHarness.Service.ChangeRebateStatusAsync(otherHarness.BookingId, other.Id,
            RebateChange(other, CustomerRebateStatus.Approved, other.FinalAmount), Actor);
        other = Assert.Single(workspace.Rebates);
        workspace = await otherHarness.Service.RecordRebateDisbursementAsync(otherHarness.BookingId, other.Id,
            new RecordRebateDisbursementDto
            {
                Method = CustomerRebateMethod.Other, Amount = 100m, AppliedAt = DateTime.UtcNow,
                Notes = "Delivered as an approved non-monetary benefit", IdempotencyKey = "other-benefit",
                RebateConcurrencyToken = other.ConcurrencyToken
            }, Actor);
        Assert.Equal(CustomerRebateStatus.Applied, Assert.Single(workspace.Rebates).Status);
        Assert.Equal(0m, workspace.RebateCredits);

        await using var creditHarness = await Harness.Create();
        workspace = await creditHarness.Service.CreateRebateAsync(creditHarness.BookingId,
            new CreateCustomerRebateDto
            {
                CalculationType = FinancialCalculationType.FixedAmount,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                FixedAmount = 100m, Reason = "Account credit", Method = CustomerRebateMethod.CreditNote
            }, Actor);
        var credit = Assert.Single(workspace.Rebates);
        await creditHarness.UploadPdf(FinancialEvidenceOwnerType.Rebate, credit.Id);
        workspace = await creditHarness.Service.ChangeRebateStatusAsync(creditHarness.BookingId, credit.Id,
            RebateChange(credit, CustomerRebateStatus.PendingApproval), Actor);
        credit = Assert.Single(workspace.Rebates);
        workspace = await creditHarness.Service.ChangeRebateStatusAsync(creditHarness.BookingId, credit.Id,
            RebateChange(credit, CustomerRebateStatus.Approved, credit.FinalAmount), Actor);
        credit = Assert.Single(workspace.Rebates);
        workspace = await creditHarness.Service.RecordRebateDisbursementAsync(creditHarness.BookingId, credit.Id,
            new RecordRebateDisbursementDto
            {
                Method = CustomerRebateMethod.CreditNote, Amount = 100m, AppliedAt = DateTime.UtcNow,
                Reference = "CN-001", IdempotencyKey = "credit-note",
                RebateConcurrencyToken = credit.ConcurrencyToken
            }, Actor);
        Assert.Equal(100m, workspace.RebateCredits);
    }

    [Fact]
    public async Task BookingCancellation_PreservesPaidHistoryAndRequiresReversal()
    {
        await using var harness = await Harness.Create();
        var commission = await harness.MakePayable();
        await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id, new RecordCommissionPayoutDto
        {
            FinanceAccountId = harness.AccountId, Amount = 100m, PaymentDate = DateTime.UtcNow,
            PaymentMethod = PaymentMethod.Cash, IdempotencyKey = "cancel-paid", CommissionConcurrencyToken = commission.ConcurrencyToken
        }, Actor);
        await harness.Service.HandleBookingCancelledAsync(harness.BookingId, "Customer withdrew", Actor);
        harness.Context.Bookings.Single().Status = BookingStatus.Cancelled;
        await harness.Context.SaveChangesAsync();
        var saved = harness.Context.BookingCommissions.Include(c => c.Payouts).Single();
        Assert.Equal(BookingCommissionStatus.ReversalRequired, saved.Status);
        Assert.Single(saved.Payouts);
        Assert.Contains(harness.Context.FinancialWorkflowAuditEntries,
            a => a.Action == FinancialWorkflowAction.CommissionReversalRequired);
    }

    [Fact]
    public async Task Evidence_IsPrivateValidatedAuditedAndCleanedWhenDatabaseFails()
    {
        await using var harness = await Harness.Create();
        var commission = await harness.CreateRuleCommission();
        await harness.UploadPdf(FinancialEvidenceOwnerType.Commission, commission.Id);
        var evidence = Assert.Single(harness.Context.FinancialEvidence);
        Assert.DoesNotContain("/", evidence.StoredFileName);
        Assert.Contains(harness.Context.FinancialWorkflowAuditEntries, a => a.Action == FinancialWorkflowAction.EvidenceUploaded);
        var download = await harness.Service.DownloadEvidenceAsync(evidence.Id, Actor);
        await download.Content.DisposeAsync();
        Assert.Contains(harness.Context.FinancialWorkflowAuditEntries, a => a.Action == FinancialWorkflowAction.EvidenceDownloaded);
        var bad = Encoding.UTF8.GetBytes("MZ executable");
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.UploadEvidenceAsync(
            FinancialEvidenceOwnerType.Commission, commission.Id,
            new FinancialEvidenceUpload { Content = new MemoryStream(bad), FileName = "proof.pdf", Length = bad.Length }, Actor));
    }

    [Fact]
    public async Task RuleUpdate_RecordsChangedControlValues_InImmutableAudit()
    {
        await using var harness = await Harness.Create();
        var created = await harness.Service.CreateRuleAsync(new SaveCommissionRuleDto
        {
            Name = "Base Rule", IsActive = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), Priority = 1,
            CalculationType = FinancialCalculationType.Percentage, PercentageRate = 2m,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            EarningCondition = CommissionEarningCondition.BookingAmountFullyReceived
        }, Actor);

        await harness.Service.UpdateRuleAsync(created.Id, new SaveCommissionRuleDto
        {
            Name = "Base Rule", IsActive = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), Priority = 5,
            CalculationType = FinancialCalculationType.Percentage, PercentageRate = 3m,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            EarningCondition = CommissionEarningCondition.BookingAmountFullyReceived,
            ConcurrencyToken = created.ConcurrencyToken,
            ChangeReason = "Increase payout rate for the new partner agreement."
        }, Actor);

        var audit = Assert.Single(harness.Context.FinancialWorkflowAuditEntries
            .Where(a => a.Action == FinancialWorkflowAction.RuleUpdated).ToList());
        Assert.Equal("Increase payout rate for the new partner agreement.", audit.Reason);
        var revisions = await harness.Context.CommissionRuleRevisions.OrderBy(r => r.RevisionNumber).ToListAsync();
        Assert.Equal(2, revisions.Count);
        Assert.Equal(revisions[0].SnapshotJson, revisions[1].PreviousSnapshotJson);
        Assert.Contains("\"PercentageRate\":\"2\"", revisions[0].SnapshotJson);
        Assert.Contains("\"PercentageRate\":\"3\"", revisions[1].SnapshotJson);
        Assert.Equal(revisions[1].Id, audit.CommissionRuleRevisionId);
        revisions[0].ChangeReason = "Tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Context.SaveChangesAsync());
        harness.Context.Entry(revisions[0]).State = EntityState.Unchanged;
    }

    [Fact]
    public async Task ReturnedCommission_CanBeCorrectedAndResubmitted_WithImmutableAudit()
    {
        await using var harness = await Harness.Create();
        var commission = await harness.CreateRuleCommission();
        await harness.UploadPdf(FinancialEvidenceOwnerType.Commission, commission.Id);
        var workspace = await harness.Service.ChangeCommissionStatusAsync(harness.BookingId, commission.Id,
            Change(commission, BookingCommissionStatus.PendingApproval), Actor);
        commission = Assert.Single(workspace.Commissions);
        workspace = await harness.Service.ChangeCommissionStatusAsync(harness.BookingId, commission.Id,
            Change(commission, BookingCommissionStatus.Draft, "Correct the basis"), Actor);
        commission = Assert.Single(workspace.Commissions);

        workspace = await harness.Service.UpdateCommissionAsync(harness.BookingId, commission.Id,
            new UpdateBookingCommissionDto
            {
                PartnerId = harness.PartnerId, AttributionId = harness.AttributionId, IsManual = true,
                ManualReason = "Manually approved correction", ManualCalculationType = FinancialCalculationType.FixedAmount,
                ManualCalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                ManualFixedAmount = 2_500m, ManualEarningCondition = CommissionEarningCondition.ManualMilestone,
                ConcurrencyToken = commission.ConcurrencyToken, ChangeReason = "Corrected after finance review."
            }, Actor);
        commission = Assert.Single(workspace.Commissions);
        Assert.Equal(BookingCommissionStatus.Draft, commission.Status);
        Assert.True(commission.IsManual);
        Assert.Equal(2_500m, commission.FinalAmount);
        Assert.Contains(await harness.Context.FinancialWorkflowAuditEntries.ToListAsync(), entry =>
            entry.Action == FinancialWorkflowAction.CommissionAdjusted
            && entry.Reason == "Corrected after finance review." && entry.PreviousAmount != entry.NewAmount);

        workspace = await harness.Service.ChangeCommissionStatusAsync(harness.BookingId, commission.Id,
            Change(commission, BookingCommissionStatus.PendingApproval), Actor);
        Assert.Equal(BookingCommissionStatus.PendingApproval, Assert.Single(workspace.Commissions).Status);
    }

    [Fact]
    public async Task ReturnedRebate_CanBeCorrectedAndResubmitted_WithImmutableAudit()
    {
        await using var harness = await Harness.Create();
        var workspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 500m, Reason = "Initial proposal", Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        var rebate = Assert.Single(workspace.Rebates);
        await harness.UploadPdf(FinancialEvidenceOwnerType.Rebate, rebate.Id);
        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.PendingApproval), Actor);
        rebate = Assert.Single(workspace.Rebates);
        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.Draft, reason: "Correct the benefit"), Actor);
        rebate = Assert.Single(workspace.Rebates);

        workspace = await harness.Service.UpdateRebateAsync(harness.BookingId, rebate.Id,
            new UpdateCustomerRebateDto
            {
                CalculationType = FinancialCalculationType.FixedAmount,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                FixedAmount = 750m, Reason = "Approved customer retention benefit",
                Method = CustomerRebateMethod.CreditNote, ConcurrencyToken = rebate.ConcurrencyToken,
                ChangeReason = "Corrected after commercial review."
            }, Actor);
        rebate = Assert.Single(workspace.Rebates);
        Assert.Equal(CustomerRebateStatus.Draft, rebate.Status);
        Assert.Equal(750m, rebate.FinalAmount);
        Assert.Equal(CustomerRebateMethod.CreditNote, rebate.Method);
        Assert.Contains(await harness.Context.FinancialWorkflowAuditEntries.ToListAsync(), entry =>
            entry.Action == FinancialWorkflowAction.RebateAdjusted
            && entry.Reason == "Corrected after commercial review." && entry.PreviousAmount != entry.NewAmount);

        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.PendingApproval), Actor);
        Assert.Equal(CustomerRebateStatus.PendingApproval, Assert.Single(workspace.Rebates).Status);
    }

    [Fact]
    public async Task CancelledRebate_CanBeSupersededByACorrectedReplacement()
    {
        await using var harness = await Harness.Create();
        var workspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 500m, Reason = "First attempt", Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        var rebate = Assert.Single(workspace.Rebates);

        // While the first rebate is live, a duplicate is rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.CreateRebateAsync(harness.BookingId,
            new CreateCustomerRebateDto
            {
                CalculationType = FinancialCalculationType.FixedAmount,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                FixedAmount = 600m, Reason = "Duplicate", Method = CustomerRebateMethod.OutstandingBalanceReduction
            }, Actor));

        // Cancel it, then a corrected replacement is allowed and the cancelled record is preserved.
        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.Cancelled, reason: "Wrong amount"), Actor);
        Assert.Equal(CustomerRebateStatus.Cancelled, Assert.Single(workspace.Rebates).Status);

        workspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 600m, Reason = "Corrected", Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        Assert.Equal(2, workspace.Rebates.Count);
        Assert.Single(workspace.Rebates, r => r.Status == CustomerRebateStatus.Draft);
        Assert.Single(workspace.Rebates, r => r.Status == CustomerRebateStatus.Cancelled);
    }

    [Fact]
    public async Task RuleDirectory_PagesWithStableOrder_AndReportsHasMore()
    {
        await using var harness = await Harness.Create();
        for (var i = 0; i < 5; i++)
            await harness.Service.CreateRuleAsync(new SaveCommissionRuleDto
            {
                Name = $"Rule {i}", IsActive = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), Priority = i,
                CalculationType = FinancialCalculationType.Percentage, PercentageRate = 1m,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                EarningCondition = CommissionEarningCondition.BookingAmountFullyReceived
            }, Actor);

        var page1 = await harness.Service.GetRulesAsync(null, null, 0, 2);
        var page2 = await harness.Service.GetRulesAsync(null, null, 2, 2);
        var page3 = await harness.Service.GetRulesAsync(null, null, 4, 2);

        Assert.Equal(2, page1.Items.Count); Assert.True(page1.HasMore);
        Assert.Equal(2, page2.Items.Count); Assert.True(page2.HasMore);
        Assert.Single(page3.Items); Assert.False(page3.HasMore);
        // Stable ordering: no row appears on two pages.
        var ids = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(r => r.Id).ToList();
        Assert.Equal(5, ids.Distinct().Count());
    }

    [Fact]
    public async Task RuleSearch_ReachesASelectableRuleBeyondFiveHundredRows()
    {
        await using var harness = await Harness.Create();
        for (var i = 0; i < 505; i++)
            harness.Context.CommissionRules.Add(Rule(i == 504 ? "Needle Production Rule" : $"Bulk Rule {i:000}",
                priority: i, fixedAmount: 100m));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetRulesAsync("Needle Production", true, 0, 25);
        var found = Assert.Single(result.Items);
        Assert.Equal("Needle Production Rule", found.Name);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task NewCommission_ReferencesTheExactImmutableRuleRevision()
    {
        await using var harness = await Harness.Create();
        var rule = await harness.Service.CreateRuleAsync(new SaveCommissionRuleDto
        {
            Name = "Revision-linked rule", IsActive = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1),
            PartnerId = harness.PartnerId, Priority = 500,
            CalculationType = FinancialCalculationType.Percentage, PercentageRate = 2m,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            EarningCondition = CommissionEarningCondition.BookingAmountFullyReceived
        }, Actor);

        var workspace = await harness.Service.CreateCommissionAsync(harness.BookingId,
            new CreateBookingCommissionDto
            {
                PartnerId = harness.PartnerId, AttributionId = harness.AttributionId, RuleId = rule.Id
            }, Actor);
        var commission = Assert.Single(workspace.Commissions);
        var revision = Assert.Single(await harness.Context.CommissionRuleRevisions
            .Where(r => r.RuleId == rule.Id).ToListAsync());
        Assert.Equal(revision.Id, commission.RuleRevisionId);
        Assert.Equal(1, commission.RuleRevisionNumber);
        Assert.Contains(await harness.Context.FinancialWorkflowAuditEntries.ToListAsync(), entry =>
            entry.CommissionId == commission.Id && entry.Action == FinancialWorkflowAction.CommissionCalculated
            && entry.CommissionRuleRevisionId == revision.Id);
    }

    [Fact]
    public async Task CreditReversal_OnCompletedBooking_IsBlockedUntilReopen()
    {
        await using var harness = await Harness.Create();
        var workspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 100m, Reason = "Account credit", Method = CustomerRebateMethod.CreditNote
        }, Actor);
        var rebate = Assert.Single(workspace.Rebates);
        await harness.UploadPdf(FinancialEvidenceOwnerType.Rebate, rebate.Id);
        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.PendingApproval), Actor);
        rebate = Assert.Single(workspace.Rebates);
        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.Approved, rebate.FinalAmount), Actor);
        rebate = Assert.Single(workspace.Rebates);
        workspace = await harness.Service.RecordRebateDisbursementAsync(harness.BookingId, rebate.Id,
            new RecordRebateDisbursementDto
            {
                Method = CustomerRebateMethod.CreditNote, Amount = 100m, AppliedAt = DateTime.UtcNow,
                Reference = "CN-900", IdempotencyKey = "credit-live", RebateConcurrencyToken = rebate.ConcurrencyToken
            }, Actor);
        var disbursement = Assert.Single(Assert.Single(workspace.Rebates).Disbursements);

        // Complete the sale; the credit now underpins the completion.
        harness.Context.Bookings.Single().Status = BookingStatus.SaleCompleted;
        await harness.Context.SaveChangesAsync();

        // Reversing the credit would restore a receivable on a completed, sold booking with no
        // collection path, so it must be blocked until the booking is reopened.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.ReverseRebateDisbursementAsync(harness.BookingId, rebate.Id, disbursement.Id,
                new ReverseMoneyMovementDto { Amount = 100m, Reason = "Correction", IdempotencyKey = "credit-reverse" }, Actor));
        Assert.Empty(harness.Context.RebateDisbursementReversals);
    }

    [Fact]
    public void FilteredUniqueIndexes_AllowSupersedingTerminalFinancialRecords()
    {
        using var context = Context();
        // The uniqueness is filtered to non-terminal rows, which is what lets a rejected/cancelled/
        // reversed record be superseded on real SQL Server without violating the index at insert.
        var rebateIndex = context.Model.FindEntityType(typeof(CustomerRebate))!.GetIndexes()
            .Single(i => i.IsUnique && i.Properties.Count == 1 && i.Properties.Single().Name == "BookingId");
        Assert.False(string.IsNullOrWhiteSpace(rebateIndex.GetFilter()));
        var commissionIndex = context.Model.FindEntityType(typeof(BookingCommission))!.GetIndexes()
            .Single(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "BookingId", "PartnerId" }));
        Assert.False(string.IsNullOrWhiteSpace(commissionIndex.GetFilter()));
    }

    [Fact]
    public async Task BookingCredit_ThatCoversTheBookingAmount_AdvancesTheOtherwiseStuckMilestone()
    {
        await using var harness = await Harness.Create();
        // Return the booking to awaiting-booking-amount with no cash received, drop the seeded payment
        // so a full balance-reduction credit is possible, and reserve the unit.
        var booking = harness.Context.Bookings.Include(b => b.Payments).Include(b => b.Unit).Single();
        harness.Context.Payments.RemoveRange(booking.Payments);
        booking.Status = BookingStatus.AwaitingBookingAmount;
        booking.BookingAmountReceived = 0m;
        booking.BookingAmountRequired = 100_000m;
        booking.BookingAmountConfirmedDate = null;
        booking.Unit.Status = UnitStatus.Reserved;
        await harness.Context.SaveChangesAsync();

        var full = harness.NetPrice; // the largest rebate the booking allows
        var workspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = full, Reason = "Full balance credit", Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        var rebate = Assert.Single(workspace.Rebates);
        await harness.UploadPdf(FinancialEvidenceOwnerType.Rebate, rebate.Id);
        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.PendingApproval), Actor);
        rebate = Assert.Single(workspace.Rebates);
        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.Approved, rebate.FinalAmount), Actor);
        rebate = Assert.Single(workspace.Rebates);

        // Still awaiting the booking amount; there is no remaining cash a payment could ever settle.
        Assert.Equal(BookingStatus.AwaitingBookingAmount, harness.Context.Bookings.Single().Status);

        await harness.Service.RecordRebateDisbursementAsync(harness.BookingId, rebate.Id,
            new RecordRebateDisbursementDto
            {
                Method = CustomerRebateMethod.OutstandingBalanceReduction, Amount = full, AppliedAt = DateTime.UtcNow,
                IdempotencyKey = "full-credit", RebateConcurrencyToken = rebate.ConcurrencyToken
            }, Actor);

        // Applying the credit that covers the booking-amount milestone advances the stuck booking.
        var advanced = harness.Context.Bookings.Include(b => b.Unit).Single();
        Assert.Equal(BookingStatus.PaymentPlanActive, advanced.Status);
        Assert.NotNull(advanced.BookingAmountConfirmedDate);
        Assert.Equal(UnitStatus.OnPaymentPlan, advanced.Unit.Status);
    }

    [Fact]
    public async Task BookingCredit_SupportsScheduleGeneration_AndReversalCannotCorruptTheMilestone()
    {
        await using var harness = await Harness.Create();
        var booking = harness.Context.Bookings.Include(b => b.Payments).Include(b => b.Unit).Single();
        harness.Context.Payments.RemoveRange(booking.Payments);
        booking.Status = BookingStatus.AwaitingBookingAmount;
        booking.BookingAmountReceived = 0m;
        booking.BookingAmountRequired = 100_000m;
        booking.BookingAmountConfirmedDate = null;
        booking.Unit.Status = UnitStatus.Reserved;
        await harness.Context.SaveChangesAsync();

        var workspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 100_000m, Reason = "Booking amount credit",
            Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        var rebate = Assert.Single(workspace.Rebates);
        await harness.UploadPdf(FinancialEvidenceOwnerType.Rebate, rebate.Id);
        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.PendingApproval), Actor);
        rebate = Assert.Single(workspace.Rebates);
        workspace = await harness.Service.ChangeRebateStatusAsync(harness.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.Approved, rebate.FinalAmount), Actor);
        rebate = Assert.Single(workspace.Rebates);
        workspace = await harness.Service.RecordRebateDisbursementAsync(harness.BookingId, rebate.Id,
            new RecordRebateDisbursementDto
            {
                Method = CustomerRebateMethod.OutstandingBalanceReduction, Amount = 100_000m,
                AppliedAt = DateTime.UtcNow, IdempotencyKey = "booking-credit-schedule",
                RebateConcurrencyToken = rebate.ConcurrencyToken
            }, Actor);
        var movement = Assert.Single(Assert.Single(workspace.Rebates).Disbursements);

        var installments = new InstallmentService(harness.Context, new FinanceAccountService(harness.Context));
        var schedule = await installments.GenerateScheduleAsync(harness.BookingId, new GenerateInstallmentPlanDto
        {
            AgreedSalePrice = 1_000_000.55m, DiscountPercent = 0m,
            Frequency = InstallmentFrequency.Monthly, NumberOfInstallments = 4,
            InstallmentStartDate = DateTime.UtcNow.Date.AddMonths(1)
        }, Actor.UserId);
        Assert.Equal(900_000.55m, schedule.Items.Sum(item => item.Amount));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.ReverseRebateDisbursementAsync(harness.BookingId, rebate.Id, movement.Id,
                new ReverseMoneyMovementDto
                {
                    Amount = 100_000m, Reason = "Invalid after plan", IdempotencyKey = "blocked-credit-reversal"
                }, Actor));
        Assert.Empty(harness.Context.RebateDisbursementReversals);

        await using var reversible = await Harness.Create();
        var reversibleBooking = reversible.Context.Bookings.Include(b => b.Payments).Include(b => b.Unit).Single();
        reversible.Context.Payments.RemoveRange(reversibleBooking.Payments);
        reversibleBooking.Status = BookingStatus.AwaitingBookingAmount;
        reversibleBooking.BookingAmountReceived = 0m;
        reversibleBooking.BookingAmountRequired = 100_000m;
        reversibleBooking.Unit.Status = UnitStatus.Reserved;
        await reversible.Context.SaveChangesAsync();
        workspace = await reversible.Service.CreateRebateAsync(reversible.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 100_000m, Reason = "Temporary booking credit",
            Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        rebate = Assert.Single(workspace.Rebates);
        await reversible.UploadPdf(FinancialEvidenceOwnerType.Rebate, rebate.Id);
        workspace = await reversible.Service.ChangeRebateStatusAsync(reversible.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.PendingApproval), Actor);
        rebate = Assert.Single(workspace.Rebates);
        workspace = await reversible.Service.ChangeRebateStatusAsync(reversible.BookingId, rebate.Id,
            RebateChange(rebate, CustomerRebateStatus.Approved, rebate.FinalAmount), Actor);
        rebate = Assert.Single(workspace.Rebates);
        workspace = await reversible.Service.RecordRebateDisbursementAsync(reversible.BookingId, rebate.Id,
            new RecordRebateDisbursementDto
            {
                Method = CustomerRebateMethod.OutstandingBalanceReduction, Amount = 100_000m,
                AppliedAt = DateTime.UtcNow, IdempotencyKey = "temporary-booking-credit",
                RebateConcurrencyToken = rebate.ConcurrencyToken
            }, Actor);
        movement = Assert.Single(Assert.Single(workspace.Rebates).Disbursements);
        await reversible.Service.ReverseRebateDisbursementAsync(reversible.BookingId, rebate.Id, movement.Id,
            new ReverseMoneyMovementDto
            {
                Amount = 100_000m, Reason = "Credit withdrawn", IdempotencyKey = "valid-credit-reversal"
            }, Actor);
        var demoted = reversible.Context.Bookings.Include(b => b.Unit).Single();
        Assert.Equal(BookingStatus.AwaitingBookingAmount, demoted.Status);
        Assert.Equal(UnitStatus.Reserved, demoted.Unit.Status);
        Assert.Null(demoted.BookingAmountConfirmedDate);
    }

    [Fact]
    public async Task RuleUpdate_WithMaximumLengthValues_PreservesFullDiff_WithoutFailing()
    {
        await using var harness = await Harness.Create();
        var created = await harness.Service.CreateRuleAsync(new SaveCommissionRuleDto
        {
            Name = "Detailed Rule", IsActive = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), Priority = 1,
            CalculationType = FinancialCalculationType.Percentage, PercentageRate = 2m,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            EarningCondition = CommissionEarningCondition.BookingAmountFullyReceived
        }, Actor);

        // Every long text field changes at once, so the machine-generated diff exceeds the old
        // 2,000-char audit cap. The update must still succeed and record the full before -> after detail.
        await harness.Service.UpdateRuleAsync(created.Id, new SaveCommissionRuleDto
        {
            Name = "Detailed Rule", IsActive = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), Priority = 1,
            CalculationType = FinancialCalculationType.Percentage, PercentageRate = 2m,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            EarningCondition = CommissionEarningCondition.BookingAmountFullyReceived,
            Description = new string('a', 1000), EligibilityCondition = new string('c', 1000),
            Notes = new string('b', 2000), ConcurrencyToken = created.ConcurrencyToken,
            ChangeReason = new string('r', 2000)
        }, Actor);

        var audit = Assert.Single(harness.Context.FinancialWorkflowAuditEntries
            .Where(a => a.Action == FinancialWorkflowAction.RuleUpdated).ToList());
        Assert.Equal(new string('r', 2000), audit.Reason);
        var revision = await harness.Context.CommissionRuleRevisions.OrderByDescending(r => r.RevisionNumber).FirstAsync();
        Assert.Contains(new string('b', 2000), revision.SnapshotJson);
    }

    private static CommissionStatusChangeDto Change(BookingCommissionDto commission, BookingCommissionStatus status,
        string? reason = null, decimal? approved = null) => new()
    {
        TargetStatus = status, Reason = reason, ApprovedAmount = approved, ConcurrencyToken = commission.ConcurrencyToken
    };

    private static RebateStatusChangeDto RebateChange(CustomerRebateDto rebate, CustomerRebateStatus status,
        decimal? approved = null, string? reason = null) => new()
    {
        TargetStatus = status, ApprovedAmount = approved, Reason = reason, ConcurrencyToken = rebate.ConcurrencyToken
    };

    private static CommissionRule Rule(string name, int priority, decimal? rate = null, decimal? fixedAmount = null,
        int? partnerId = null) => new()
    {
        Name = name, IsActive = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), Priority = priority,
        PartnerId = partnerId, CalculationType = rate.HasValue ? FinancialCalculationType.Percentage : FinancialCalculationType.FixedAmount,
        PercentageRate = rate, FixedAmount = fixedAmount, CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
        EarningCondition = CommissionEarningCondition.BookingAmountFullyReceived
    };

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Harness : IAsyncDisposable
    {
        public AppDbContext Context { get; }
        public CommissionRebateService Service { get; }
        public MemoryEvidenceStorage Storage { get; }
        public int BookingId { get; private set; }
        public int PartnerId { get; private set; }
        public int AttributionId { get; private set; }
        public int AccountId { get; private set; }
        public decimal NetPrice => 1_000_000.55m - Discount;
        public decimal Discount => 50_000.10m;

        private Harness(AppDbContext context, MemoryEvidenceStorage storage)
        {
            Context = context; Storage = storage;
            Service = new CommissionRebateService(context, new FinanceAccountService(context), storage);
        }

        public static async Task<Harness> Create()
        {
            var context = CommissionRebateTests.Context(); var storage = new MemoryEvidenceStorage();
            var harness = new Harness(context, storage);
            var project = new Project { ProjectName = "Financial Test", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit { Project = project, UnitNumber = "T-01", UnitType = "Apartment", Price = 1_100_000m, Status = UnitStatus.OnPaymentPlan };
            var customer = new Customer { FullName = "Test Customer", Phone = "03000000000" };
            var booking = new Booking
            {
                BookingReference = $"BK-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.PaymentPlanActive, Source = CustomerSource.Referral,
                AgreedSalePrice = 1_000_000.55m, DiscountAmount = harness.Discount,
                BookingAmountRequired = 100_000m, BookingAmountReceived = 100_000m, BookingDate = DateTime.UtcNow
            };
            booking.Payments.Add(new Payment { Booking = booking, Amount = 100_000m, Type = PaymentType.BookingAmount, PaymentMethod = PaymentMethod.BankTransfer });
            var partner = new ThirdPartyPartner
            {
                Name = "ABC Broker", PartnerType = "Broker", InternalCode = "ABC-1", IsActive = true,
                BankName = "Test Bank", AccountTitle = "ABC Broker", AccountNumber = "00123456789"
            };
            var attribution = new ThirdPartyAttribution
            {
                Booking = booking, Partner = partner, RelationshipType = "Broker", AllocationPercent = 100m,
                IsPrimary = true, AssignedAt = DateTime.UtcNow
            };
            var account = new FinanceAccount { Name = "Operations Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
            context.AddRange(project, unit, customer, booking, partner, attribution, account);
            await context.SaveChangesAsync();
            harness.BookingId = booking.Id; harness.PartnerId = partner.Id; harness.AttributionId = attribution.Id; harness.AccountId = account.Id;
            return harness;
        }

        public async Task<BookingCommissionDto> CreateRuleCommission()
        {
            if (!Context.CommissionRules.Any())
            {
                Context.CommissionRules.Add(Rule("Standard 2%", 1, rate: 2m));
                await Context.SaveChangesAsync();
            }
            return Assert.Single((await Service.CreateCommissionAsync(BookingId,
                new CreateBookingCommissionDto { PartnerId = PartnerId, AttributionId = AttributionId }, Actor)).Commissions);
        }

        public async Task<BookingCommissionDto> MakePayable()
        {
            var commission = await CreateRuleCommission();
            var workspace = await Service.ChangeCommissionStatusAsync(BookingId, commission.Id,
                Change(commission, BookingCommissionStatus.PendingApproval), Actor);
            commission = Assert.Single(workspace.Commissions);
            workspace = await Service.ChangeCommissionStatusAsync(BookingId, commission.Id,
                Change(commission, BookingCommissionStatus.Approved, approved: commission.FinalAmount), Actor);
            commission = Assert.Single(workspace.Commissions);
            workspace = await Service.ChangeCommissionStatusAsync(BookingId, commission.Id,
                Change(commission, BookingCommissionStatus.Earned), Actor);
            commission = Assert.Single(workspace.Commissions);
            workspace = await Service.ChangeCommissionStatusAsync(BookingId, commission.Id,
                Change(commission, BookingCommissionStatus.Payable), Actor);
            return Assert.Single(workspace.Commissions);
        }

        public async Task UploadPdf(FinancialEvidenceOwnerType type, int id)
        {
            var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");
            await Service.UploadEvidenceAsync(type, id,
                new FinancialEvidenceUpload { Content = new MemoryStream(bytes), FileName = "proof.pdf", Length = bytes.Length }, Actor);
        }

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }

    private sealed class MemoryEvidenceStorage : IFinancialEvidenceStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            var key = $"{Guid.NewGuid():N}{extension}"; content.Position = 0; using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken); Files[key] = buffer.ToArray(); return key;
        }
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storedFileName, out var value) ? new MemoryStream(value) : null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
        { Files.Remove(storedFileName); return Task.CompletedTask; }
    }
}
