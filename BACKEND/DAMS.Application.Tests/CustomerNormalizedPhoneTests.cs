using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Services;
using DAMS.Domain.Common;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class CustomerNormalizedPhoneTests
{
    [Theory]
    [InlineData("03001234567", "3001234567")]
    [InlineData("+92 300 1234567", "3001234567")]
    [InlineData("00923001234567", "3001234567")]
    [InlineData("923001234567", "3001234567")]
    public void ContactNormalization_MapsEquivalentPakistaniNumbers(string input, string expected) =>
        Assert.Equal(expected, ContactNormalization.NormalizePhone(input));

    [Fact]
    public async Task SaveChanges_StampsNormalizedPhone_OnExistingCustomerRows()
    {
        await using var db = LeadTestHarness.CreateContext();
        db.Customers.Add(new Customer { FullName = "Legacy", Phone = "0300-1112233" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.Customers.AsNoTracking().SingleAsync();
        Assert.Equal("3001112233", stored.NormalizedPhone);
    }

    [Fact]
    public async Task ConvertingLead_WithAlternatePhoneFormat_ReusesExistingCustomer()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.Customers.Add(new Customer
        {
            FullName = "Returning Buyer",
            Phone = "03001234567",
        });
        await h.Db.SaveChangesAsync();

        var leadId = await h.CreateWorkedLeadAsync("+92 300 1234567");
        var before = await h.Db.Customers.CountAsync();

        var result = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        Assert.Equal(before, await h.Db.Customers.CountAsync());
        Assert.False(result.CustomerWasCreated);
        Assert.Equal(
            (await h.Db.Customers.AsNoTracking().SingleAsync(c => c.Phone == "03001234567")).Id,
            result.CustomerId);
    }

    [Theory]
    [InlineData("00923001234567")]
    [InlineData("923001234567")]
    public async Task ConvertingLead_WithInternationalPrefixes_ReusesExistingCustomer(string leadPhone)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.Customers.Add(new Customer { FullName = "Returning Buyer", Phone = "03001234567" });
        await h.Db.SaveChangesAsync();

        var leadId = await h.CreateWorkedLeadAsync(leadPhone);
        var result = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        Assert.False(result.CustomerWasCreated);
        Assert.Equal(1, await h.Db.Customers.CountAsync());
    }

    [Fact]
    public async Task ConvertingLead_WhenTwoCustomersShareNormalizedPhone_RequiresExplicitChoice()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.Customers.AddRange(
            new Customer { FullName = "Buyer A", Phone = "03001234567" },
            new Customer { FullName = "Buyer B", Phone = "+92 300 1234567" });
        await h.Db.SaveChangesAsync();

        var leadId = await h.CreateWorkedLeadAsync("0300-1234567");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin));

        Assert.Contains("choose the customer", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await h.Db.Customers.CountAsync());
    }

    [Fact]
    public async Task FindOrCreateCustomer_RejectsEmailMatch_WhenCnicConflicts()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.Customers.Add(new Customer
        {
            FullName = "Person A",
            Phone = "03001112233",
            Email = "shared@example.com",
            CNIC = "35202-1111111-1",
        });
        await h.Db.SaveChangesAsync();

        var customers = new CustomerService(h.Db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            customers.FindOrCreateCustomerAsync(
                "Person B",
                "03009998877",
                "35202-2222222-2",
                "shared@example.com",
                address: null,
                CustomerSource.WalkIn,
                sourceNotes: null,
                createdByUserId: h.AdminUserId));

        Assert.Contains("email belongs to another customer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
