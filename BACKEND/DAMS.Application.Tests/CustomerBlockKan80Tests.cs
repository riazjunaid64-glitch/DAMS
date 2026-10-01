using DAMS.Application.Common;
using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>KAN-80: block / unblock with a reason and a log, the richer detail, and the edit call.</summary>
public sealed class CustomerBlockKan80Tests
{
    private static async Task<int> NewCustomerAsync(LeadTestHarness h, string name = "Usman Tariq", string phone = "03334412987", string? cnic = null)
    {
        var created = await new CustomerService(h.Db).CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = name, Phone = phone, CNIC = cnic, Source = CustomerSource.WalkIn
        }, h.AdminUserId);
        return created.Id;
    }

    [Fact]
    public async Task Block_needs_a_reason_and_changes_nothing_without_one()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var id = await NewCustomerAsync(h);
        var service = new CustomerService(h.Db);

        foreach (var reason in new[] { "", "   " })
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.BlockCustomerAsync(id, reason, h.AdminUserId));

        Assert.Equal(CustomerStatus.Active, (await h.Db.Customers.AsNoTracking().SingleAsync(c => c.Id == id)).Status);
        Assert.Empty(h.Db.CustomerStatusLogs);
    }

    [Fact]
    public async Task Block_records_reason_person_and_time_and_writes_one_log_row()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var id = await NewCustomerAsync(h);
        var service = new CustomerService(h.Db);

        var blocked = await service.BlockCustomerAsync(id, "  Cheque bounced twice  ", h.AdminUserId);

        Assert.Equal(CustomerStatus.Blocked, blocked.Status);
        Assert.Equal("Cheque bounced twice", blocked.BlockedReason);
        Assert.NotNull(blocked.BlockedAt);
        Assert.False(string.IsNullOrEmpty(blocked.BlockedByName));
        var row = Assert.Single(h.Db.CustomerStatusLogs);
        Assert.Equal((id, CustomerStatusAction.Blocked, "Cheque bounced twice", (int?)h.AdminUserId), (row.CustomerId, row.Action, row.Reason, row.ByUserId));
    }

    [Fact]
    public async Task Blocking_twice_is_refused_and_does_not_log_twice()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var id = await NewCustomerAsync(h);
        var service = new CustomerService(h.Db);
        await service.BlockCustomerAsync(id, "First reason", h.AdminUserId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BlockCustomerAsync(id, "Second reason", h.AdminUserId));

        Assert.Single(h.Db.CustomerStatusLogs);
        Assert.Equal("First reason", (await h.Db.Customers.AsNoTracking().SingleAsync(c => c.Id == id)).BlockedReason);
    }

    [Fact]
    public async Task Unblock_clears_the_block_fields_and_keeps_the_history_in_the_log()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var id = await NewCustomerAsync(h);
        var service = new CustomerService(h.Db);
        await service.BlockCustomerAsync(id, "Cheque bounced twice", h.AdminUserId);

        var unblocked = await service.UnblockCustomerAsync(id, h.AdminUserId);

        Assert.Equal(CustomerStatus.Active, unblocked.Status);
        Assert.Null(unblocked.BlockedReason);
        Assert.Null(unblocked.BlockedAt);
        Assert.Null(unblocked.BlockedByName);
        var stored = await h.Db.Customers.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Null(stored.BlockedByUserId);
        var log = await h.Db.CustomerStatusLogs.AsNoTracking().OrderBy(l => l.Id).ToListAsync();
        Assert.Equal([CustomerStatusAction.Blocked, CustomerStatusAction.Unblocked], log.Select(l => l.Action));
        Assert.Equal("Cheque bounced twice", log[0].Reason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnblockCustomerAsync(id, h.AdminUserId));
    }

    [Fact]
    public async Task The_status_log_is_append_only()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var id = await NewCustomerAsync(h);
        await new CustomerService(h.Db).BlockCustomerAsync(id, "Reason", h.AdminUserId);

        var row = await h.Db.CustomerStatusLogs.SingleAsync();
        row.Reason = "Rewritten";
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Db.SaveChangesAsync());
        h.Db.ChangeTracker.Clear();
        h.Db.CustomerStatusLogs.Remove(await h.Db.CustomerStatusLogs.SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_blocked_customer_cannot_get_a_new_booking_through_the_customer_service()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var id = await NewCustomerAsync(h, "Amina Shah", "03001230000");
        var service = new CustomerService(h.Db);
        await service.BlockCustomerAsync(id, "Cheque bounced twice", h.AdminUserId);

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.FindOrCreateCustomerAsync(
            "Amina Shah", "03001230000", null, null, null, CustomerSource.WalkIn, null, h.AdminUserId));

        Assert.Contains("blocked", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_returns_the_personal_details_blocked_fields_and_documents_needed()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new CustomerService(h.Db);
        var created = await service.CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Usman Tariq", Phone = "03334412987", FatherName = "Tariq Mehmood", Whatsapp = "03334412987",
            DateOfBirth = new DateTime(1988, 3, 14), Nationality = "Pakistani", Occupation = "Business owner",
            Notes = "Prefers calls after 5 pm.", Source = CustomerSource.WalkIn
        }, h.AdminUserId);
        await service.BlockCustomerAsync(created.Id, "Cheque bounced twice", h.AdminUserId);

        var detail = (await service.GetCustomerByIdAsync(created.Id))!;

        Assert.Equal(new DateTime(1988, 3, 14), detail.DateOfBirth);
        Assert.Equal(("Pakistani", "Business owner", "03334412987"), (detail.Nationality, detail.Occupation, detail.Whatsapp));
        Assert.Equal("Cheque bounced twice", detail.BlockedReason);
        Assert.Equal(0, detail.BookingsCount);
        Assert.True(detail.DocumentsNeeded >= 0);
    }

    [Fact]
    public async Task Edit_saves_every_form_field_including_the_personal_details()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var id = await NewCustomerAsync(h);
        var service = new CustomerService(h.Db);

        var updated = await service.UpdateCustomerAsync(id, new UpdateCustomerDto
        {
            FullName = "Usman T. Tariq", FatherName = "Tariq Mehmood", Phone = "03334412987", CNIC = "37405-1234567-1",
            Email = "usman.tariq@gmail.com", Whatsapp = " 0333 4412987 ", DateOfBirth = new DateTime(1988, 3, 14),
            Nationality = "Pakistani", Occupation = "Business owner", Address = "House 21, Taxila", Notes = "Call after 5."
        });

        Assert.Equal(("Usman T. Tariq", "0333 4412987", "Pakistani", "Business owner"),
            (updated.FullName, updated.Whatsapp, updated.Nationality, updated.Occupation));
        Assert.Equal(new DateTime(1988, 3, 14), updated.DateOfBirth);
        // Clearing a value works the same way: blank means "none".
        var cleared = await service.UpdateCustomerAsync(id, new UpdateCustomerDto { Whatsapp = "", DateOfBirth = null, Nationality = " " });
        Assert.Null(cleared.Whatsapp);
        Assert.Null(cleared.DateOfBirth);
        Assert.Null(cleared.Nationality);
        Assert.Equal("Business owner", cleared.Occupation);
    }

    [Fact]
    public async Task Edit_does_not_change_the_status_even_when_the_customer_is_blocked()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var id = await NewCustomerAsync(h);
        var service = new CustomerService(h.Db);
        await service.BlockCustomerAsync(id, "Reason", h.AdminUserId);

        var updated = await service.UpdateCustomerAsync(id, new UpdateCustomerDto { FullName = "Renamed Person" });

        Assert.Equal(CustomerStatus.Blocked, updated.Status);
        Assert.Equal("Reason", updated.BlockedReason);
    }

    [Fact]
    public async Task Edit_refuses_a_mobile_or_cnic_that_belongs_to_another_customer_comparing_digits_only()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var other = await NewCustomerAsync(h, "Other Person", "03001110000", "37405-1111111-1");
        var id = await NewCustomerAsync(h);
        var service = new CustomerService(h.Db);

        var phone = await Assert.ThrowsAsync<CustomerConflictException>(() =>
            service.UpdateCustomerAsync(id, new UpdateCustomerDto { Phone = "+92 300 1110000" }));
        Assert.Equal((other, "Other Person"), (phone.ExistingCustomerId, phone.ExistingCustomerName));

        var cnic = await Assert.ThrowsAsync<CustomerConflictException>(() =>
            service.UpdateCustomerAsync(id, new UpdateCustomerDto { CNIC = "3740511111111" }));
        Assert.Equal(other, cnic.ExistingCustomerId);

        // Keeping its own mobile and CNIC is not a clash.
        await service.UpdateCustomerAsync(id, new UpdateCustomerDto { Phone = "0333 4412987", FullName = "Usman Tariq" });
    }
}
