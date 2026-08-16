using DAMS.Application.DTOs.LeadDtos;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// A lead's phone number became optional so that leads from ad platforms — which frequently
/// arrive without one — are not lost or given invented numbers.
///
/// The risk that change introduced is in duplicate detection, where a null compared with a
/// null would match. Most of what follows guards that.
/// </summary>
public class LeadPhoneOptionalTests
{
    private static LeadIntakeDto External(
        string externalId, string? phone = null, string? email = null, string firstName = "Ali") =>
        new()
        {
            FirstName = firstName,
            Phone = phone,
            Email = email,
            SourceCode = "facebook",
            ExternalProvider = "meta",
            ExternalLeadId = externalId,
            AllowDuplicate = true
        };

    [Fact]
    public async Task ExternalLead_WithNoContactDetails_IsStillCaptured()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var result = await h.Leads.IngestAsync(External("meta-1"), actor: null);

        Assert.NotNull(result.Lead);

        var lead = await h.Db.Leads.SingleAsync(l => l.Id == result.Lead!.Id);
        // Nothing invented: no placeholder number, no empty string standing in for one.
        Assert.Null(lead.Phone);
        Assert.Null(lead.NormalizedPhone);
        Assert.Equal("meta", lead.ExternalProvider);
        Assert.Equal("meta-1", lead.ExternalLeadId);
    }

    [Fact]
    public async Task ManualLead_WithNoContactMethodAtAll_IsRejected()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var dto = LeadTestHarness.Intake(phone: null, email: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Leads.IngestAsync(dto, h.Admin));

        Assert.Contains("at least one way to reach", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManualLead_WithOnlyAnEmail_IsAccepted()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var result = await h.Leads.IngestAsync(
            LeadTestHarness.Intake(phone: null, email: "only@example.com"), h.Admin);

        Assert.NotNull(result.Lead);
        Assert.Null(result.Lead!.Phone);
        Assert.Equal("only@example.com", result.Lead.Email);
    }

    [Fact]
    public async Task ManualLead_WithOnlyAWhatsappNumber_IsAccepted()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var dto = LeadTestHarness.Intake(phone: null, email: null);
        dto.WhatsappNumber = "0301-9876543";

        var result = await h.Leads.IngestAsync(dto, h.Admin);

        Assert.NotNull(result.Lead);
        Assert.Null(result.Lead!.Phone);
    }

    [Fact]
    public async Task TwoLeadsWithoutPhoneNumbers_AreNotTreatedAsTheSamePerson()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        // The regression this whole change risked: comparing null with null in the duplicate
        // query would have collapsed every phoneless lead into the first one.
        var first = await h.Leads.IngestAsync(External("meta-1", firstName: "Ali"), actor: null);
        var second = await h.Leads.IngestAsync(External("meta-2", firstName: "Bilal"), actor: null);

        Assert.NotNull(first.Lead);
        Assert.NotNull(second.Lead);
        Assert.NotEqual(first.Lead!.Id, second.Lead!.Id);
        Assert.False(second.EnrichedExisting);
        Assert.Equal(2, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task PhonelessLeads_SharingAnEmail_StillEnrichTheSameLead()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var first = await h.Leads.IngestAsync(External("meta-1", email: "ali@example.com"), actor: null);
        var second = await h.Leads.IngestAsync(External("meta-2", email: "ali@example.com"), actor: null);

        Assert.True(second.EnrichedExisting);
        Assert.Equal(first.Lead!.Id, second.Lead!.Id);

        // One person, one lead, two immutable receipts.
        Assert.Equal(1, await h.Db.Leads.CountAsync());
        Assert.Equal(2, await h.Db.LeadExternalSubmissions.CountAsync());
    }

    [Fact]
    public async Task PakistaniPhoneNormalization_IsUnchanged()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        await h.Leads.IngestAsync(LeadTestHarness.Intake(phone: "0300-1234567", email: "a@example.com"), h.Admin);

        var second = await h.Leads.IngestAsync(
            LeadTestHarness.Intake(phone: "+92 300 1234567", email: "b@example.com"), h.Admin);

        Assert.True(second.IsDuplicate);
        Assert.Equal("phone", second.Match!.MatchedOn);
    }

    [Fact]
    public async Task ARepeatEnquiry_BackfillsThePhoneNumberOntoAPhonelessLead()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var first = await h.Leads.IngestAsync(External("meta-1", email: "ali@example.com"), actor: null);
        Assert.Null(first.Lead!.Phone);

        await h.Leads.IngestAsync(
            External("meta-2", phone: "0300-1234567", email: "ali@example.com"), actor: null);

        var lead = await h.Db.Leads.SingleAsync();
        Assert.Equal("0300-1234567", lead.Phone);
        Assert.Equal("3001234567", lead.NormalizedPhone);
    }

    [Fact]
    public async Task AnUnusablePhoneNumber_IsNeverStoredOnAnExternalLead()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var result = await h.Leads.IngestAsync(External("meta-1", phone: "123"), actor: null);

        var lead = await h.Db.Leads.SingleAsync(l => l.Id == result.Lead!.Id);
        Assert.Null(lead.Phone);
        Assert.Null(lead.NormalizedPhone);
    }

    [Fact]
    public async Task ConvertingAPhonelessLead_IsRefusedWithAClearReason()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var created = await h.Leads.IngestAsync(External("meta-1", email: "ali@example.com"), actor: null);
        var leadId = created.Lead!.Id;

        // A customer record cannot exist without a phone number, so conversion must ask for
        // one rather than quietly writing an empty string that follows the customer forever.
        var lead = await h.Db.Leads.SingleAsync(l => l.Id == leadId);
        lead.Stage = Domain.Enums.LeadStage.BookingPending;
        await h.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Leads.ConvertAsync(leadId, new ConvertLeadDto
            {
                UnitId = h.UnitId,
                AgreedSalePrice = 1_000_000m,
                BookingAmountRequired = 100_000m
            }, h.Admin));

        Assert.Contains("phone number", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
