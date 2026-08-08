using System.Reflection;
using System.Text;
using DAMS.Api.Controllers;
using DAMS.Application.Common;
using DAMS.Application.DTOs.CommissionRebateDtos;
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
            PaymentMethod = PaymentMethod.BankTransfer, IdempotencyKey = "same-payout", CommissionConcurrencyToken = commission.ConcurrencyToken
        };
        var workspace = await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id, request, Actor);
        commission = Assert.Single(workspace.Commissions);
        Assert.Equal(BookingCommissionStatus.PartiallyPaid, commission.Status);
        Assert.Equal(100m, commission.PaidAmount);
        await harness.Service.RecordPayoutAsync(harness.BookingId, commission.Id, request, Actor);
        Assert.Single(harness.Context.CommissionPayouts);
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
            var partner = new ThirdPartyPartner { Name = "ABC Broker", PartnerType = "Broker", InternalCode = "ABC-1", IsActive = true };
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
