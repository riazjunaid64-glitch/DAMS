using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// One person, however their phone was written, is one customer. Conversion, booking and lead
/// intake all match on the national number, and an email that already belongs to someone else
/// is not a way to attach a different CNIC to them.
/// </summary>
public sealed class CustomerPhoneMatchTests
{
    [Theory]
    [InlineData("+92 300 1234567")]
    [InlineData("00923001234567")]
    [InlineData("923001234567")]
    public async Task Converting_reuses_the_customer_when_the_phone_is_written_differently(string leadPhone)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        // No email on the customer: the lead's address must not be what joins them.
        var existing = await SeedCustomerAsync(h, "Amina Shah", "03001234567", email: null);

        var leadId = await h.CreateWorkedLeadAsync(leadPhone);
        var detail = Assert.IsType<LeadDetailResponseDto>(await h.Leads.GetByIdAsync(leadId, h.Admin));
        Assert.Equal(existing, detail.MatchedCustomerId);
        Assert.Equal("Amina Shah", detail.MatchedCustomerName);
        Assert.Empty(detail.PhoneMatches);

        var before = await h.Db.Customers.CountAsync();
        // No customer id: the server has to recognise the number itself.
        var result = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        Assert.False(result.CustomerWasCreated);
        Assert.Equal(existing, result.CustomerId);
        Assert.Equal(before, await h.Db.Customers.CountAsync());
        Assert.Equal(existing, (await h.LoadLeadAsync(leadId)).ConvertedCustomerId);
    }

    [Fact]
    public async Task Converting_without_customerId_rejects_a_phone_match_when_the_email_differs()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var existing = await SeedCustomerAsync(h, "Amina Shah", "03001234567", "a@example.com");

        var leadId = await h.CreateWorkedLeadAsync("+923001234567");
        // CreateWorkedLeadAsync gives the lead its own email; keep that so FindOrCreate sees a conflict.
        var lead = await h.Db.Leads.SingleAsync(l => l.Id == leadId);
        Assert.NotEqual("a@example.com", lead.Email);

        var beforeCustomers = await h.Db.Customers.CountAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin));

        Assert.Contains("email is different", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(beforeCustomers, await h.Db.Customers.CountAsync());
        Assert.Equal(0, await h.Db.Bookings.CountAsync());
        Assert.NotEqual(LeadStage.Won, (await h.LoadLeadAsync(leadId)).Stage);
        Assert.Null((await h.LoadLeadAsync(leadId)).ConvertedCustomerId);
        Assert.Equal(existing, (await h.Db.Customers.AsNoTracking().SingleAsync(c => c.Email == "a@example.com")).Id);
    }

    [Fact]
    public async Task Salesperson_converts_a_phone_match_without_sending_customerId()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        // Compatible contact details: FindOrCreate reuses the row. Sending CustomerId would be refused.
        var existing = await SeedCustomerAsync(h, "Amina Shah", "03001234567", email: null);

        var leadId = await h.CreateWorkedLeadAsync("+92 300 1234567");
        var result = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Sales);

        Assert.Equal(existing, result.CustomerId);
        Assert.False(result.CustomerWasCreated);
        Assert.Equal(1, await h.Db.Bookings.CountAsync());
        Assert.Equal(LeadStage.Won, (await h.LoadLeadAsync(leadId)).Stage);
    }

    [Fact]
    public async Task Converting_asks_to_choose_when_two_customers_share_the_phone()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var amina = await SeedCustomerAsync(h, "Amina Shah", "03001234567", "amina@example.com");
        var other = await SeedCustomerAsync(h, "Other Person", "+92 300 1234567", "other@example.com");

        var leadId = await h.CreateWorkedLeadAsync("+92 300 1234567");
        var detail = Assert.IsType<LeadDetailResponseDto>(await h.Leads.GetByIdAsync(leadId, h.Admin));
        Assert.Null(detail.MatchedCustomerId);
        Assert.Equal(2, detail.PhoneMatches.Count);
        Assert.Contains(detail.PhoneMatches, c => c.Id == amina);
        Assert.Contains(detail.PhoneMatches, c => c.Id == other);

        var before = await h.Db.Customers.CountAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin));

        Assert.Contains("choose the customer", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await h.Db.Customers.CountAsync());
        Assert.NotEqual(LeadStage.Won, (await h.LoadLeadAsync(leadId)).Stage);

        var chosen = await h.Leads.ConvertAsync(leadId,
            new ConvertLeadDto { UnitId = h.UnitId, CustomerId = amina }, h.Admin);
        Assert.Equal(amina, chosen.CustomerId);
        Assert.False(chosen.CustomerWasCreated);
        Assert.Equal(before, await h.Db.Customers.CountAsync());
    }

    [Fact]
    public async Task A_phone_match_does_not_grant_portal_ownership()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var existing = await SeedCustomerAsync(h, "Amina Shah", "03001234567", email: null);

        var leadId = await h.CreateWorkedLeadAsync("+92 300 1234567");
        h.Db.BookingRequests.Add(new BookingRequest
        {
            UnitId = h.UnitId,
            LeadId = leadId,
            UserId = h.ClientUserId,
            FullName = "Amina Shah",
            Phone = "+92 300 1234567",
            Email = "typed-something-else@example.com",
            CNIC = "42101-0000000-1",
            Address = "Karachi",
            Status = BookingRequestStatus.Pending
        });
        await h.Db.SaveChangesAsync();

        var result = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        Assert.Equal(existing, result.CustomerId);
        Assert.False(result.CustomerWasCreated);
        Assert.Null((await h.Db.Customers.AsNoTracking().SingleAsync(c => c.Id == existing)).UserId);
        Assert.Empty(await h.Db.CustomerAccountLinkAudits.ToListAsync());
    }

    [Fact]
    public async Task A_blocked_customer_is_not_duplicated_when_the_phone_is_written_differently()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var existing = await SeedCustomerAsync(h, "Blocked Buyer", "03001234567", email: null);
        var row = await h.Db.Customers.SingleAsync(c => c.Id == existing);
        row.Status = CustomerStatus.Blocked;
        await h.Db.SaveChangesAsync();

        var leadId = await h.CreateWorkedLeadAsync("+92 300 1234567");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin));

        Assert.Contains("blocked", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await h.Db.Customers.CountAsync());
        Assert.NotEqual(LeadStage.Won, (await h.LoadLeadAsync(leadId)).Stage);
    }

    [Fact]
    public async Task FindOrCreate_matches_the_national_number_and_keeps_the_display_phone()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);

        var first = await service.FindOrCreateCustomerAsync(
            "Amina Shah", "03001234567", cnic: null, email: null, address: null,
            CustomerSource.WalkIn, sourceNotes: null, h.AdminUserId);
        var stored = await h.Db.Customers.AsNoTracking().SingleAsync();
        Assert.Equal("03001234567", stored.Phone);
        Assert.Equal("3001234567", stored.NormalizedPhone);
        Assert.Null(stored.UserId);

        var second = await service.FindOrCreateCustomerAsync(
            "Amina Shah", "+92 300 1234567", cnic: null, email: "other@example.com", address: null,
            CustomerSource.Phone, sourceNotes: null, h.AdminUserId);

        Assert.Equal(first.CustomerId, second.CustomerId);
        Assert.False(second.WasCreated);
        Assert.Equal(1, await h.Db.Customers.CountAsync());
        Assert.Null((await h.Db.Customers.AsNoTracking().SingleAsync()).UserId);
    }

    [Fact]
    public async Task An_email_match_with_a_different_cnic_is_rejected()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);
        await service.FindOrCreateCustomerAsync(
            "Amina Shah", "03001111111", "11111-1111111-1", "x@y.com", address: null,
            CustomerSource.WalkIn, sourceNotes: null, h.AdminUserId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.FindOrCreateCustomerAsync(
                "Other Person", "03002222222", "22222-2222222-2", "x@y.com", address: null,
                CustomerSource.WalkIn, sourceNotes: null, h.AdminUserId));

        Assert.Contains("This email belongs to another customer", error.Message);
        Assert.Equal(1, await h.Db.Customers.CountAsync());
    }

    [Fact]
    public async Task An_email_match_with_no_incoming_cnic_still_reuses_the_customer()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);
        var existing = await service.FindOrCreateCustomerAsync(
            "Amina Shah", "03001111111", "11111-1111111-1", "x@y.com", address: null,
            CustomerSource.WalkIn, sourceNotes: null, h.AdminUserId);

        var again = await service.FindOrCreateCustomerAsync(
            "Amina Shah", "03002222222", cnic: null, email: "x@y.com", address: null,
            CustomerSource.WalkIn, sourceNotes: null, h.AdminUserId);

        Assert.Equal(existing.CustomerId, again.CustomerId);
        Assert.False(again.WasCreated);
        Assert.Equal(1, await h.Db.Customers.CountAsync());
    }

    [Fact]
    public async Task Updating_the_phone_rewrites_the_national_number()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);
        var created = await service.CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Amina Shah",
            Phone = "03001234567",
            Source = CustomerSource.WalkIn
        }, h.AdminUserId);

        await service.UpdateCustomerAsync(created.Id, new UpdateCustomerDto { Phone = "+92 300 9998887" });

        var stored = await h.Db.Customers.AsNoTracking().SingleAsync();
        Assert.Equal("+923009998887", stored.Phone);
        Assert.Equal("3009998887", stored.NormalizedPhone);
    }

    [Fact]
    public async Task Intake_reports_the_customer_when_only_the_phone_spelling_differs()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var existing = await SeedCustomerAsync(h, "Amina Shah", "03001234567", "amina@example.com");

        var reported = await h.Leads.IngestAsync(LeadTestHarness.Intake(
            firstName: "Amina", phone: "+92 300 1234567", email: "different@example.com"), h.Admin);

        Assert.False(reported.IsDuplicate);
        Assert.NotNull(reported.Lead);
        Assert.Equal(existing, reported.Match!.CustomerId);
        Assert.Equal("phone", reported.Match.MatchedOn);
    }

    [Fact]
    public async Task Intake_does_not_name_one_customer_when_several_share_the_phone()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await SeedCustomerAsync(h, "Amina Shah", "03001234567", "amina@example.com");
        await SeedCustomerAsync(h, "Other Person", "00923001234567", "other@example.com");

        var ambiguous = await h.Leads.IngestAsync(LeadTestHarness.Intake(
            firstName: "Someone", phone: "923001234567", email: "third@example.com"), h.Admin);

        Assert.False(ambiguous.IsDuplicate);
        Assert.NotNull(ambiguous.Lead);
        Assert.Null(ambiguous.Match);
    }

    private static async Task<int> SeedCustomerAsync(LeadTestHarness h, string name, string phone, string? email)
    {
        var created = await new CustomerService(h.Db).CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = name,
            Phone = phone,
            Email = email,
            Source = CustomerSource.WalkIn
        }, h.AdminUserId);
        return created.Id;
    }
}
