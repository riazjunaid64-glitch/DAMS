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
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task Summary_PreservesStatusPartialPaymentReversalAndPartnerSemantics()
    {
        await using var harness = await Harness.Create();
        var booking = await harness.Context.Bookings.SingleAsync(b => b.Id == harness.BookingId);
        var partner = await harness.Context.ThirdPartyPartners.SingleAsync(p => p.Id == harness.PartnerId);
        var account = await harness.Context.FinanceAccounts.SingleAsync(a => a.Id == harness.AccountId);

        harness.Context.ThirdPartyPartners.AddRange(
            new ThirdPartyPartner
            {
                Name = "Second active", PartnerType = "Broker", InternalCode = "ACTIVE-2", IsActive = true
            },
            new ThirdPartyPartner
            {
                Name = "Inactive", PartnerType = "Dealer", InternalCode = "INACTIVE-1", IsActive = false
            });

        BookingCommission Commission(BookingCommissionStatus status, decimal amount) => new()
        {
            Booking = booking,
            Partner = partner,
            PartnerNameSnapshot = partner.Name,
            PartnerTypeSnapshot = partner.PartnerType,
            PartnerInternalCodeSnapshot = partner.InternalCode,
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.ManuallyApprovedAmount,
            FixedAmount = amount,
            BasisAmount = amount,
            CalculatedAmount = amount,
            FinalAmount = amount,
            Status = status
        };

        var pendingCommission = Commission(BookingCommissionStatus.Pending, 100m);
        var paidCommission = Commission(BookingCommissionStatus.Paid, 200m);
        var cancelledCommission = Commission(BookingCommissionStatus.Cancelled, 300m);
        var recoveryCommission = Commission(BookingCommissionStatus.ReversalRequired, 400m);
        var reversedCommission = Commission(BookingCommissionStatus.Reversed, 500m);
        harness.Context.BookingCommissions.AddRange(pendingCommission, paidCommission, cancelledCommission,
            recoveryCommission, reversedCommission);

        CommissionPayout Payout(BookingCommission commission, decimal amount, string key) => new()
        {
            Commission = commission,
            FinanceAccount = account,
            Amount = amount,
            PaymentDate = PakistanTime.Today,
            PaymentMethod = PaymentMethod.Cash,
            IdempotencyKey = key
        };

        var pendingPayout = Payout(pendingCommission, 40m, "summary-commission-pending");
        var paidPayout = Payout(paidCommission, 200m, "summary-commission-paid");
        var recoveryPayout = Payout(recoveryCommission, 150m, "summary-commission-recovery");
        var reversedPayout = Payout(reversedCommission, 80m, "summary-commission-reversed");
        harness.Context.CommissionPayouts.AddRange(pendingPayout, paidPayout, recoveryPayout, reversedPayout);
        harness.Context.CommissionPayoutReversals.AddRange(
            new CommissionPayoutReversal
            {
                Payout = pendingPayout, Amount = 10m, Reason = "Partial correction",
                IdempotencyKey = "summary-commission-pending-reversal"
            },
            new CommissionPayoutReversal
            {
                Payout = recoveryPayout, Amount = 50m, Reason = "Partial recovery",
                IdempotencyKey = "summary-commission-recovery-reversal"
            },
            new CommissionPayoutReversal
            {
                Payout = reversedPayout, Amount = 80m, Reason = "Fully reversed",
                IdempotencyKey = "summary-commission-full-reversal"
            });

        CustomerRebate Rebate(CustomerRebateStatus status, decimal amount) => new()
        {
            Booking = booking,
            Customer = booking.Customer,
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.ManuallyApprovedAmount,
            FixedAmount = amount,
            BasisAmount = amount,
            CalculatedAmount = amount,
            FinalAmount = amount,
            Reason = "Summary fixture",
            Status = status
        };

        var pendingRebate = Rebate(CustomerRebateStatus.Pending, 60m);
        var appliedRebate = Rebate(CustomerRebateStatus.Applied, 70m);
        var paidRebate = Rebate(CustomerRebateStatus.Paid, 80m);
        var cancelledRebate = Rebate(CustomerRebateStatus.Cancelled, 90m);
        var recoveryRebate = Rebate(CustomerRebateStatus.ReversalRequired, 100m);
        var reversedRebate = Rebate(CustomerRebateStatus.Reversed, 110m);
        harness.Context.CustomerRebates.AddRange(pendingRebate, appliedRebate, paidRebate, cancelledRebate,
            recoveryRebate, reversedRebate);

        RebateDisbursement Disbursement(CustomerRebate rebate, decimal amount, string key) => new()
        {
            Rebate = rebate,
            Method = CustomerRebateMethod.CreditNote,
            Amount = amount,
            AppliedAt = PakistanTime.Today,
            IdempotencyKey = key
        };

        var pendingDisbursement = Disbursement(pendingRebate, 20m, "summary-rebate-pending");
        var appliedDisbursement = Disbursement(appliedRebate, 70m, "summary-rebate-applied");
        var paidDisbursement = Disbursement(paidRebate, 80m, "summary-rebate-paid");
        var recoveryDisbursement = Disbursement(recoveryRebate, 50m, "summary-rebate-recovery");
        var reversedDisbursement = Disbursement(reversedRebate, 40m, "summary-rebate-reversed");
        harness.Context.RebateDisbursements.AddRange(pendingDisbursement, appliedDisbursement, paidDisbursement,
            recoveryDisbursement, reversedDisbursement);
        harness.Context.RebateDisbursementReversals.AddRange(
            new RebateDisbursementReversal
            {
                Disbursement = pendingDisbursement, Amount = 5m, Reason = "Partial correction",
                IdempotencyKey = "summary-rebate-pending-reversal"
            },
            new RebateDisbursementReversal
            {
                Disbursement = recoveryDisbursement, Amount = 20m, Reason = "Partial recovery",
                IdempotencyKey = "summary-rebate-recovery-reversal"
            },
            new RebateDisbursementReversal
            {
                Disbursement = reversedDisbursement, Amount = 40m, Reason = "Fully reversed",
                IdempotencyKey = "summary-rebate-full-reversal"
            });

        await harness.Context.SaveChangesAsync();

        var summary = await harness.Service.GetSummaryAsync();

        Assert.Equal(300m, summary.AccruedCommission);
        Assert.Equal(70m, summary.PayableCommission);
        Assert.Equal(330m, summary.CommissionPaid);
        Assert.Equal(100m, summary.CommissionReversalRequired);
        Assert.Equal(210m, summary.RebatesGranted);
        Assert.Equal(195m, summary.RebatesAppliedOrPaid);
        Assert.Equal(30m, summary.RebateReversalRequired);
        Assert.Equal(2, summary.ActivePartners);
        Assert.Equal(2, summary.PendingRecords);
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
    public async Task NewCommission_IsPendingPaymentImmediately()
    {
        await using var harness = await Harness.Create();
        // A rule decides the amount and nothing else — there is no approval or earning step left for
        // one to gate. It is owed from the moment it is agreed, and payment is all that remains.
        harness.Context.CommissionRules.Add(Rule("Standard", 10, rate: 2m));
        await harness.Context.SaveChangesAsync();

        var commission = await harness.Service.CreateCommissionAsync(harness.BookingId,
            new CreateBookingCommissionDto { PartnerId = harness.PartnerId, AttributionId = harness.AttributionId }, Actor);
        var created = Assert.Single(commission.Commissions);

        Assert.Equal(BookingCommissionStatus.Pending, created.Status);
        Assert.Equal(created.FinalAmount, created.OutstandingAmount);
        Assert.Equal(0m, created.PaidAmount);
        Assert.Contains(harness.Context.FinancialWorkflowAuditEntries,
            a => a.Action == FinancialWorkflowAction.CommissionCreated
                && a.NewCommissionStatus == BookingCommissionStatus.Pending);
        Assert.DoesNotContain(harness.Context.FinancialWorkflowAuditEntries,
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
    public async Task CommissionLifecycle_IsPendingThenPaid_AndNoStatusCanBeSetByHand()
    {
        await using var harness = await Harness.Create();
        var commission = await harness.CreateRuleCommission();
        Assert.Equal(BookingCommissionStatus.Pending, commission.Status);

        // Paid is reached by paying, never by declaring it. Cancelling is the one status a person
        // still sets, and it needs a reason.
        foreach (var target in new[] { BookingCommissionStatus.Paid, BookingCommissionStatus.Reversed,
                     BookingCommissionStatus.ReversalRequired })
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.ChangeCommissionStatusAsync(
                harness.BookingId, commission.Id, Change(commission, target), Actor));
            Assert.Contains("cannot move", refused.Message);
        }

        // Half of it goes out: still Pending, with the paid and remaining halves both readable.
        var half = Math.Round(commission.FinalAmount / 2m, 2, MidpointRounding.AwayFromZero);
        var workspace = await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id,
            Payout(harness.AccountId, half, commission.ConcurrencyToken, "half"), Actor);
        commission = Assert.Single(workspace.Commissions);
        Assert.Equal(BookingCommissionStatus.Pending, commission.Status);
        Assert.Equal(half, commission.PaidAmount);
        Assert.Equal(commission.FinalAmount - half, commission.OutstandingAmount);

        workspace = await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id,
            Payout(harness.AccountId, commission.OutstandingAmount, commission.ConcurrencyToken, "rest"), Actor);
        commission = Assert.Single(workspace.Commissions);
        Assert.Equal(BookingCommissionStatus.Paid, commission.Status);
        Assert.Equal(0m, commission.OutstandingAmount);
        var actions = harness.Context.FinancialWorkflowAuditEntries.Where(a => a.CommissionId == commission.Id).Select(a => a.Action).ToList();
        Assert.Contains(FinancialWorkflowAction.CommissionCreated, actions);
        Assert.Contains(FinancialWorkflowAction.PayoutRecorded, actions);
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
        Assert.Equal(BookingCommissionStatus.Pending, commission.Status);
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
        Assert.Equal(BookingCommissionStatus.Pending, commission.Status);

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
                ManualPercentageRate = 2.3456789m, AdjustmentAmount = 10m,
                AdjustmentReason = "Approved administrative adjustment"
            }, Actor);
        var commission = Assert.Single(workspace.Commissions);
        Assert.Equal(2.345679m, commission.PercentageRate);
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
    public async Task Rebate_IsSeparateAndCannotApplyTwiceOrBelowZero()
    {
        await using var harness = await Harness.Create();
        var workspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount, CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 1_000m, Reason = "Customer retention", Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        var rebate = Assert.Single(workspace.Rebates);
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
        }, Actor);

        await harness.Service.UpdateRuleAsync(created.Id, new SaveCommissionRuleDto
        {
            Name = "Base Rule", IsActive = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), Priority = 5,
            CalculationType = FinancialCalculationType.Percentage, PercentageRate = 3m,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            
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
    public async Task PendingCommission_CanBeCorrectedInPlace_UntilTheFirstPayout()
    {
        await using var harness = await Harness.Create();
        var commission = await harness.CreateRuleCommission();
        await harness.UploadPdf(FinancialEvidenceOwnerType.Commission, commission.Id);

        var workspace = await harness.Service.UpdateCommissionAsync(harness.BookingId, commission.Id,
            new UpdateBookingCommissionDto
            {
                PartnerId = harness.PartnerId, AttributionId = harness.AttributionId, IsManual = true,
                ManualReason = "Agreed correction", ManualCalculationType = FinancialCalculationType.FixedAmount,
                ManualCalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                ManualFixedAmount = 2_500m,
                ConcurrencyToken = commission.ConcurrencyToken, ChangeReason = "Corrected after finance review."
            }, Actor);
        commission = Assert.Single(workspace.Commissions);
        // Correcting leaves it exactly where it was: pending, for the corrected amount.
        Assert.Equal(BookingCommissionStatus.Pending, commission.Status);
        Assert.True(commission.IsManual);
        Assert.Equal(2_500m, commission.FinalAmount);
        Assert.Equal(2_500m, commission.OutstandingAmount);
        Assert.Contains(await harness.Context.FinancialWorkflowAuditEntries.ToListAsync(), entry =>
            entry.Action == FinancialWorkflowAction.CommissionAdjusted
            && entry.Reason == "Corrected after finance review." && entry.PreviousAmount != entry.NewAmount);

        // Once money has gone out the agreed figures are history: reverse the payout to change them.
        workspace = await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id,
            Payout(harness.AccountId, 500m, commission.ConcurrencyToken), Actor);
        commission = Assert.Single(workspace.Commissions);
        Assert.Equal(BookingCommissionStatus.Pending, commission.Status);
        var locked = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.UpdateCommissionAsync(
            harness.BookingId, commission.Id, new UpdateBookingCommissionDto
            {
                PartnerId = harness.PartnerId, IsManual = true, ManualCalculationType = FinancialCalculationType.FixedAmount,
                ManualCalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount, ManualFixedAmount = 100m,
                
                ConcurrencyToken = commission.ConcurrencyToken, ChangeReason = "Too late"
            }, Actor));
        Assert.Contains("payout history", locked.Message);
    }

    [Fact]
    public async Task PendingRebate_CanBeCorrectedInPlace_UntilTheFirstDisbursement()
    {
        await using var harness = await Harness.Create();
        var workspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 500m, Reason = "Initial proposal", Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        var rebate = Assert.Single(workspace.Rebates);
        Assert.Equal(CustomerRebateStatus.Pending, rebate.Status);
        await harness.UploadPdf(FinancialEvidenceOwnerType.Rebate, rebate.Id);

        workspace = await harness.Service.UpdateRebateAsync(harness.BookingId, rebate.Id,
            new UpdateCustomerRebateDto
            {
                CalculationType = FinancialCalculationType.FixedAmount,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                FixedAmount = 750m, Reason = "Customer retention benefit",
                Method = CustomerRebateMethod.CreditNote, ConcurrencyToken = rebate.ConcurrencyToken,
                ChangeReason = "Corrected after commercial review."
            }, Actor);
        rebate = Assert.Single(workspace.Rebates);
        Assert.Equal(CustomerRebateStatus.Pending, rebate.Status);
        Assert.Equal(750m, rebate.FinalAmount);
        Assert.Equal(750m, rebate.OutstandingAmount);
        Assert.Contains(await harness.Context.FinancialWorkflowAuditEntries.ToListAsync(), entry =>
            entry.Action == FinancialWorkflowAction.RebateAdjusted
            && entry.Reason == "Corrected after commercial review." && entry.PreviousAmount != entry.NewAmount);

        workspace = await harness.Service.RecordRebateDisbursementAsync(harness.BookingId, rebate.Id,
            new RecordRebateDisbursementDto
            {
                Method = CustomerRebateMethod.OutstandingBalanceReduction, Amount = 250m, AppliedAt = DateTime.UtcNow,
                IdempotencyKey = "partial-apply", RebateConcurrencyToken = rebate.ConcurrencyToken
            }, Actor);
        rebate = Assert.Single(workspace.Rebates);
        Assert.Equal(CustomerRebateStatus.Pending, rebate.Status);
        var locked = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.UpdateRebateAsync(
            harness.BookingId, rebate.Id, new UpdateCustomerRebateDto
            {
                CalculationType = FinancialCalculationType.FixedAmount,
                CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                FixedAmount = 100m, Reason = "Too late", Method = CustomerRebateMethod.OutstandingBalanceReduction,
                ConcurrencyToken = rebate.ConcurrencyToken, ChangeReason = "Too late"
            }, Actor));
        Assert.Contains("application or payment history", locked.Message);
    }

    [Fact]
    public async Task EditingACommission_OnALockedBookingAmountReceivedBasis_DoesNotSilentlyReprice()
    {
        await using var harness = await Harness.Create();
        var workspace = await harness.Service.CreateCommissionAsync(harness.BookingId, new CreateBookingCommissionDto
        {
            PartnerId = harness.PartnerId, AttributionId = harness.AttributionId, IsManual = true,
            ManualCalculationType = FinancialCalculationType.Percentage,
            ManualCalculationBasis = FinancialCalculationBasis.BookingAmountReceived, ManualPercentageRate = 10m
        }, Actor);
        var commission = Assert.Single(workspace.Commissions);
        Assert.Equal(100_000m, commission.BasisAmount);
        Assert.Equal(10_000m, commission.FinalAmount);

        // More of the booking amount arrives after the commission was agreed — exactly what makes
        // BookingAmountReceived a moving target for anything that re-reads it later.
        var booking = await harness.Context.Bookings.SingleAsync(b => b.Id == harness.BookingId);
        booking.BookingAmountReceived = 250_000m;
        await harness.Context.SaveChangesAsync();

        // A note-only edit: same partner, same rate, the same basis the form shows read-only —
        // nothing an operator would consider a calculation change.
        workspace = await harness.Service.UpdateCommissionAsync(harness.BookingId, commission.Id,
            new UpdateBookingCommissionDto
            {
                PartnerId = harness.PartnerId, AttributionId = harness.AttributionId, IsManual = true,
                ManualReason = "Typo fix in the note only.", ManualCalculationType = FinancialCalculationType.Percentage,
                ManualCalculationBasis = FinancialCalculationBasis.BookingAmountReceived, ManualPercentageRate = 10m,
                ConcurrencyToken = commission.ConcurrencyToken, ChangeReason = "Fixed a typo in the note."
            }, Actor);
        commission = Assert.Single(workspace.Commissions);

        // Frozen at what it was agreed against, not re-priced against the 250,000 now on the booking.
        Assert.Equal(100_000m, commission.BasisAmount);
        Assert.Equal(10_000m, commission.FinalAmount);
    }

    [Fact]
    public async Task EditingARebate_OnALockedBookingAmountReceivedBasis_DoesNotSilentlyReprice()
    {
        await using var harness = await Harness.Create();
        var workspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.Percentage,
            CalculationBasis = FinancialCalculationBasis.BookingAmountReceived, PercentageRate = 10m,
            Reason = "Loyalty rebate", Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        var rebate = Assert.Single(workspace.Rebates);
        Assert.Equal(100_000m, rebate.BasisAmount);
        Assert.Equal(10_000m, rebate.FinalAmount);

        var booking = await harness.Context.Bookings.SingleAsync(b => b.Id == harness.BookingId);
        booking.BookingAmountReceived = 250_000m;
        await harness.Context.SaveChangesAsync();

        workspace = await harness.Service.UpdateRebateAsync(harness.BookingId, rebate.Id,
            new UpdateCustomerRebateDto
            {
                CalculationType = FinancialCalculationType.Percentage,
                CalculationBasis = FinancialCalculationBasis.BookingAmountReceived, PercentageRate = 10m,
                Reason = "Loyalty rebate", Method = CustomerRebateMethod.OutstandingBalanceReduction,
                ConcurrencyToken = rebate.ConcurrencyToken, ChangeReason = "Fixed a typo in the note."
            }, Actor);
        rebate = Assert.Single(workspace.Rebates);

        Assert.Equal(100_000m, rebate.BasisAmount);
        Assert.Equal(10_000m, rebate.FinalAmount);
    }

    [Fact]
    public async Task EditingACommission_OnAnEditableAmountActuallyCollectedBasis_StillTracksTheLatestCollections()
    {
        // The other side of the same fix: a basis the edit form DOES let the operator actively
        // re-pick every time is meant to keep tracking live data, and must not get frozen too.
        await using var harness = await Harness.Create();
        var workspace = await harness.Service.CreateCommissionAsync(harness.BookingId, new CreateBookingCommissionDto
        {
            PartnerId = harness.PartnerId, AttributionId = harness.AttributionId, IsManual = true,
            ManualCalculationType = FinancialCalculationType.Percentage,
            ManualCalculationBasis = FinancialCalculationBasis.AmountActuallyCollected, ManualPercentageRate = 10m
        }, Actor);
        var commission = Assert.Single(workspace.Commissions);
        Assert.Equal(100_000m, commission.BasisAmount);

        harness.Context.Payments.Add(new Payment
        {
            BookingId = harness.BookingId, Amount = 50_000m, Type = PaymentType.BookingAmount,
            PaymentMethod = PaymentMethod.BankTransfer
        });
        await harness.Context.SaveChangesAsync();

        workspace = await harness.Service.UpdateCommissionAsync(harness.BookingId, commission.Id,
            new UpdateBookingCommissionDto
            {
                PartnerId = harness.PartnerId, AttributionId = harness.AttributionId, IsManual = true,
                ManualCalculationType = FinancialCalculationType.Percentage,
                ManualCalculationBasis = FinancialCalculationBasis.AmountActuallyCollected, ManualPercentageRate = 10m,
                ConcurrencyToken = commission.ConcurrencyToken, ChangeReason = "Refreshing after the extra receipt."
            }, Actor);
        commission = Assert.Single(workspace.Commissions);

        Assert.Equal(150_000m, commission.BasisAmount);
        Assert.Equal(15_000m, commission.FinalAmount);
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
        Assert.Single(workspace.Rebates, r => r.Status == CustomerRebateStatus.Pending);
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
        }, Actor);

        // Every long text field changes at once, so the machine-generated diff exceeds the old
        // 2,000-char audit cap. The update must still succeed and record the full before -> after detail.
        await harness.Service.UpdateRuleAsync(created.Id, new SaveCommissionRuleDto
        {
            Name = "Detailed Rule", IsActive = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), Priority = 1,
            CalculationType = FinancialCalculationType.Percentage, PercentageRate = 2m,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            Description = new string('a', 1000),
            Notes = new string('b', 2000), ConcurrencyToken = created.ConcurrencyToken,
            ChangeReason = new string('r', 2000)
        }, Actor);

        var audit = Assert.Single(harness.Context.FinancialWorkflowAuditEntries
            .Where(a => a.Action == FinancialWorkflowAction.RuleUpdated).ToList());
        Assert.Equal(new string('r', 2000), audit.Reason);
        var revision = await harness.Context.CommissionRuleRevisions.OrderByDescending(r => r.RevisionNumber).FirstAsync();
        Assert.Contains(new string('b', 2000), revision.SnapshotJson);
    }

    /// <summary>
    /// A reversal is a P&amp;L event: it takes a commission cost or a rebate cost back out of the
    /// period. So the day it is filed on is a BUSINESS day, and DAMS' business day is Pakistan's.
    /// <para>
    /// Stamped from <c>DateTime.UtcNow</c>, a reversal entered at 01:00 PKT on the 1st was dated the
    /// last day of the previous month — reopening a month that was reported as closed, silently, by a
    /// correction that had nothing to do with it. That is the failure this asserts against; between
    /// 19:00 and 23:59 UTC it fails outright if the stamp goes back to UTC.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ReversingAPayoutOrARebate_DatesTheReversalOnThePakistanBusinessDay()
    {
        await using var harness = await Harness.Create();
        var today = PakistanTime.Today;

        var commission = await harness.MakePayable();
        var paid = await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id, new RecordCommissionPayoutDto
        {
            FinanceAccountId = harness.AccountId, Amount = 100m, PaymentDate = today,
            PaymentMethod = PaymentMethod.Cash, IdempotencyKey = "payout-date",
            CommissionConcurrencyToken = commission.ConcurrencyToken
        }, Actor);
        var payout = Assert.Single(Assert.Single(paid.Commissions).Payouts);

        await harness.Service.ReversePayoutAsync(harness.BookingId, commission.Id, payout.Id,
            new ReverseMoneyMovementDto { Amount = 40m, Reason = "Correction", IdempotencyKey = "reverse-date" }, Actor);

        var reversal = Assert.Single(harness.Context.CommissionPayoutReversals.ToList());
        Assert.Equal(today, reversal.ReversedAt.Date);

        // …and today's P&L carries the commission the day it was AGREED, untouched by the payout or
        // its reversal: both settle Commission Payable and neither is a cost. The payable itself
        // moves with them — 19,000.01 accrued, 100 paid, 40 given back.
        var accounts = new FinanceAccountService(harness.Context);
        var finance = new FinanceService(harness.Context, new NoopAttachmentStorage(), accounts,
            new WhtService(harness.Context, accounts), NullLogger<FinanceService>.Instance);
        var pnl = await finance.GetProfitAndLossAsync(null, today, today);
        var accrued = Assert.Single(harness.Context.CommissionAccruals.ToList()).Amount;
        Assert.Equal(commission.FinalAmount, accrued);
        Assert.Equal(accrued, Assert.Single(pnl.ExpenseLines, l => l.Name == "Partner Commissions").Amount);
    }

    /// <summary>
    /// The booking screen's whole commission flow: pick a partner, pick percentage or fixed, save.
    /// No attribution, no rule, no allocation, no typed reason — and two partners can each hold a
    /// full commission on one booking without competing for a shared 100% allocation budget.
    /// </summary>
    [Fact]
    public async Task DirectCommission_NeedsNoAttributionRuleOrReason_AndTwoPartnersEachGetTheirFullAmount()
    {
        await using var harness = await Harness.Create();
        var second = new ThirdPartyPartner { Name = "Second Dealer", PartnerType = "Dealer", InternalCode = "SEC-1", IsActive = true };
        harness.Context.ThirdPartyPartners.Add(second);
        await harness.Context.SaveChangesAsync();

        var workspace = await harness.Service.CreateCommissionAsync(harness.BookingId, Direct(harness.PartnerId, rate: 2m), Actor);
        workspace = await harness.Service.CreateCommissionAsync(harness.BookingId, Direct(second.Id, fixedAmount: 25_000m), Actor);

        Assert.Equal(2, workspace.Commissions.Count);
        var percentage = workspace.Commissions.Single(c => c.PartnerId == harness.PartnerId);
        var fixedOne = workspace.Commissions.Single(c => c.PartnerId == second.Id);
        // 2% of the net sale price in full — an allocation percentage never scales it down.
        Assert.Equal(Math.Round(harness.NetPrice * 0.02m, 2, MidpointRounding.AwayFromZero), percentage.FinalAmount);
        Assert.Equal(100m, percentage.AllocationPercent);
        Assert.Equal(25_000m, fixedOne.FinalAmount);
        Assert.All(workspace.Commissions, c => Assert.Null(c.AttributionId));
        Assert.All(workspace.Commissions, c => Assert.Null(c.RuleId));
        Assert.All(workspace.Commissions, c => Assert.Null(c.ManualReason));
        // The log still says what was agreed even though nobody typed a reason.
        Assert.Contains(harness.Context.FinancialWorkflowAuditEntries,
            e => e.Action == FinancialWorkflowAction.CommissionCreated && e.Reason == "2% of net sale price");
    }

    /// <summary>
    /// An explicitly named attribution still splits the commission by its allocation, which is what a
    /// shared introduction needs. Naming one that belongs elsewhere is refused rather than ignored.
    /// </summary>
    [Fact]
    public async Task NamedAttribution_StillAppliesItsAllocation_AndIsCheckedAgainstTheBookingAndPartner()
    {
        await using var harness = await Harness.Create();
        var attribution = await harness.Context.ThirdPartyAttributions.FindAsync(harness.AttributionId);
        Assert.NotNull(attribution);
        attribution.AllocationPercent = 50m;
        var stranger = new ThirdPartyPartner { Name = "Unrelated", PartnerType = "Agency", InternalCode = "UNR-1", IsActive = true };
        harness.Context.ThirdPartyPartners.Add(stranger);
        await harness.Context.SaveChangesAsync();

        var direct = Direct(harness.PartnerId, rate: 2m);
        direct.AttributionId = harness.AttributionId;
        var workspace = await harness.Service.CreateCommissionAsync(harness.BookingId, direct, Actor);
        var commission = Assert.Single(workspace.Commissions);
        Assert.Equal(50m, commission.AllocationPercent);
        Assert.Equal(Math.Round(harness.NetPrice * 0.02m * 0.5m, 2, MidpointRounding.AwayFromZero), commission.FinalAmount);

        var mismatched = Direct(stranger.Id, rate: 1m);
        mismatched.AttributionId = harness.AttributionId;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.CreateCommissionAsync(harness.BookingId, mismatched, Actor));
        Assert.Contains("different partner", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The rebate entry screen collects a type, an amount, and nothing else. The delivery method is
    /// chosen when the rebate is applied, and the first application locks it for the rest.
    /// </summary>
    [Fact]
    public async Task DirectRebate_NeedsNoTypedReason_AndTheMethodIsChosenAndLockedAtApplicationTime()
    {
        await using var harness = await Harness.Create();
        var workspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.Percentage,
            CalculationBasis = FinancialCalculationBasis.AgreedSalePrice,
            PercentageRate = 5m
        }, Actor);
        var rebate = Assert.Single(workspace.Rebates);
        Assert.Equal(Math.Round(1_000_000.55m * 0.05m, 2, MidpointRounding.AwayFromZero), rebate.FinalAmount);
        Assert.Equal("5% of sale price", rebate.Reason);

        await harness.UploadPdf(FinancialEvidenceOwnerType.Rebate, rebate.Id);

        // Entered as the default balance reduction, applied as a credit note: the first disbursement
        // decides, and the booking's non-cash credit total follows the method actually used.
        workspace = await harness.Service.RecordRebateDisbursementAsync(harness.BookingId, rebate.Id,
            new RecordRebateDisbursementDto
            {
                Method = CustomerRebateMethod.CreditNote, Amount = 1_000m, AppliedAt = DateTime.UtcNow,
                Reference = "CN-LOCK", IdempotencyKey = "rebate-method-lock",
                RebateConcurrencyToken = rebate.ConcurrencyToken
            }, Actor);
        rebate = Assert.Single(workspace.Rebates);
        Assert.Equal(CustomerRebateMethod.CreditNote, rebate.Method);
        Assert.Equal(1_000m, workspace.RebateCredits);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.RecordRebateDisbursementAsync(harness.BookingId, rebate.Id,
                new RecordRebateDisbursementDto
                {
                    Method = CustomerRebateMethod.OutstandingBalanceReduction, Amount = 500m, AppliedAt = DateTime.UtcNow,
                    IdempotencyKey = "rebate-method-switch", RebateConcurrencyToken = rebate.ConcurrencyToken
                }, Actor));
        Assert.Contains("same method", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Entering a commission or a rebate is the whole workflow: no submission, no approval, and no
    /// proof file. It is pending payment immediately, and paying it is the only thing left to do.
    /// </summary>
    [Fact]
    public async Task EnteringACommissionOrRebate_NeedsNothingElse_BeforeItCanBePaid()
    {
        await using var harness = await Harness.Create();
        var created = await harness.Service.CreateCommissionAsync(harness.BookingId, Direct(harness.PartnerId, rate: 2m), Actor);
        var commission = Assert.Single(created.Commissions);
        Assert.True(commission.IsManual);
        Assert.Empty(commission.Evidence);
        Assert.Equal(BookingCommissionStatus.Pending, commission.Status);

        var paid = await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id,
            Payout(harness.AccountId, commission.FinalAmount, commission.ConcurrencyToken, "straight-to-paid"), Actor);
        Assert.Equal(BookingCommissionStatus.Paid, Assert.Single(paid.Commissions).Status);

        var rebateWorkspace = await harness.Service.CreateRebateAsync(harness.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.AgreedSalePrice, FixedAmount = 5_000m
        }, Actor);
        var rebate = Assert.Single(rebateWorkspace.Rebates);
        Assert.Empty(rebate.Evidence);
        Assert.Equal(CustomerRebateStatus.Pending, rebate.Status);

        rebateWorkspace = await harness.Service.RecordRebateDisbursementAsync(harness.BookingId, rebate.Id,
            new RecordRebateDisbursementDto
            {
                Method = CustomerRebateMethod.OutstandingBalanceReduction, Amount = rebate.FinalAmount,
                AppliedAt = DateTime.UtcNow, IdempotencyKey = "straight-to-applied",
                RebateConcurrencyToken = rebate.ConcurrencyToken
            }, Actor);
        Assert.Equal(CustomerRebateStatus.Applied, Assert.Single(rebateWorkspace.Rebates).Status);
    }

    /// <summary>A partner added from the booking screen supplies only a name and type.</summary>
    [Fact]
    public async Task AddingAPartnerWithoutACode_GeneratesASequentialDirectoryCode()
    {
        await using var harness = await Harness.Create();
        var first = await harness.Service.CreatePartnerAsync(
            new SaveThirdPartyPartnerDto { Name = "Ahmed Khan", PartnerType = "Dealer", Phone = "0300-1234567" }, Actor);
        var second = await harness.Service.CreatePartnerAsync(
            new SaveThirdPartyPartnerDto { Name = "Bilal Traders", PartnerType = "Agency" }, Actor);

        Assert.Equal("PTR-0001", first.InternalCode);
        Assert.Equal("PTR-0002", second.InternalCode);
        // A caller that supplies its own code still keeps it.
        var explicitCode = await harness.Service.CreatePartnerAsync(
            new SaveThirdPartyPartnerDto { Name = "Legacy Broker", PartnerType = "Broker", InternalCode = "legacy-9" }, Actor);
        Assert.Equal("LEGACY-9", explicitCode.InternalCode);
    }

    private static CreateBookingCommissionDto Direct(int partnerId, decimal? rate = null, decimal? fixedAmount = null) => new()
    {
        PartnerId = partnerId, IsManual = true,
        ManualCalculationType = rate.HasValue ? FinancialCalculationType.Percentage : FinancialCalculationType.FixedAmount,
        ManualCalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
        ManualPercentageRate = rate, ManualFixedAmount = fixedAmount,
    };

    private sealed class NoopAttachmentStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static CommissionStatusChangeDto Change(BookingCommissionDto commission, BookingCommissionStatus status,
        string? reason = null, decimal? approved = null) => new()
    {
        TargetStatus = status, Reason = reason, ConcurrencyToken = commission.ConcurrencyToken
    };

    private static RecordCommissionPayoutDto Payout(int accountId, decimal amount, string concurrencyToken,
        string key = "payout") => new()
    {
        FinanceAccountId = accountId, Amount = amount, PaymentDate = DateTime.UtcNow,
        PaymentMethod = PaymentMethod.Cash, IdempotencyKey = key, CommissionConcurrencyToken = concurrencyToken
    };

    private static RebateStatusChangeDto RebateChange(CustomerRebateDto rebate, CustomerRebateStatus status,
        decimal? approved = null, string? reason = null) => new()
    {
        TargetStatus = status, Reason = reason, ConcurrencyToken = rebate.ConcurrencyToken
    };

    private static CommissionRule Rule(string name, int priority, decimal? rate = null, decimal? fixedAmount = null,
        int? partnerId = null) => new()
    {
        Name = name, IsActive = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), Priority = priority,
        PartnerId = partnerId, CalculationType = rate.HasValue ? FinancialCalculationType.Percentage : FinancialCalculationType.FixedAmount,
        PercentageRate = rate, FixedAmount = fixedAmount, CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
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

        // A commission is payable as soon as it exists — there is no approval ladder to walk.
        public Task<BookingCommissionDto> MakePayable() => CreateRuleCommission();

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
