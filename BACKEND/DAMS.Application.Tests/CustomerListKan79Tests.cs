using DAMS.Application.Common;
using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>KAN-79: Customers list summary, documents-needed filter, search, and create conflicts.</summary>
public sealed class CustomerListKan79Tests
{
    [Fact]
    public async Task List_returns_summary_that_follows_search_but_not_the_documents_filter()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);

        var complete = await service.CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Complete Person",
            Phone = "03001110001",
            Source = CustomerSource.WalkIn
        }, h.AdminUserId);
        var needing = await service.CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Needs Docs",
            Phone = "03001110002",
            Source = CustomerSource.WalkIn
        }, h.AdminUserId);

        // Create may attach "Ask every customer" defaults. Make one customer fully complete and the
        // other still needing at least one required document.
        foreach (var requirement in await h.Db.CustomerDocumentRequirements
                     .Where(r => r.CustomerId == complete.Id || r.CustomerId == needing.Id)
                     .ToListAsync())
        {
            requirement.Status = requirement.CustomerId == complete.Id
                ? CustomerDocumentStatus.Uploaded
                : CustomerDocumentStatus.Needed;
            requirement.IsRequired = true;
        }
        if (!await h.Db.CustomerDocumentRequirements.AnyAsync(r => r.CustomerId == needing.Id))
        {
            h.Db.CustomerDocumentRequirements.Add(new CustomerDocumentRequirement
            {
                CustomerId = needing.Id,
                Name = "CNIC copy",
                IsRequired = true,
                Status = CustomerDocumentStatus.Needed,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await h.Db.SaveChangesAsync();

        var all = await service.GetCustomersAsync(new CustomerFilterDto());
        Assert.Equal(2, all.TotalCustomers);
        Assert.Equal(1, all.DocumentsNeededCount);
        Assert.Equal(2, all.TotalCount);

        var filtered = await service.GetCustomersAsync(new CustomerFilterDto { DocumentsNeededOnly = true });
        Assert.Equal(2, filtered.TotalCustomers);
        Assert.Equal(1, filtered.DocumentsNeededCount);
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal(needing.Id, Assert.Single(filtered.Items).Id);
        Assert.True(filtered.Items[0].DocumentsNeeded > 0);
        Assert.Equal(0, all.Items.Single(i => i.Id == complete.Id).DocumentsNeeded);
    }

    [Theory]
    [InlineData("0300-1110003")]
    [InlineData("0300 1110003")]
    [InlineData("03001110003")]
    [InlineData("+92 300 1110003")]
    public async Task Search_finds_a_customer_by_any_phone_spelling(string search)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);
        var created = await service.CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Phone Search",
            Phone = "03001110003",
            CNIC = "37405-1234567-1",
            Source = CustomerSource.WalkIn
        }, h.AdminUserId);

        var list = await service.GetCustomersAsync(new CustomerFilterDto { SearchTerm = search });
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(created.Id, Assert.Single(list.Items).Id);
    }

    [Theory]
    [InlineData("37405-1234567-1")]
    [InlineData("3740512345671")]
    public async Task Search_finds_a_customer_by_cnic_with_or_without_dashes(string search)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);
        var created = await service.CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Cnic Search",
            Phone = "03001110004",
            CNIC = "37405-1234567-1",
            Source = CustomerSource.WalkIn
        }, h.AdminUserId);

        var list = await service.GetCustomersAsync(new CustomerFilterDto { SearchTerm = search });
        Assert.Equal(created.Id, Assert.Single(list.Items).Id);
    }

    [Fact]
    public async Task Create_refuses_a_duplicate_phone_with_the_existing_customer()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);
        var existing = await service.CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Usman Tariq",
            Phone = "03334412987",
            Source = CustomerSource.WalkIn
        }, h.AdminUserId);

        var error = await Assert.ThrowsAsync<CustomerConflictException>(() =>
            service.CreateCustomerAsync(new CreateCustomerDto
            {
                FullName = "Someone Else",
                Phone = "+92 333 4412987",
                Source = CustomerSource.WalkIn
            }, h.AdminUserId));

        Assert.Equal("phone", error.Field);
        Assert.Equal(existing.Id, error.ExistingCustomerId);
        Assert.Equal("Usman Tariq", error.ExistingCustomerName);
        Assert.Contains("Usman Tariq", error.Message);
    }

    [Fact]
    public async Task Create_refuses_a_duplicate_cnic_comparing_digits_only()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);
        var existing = await service.CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Usman Tariq",
            Phone = "03334412988",
            CNIC = "37405-1234567-1",
            Source = CustomerSource.WalkIn
        }, h.AdminUserId);

        var error = await Assert.ThrowsAsync<CustomerConflictException>(() =>
            service.CreateCustomerAsync(new CreateCustomerDto
            {
                FullName = "Someone Else",
                Phone = "03334412989",
                CNIC = "3740512345671",
                Source = CustomerSource.WalkIn
            }, h.AdminUserId));

        Assert.Equal("cnic", error.Field);
        Assert.Equal(existing.Id, error.ExistingCustomerId);
    }

    [Fact]
    public async Task Create_stores_personal_fields_and_walk_in_source()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);
        var dob = new DateTime(1990, 5, 15);

        var created = await service.CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Hamza Iqbal",
            FatherName = "Iqbal Ahmed",
            Phone = "03346120451",
            Whatsapp = "03346120451",
            DateOfBirth = dob,
            Nationality = "Pakistani",
            Occupation = "Engineer",
            Address = "Islamabad",
            Notes = "Prefers WhatsApp",
            Source = CustomerSource.WalkIn
        }, h.AdminUserId);

        var stored = await h.Db.Customers.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.Equal(dob, stored.DateOfBirth);
        Assert.Equal("Pakistani", stored.Nationality);
        Assert.Equal("Engineer", stored.Occupation);
        Assert.Equal("03346120451", stored.Whatsapp);
        Assert.Equal("Prefers WhatsApp", stored.Notes);
        Assert.Equal(CustomerSource.WalkIn, stored.Source);
        Assert.Equal("Iqbal Ahmed", stored.FatherName);
    }

    [Fact]
    public async Task List_marks_blocked_customers()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);
        var created = await service.CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Blocked One",
            Phone = "03001110005",
            Source = CustomerSource.WalkIn
        }, h.AdminUserId);
        await service.BlockCustomerAsync(created.Id, "Cheque bounced twice", h.AdminUserId);

        var list = await service.GetCustomersAsync(new CustomerFilterDto());
        var row = Assert.Single(list.Items);
        Assert.True(row.IsBlocked);
    }
}
