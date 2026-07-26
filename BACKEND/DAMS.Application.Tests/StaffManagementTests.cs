using DAMS.Application.Common;
using DAMS.Application.DTOs.EmployeeDtos;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class StaffManagementTests
{
    [Fact]
    public async Task Admin_can_create_secure_sales_login_linked_to_employee_and_team()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new StaffManagementService(h.Db);

        var created = await service.CreateAsync(new CreateStaffAccountDto
        {
            FullName = "Nadia Sales",
            Email = "NADIA@EXAMPLE.COM",
            TemporaryPassword = "Temporary#123",
            Role = LeadRoles.Employee,
            TeamId = h.TeamId,
            JobTitle = "Sales Executive",
            Department = "Sales",
            Phone = "03009998888"
        });

        Assert.Equal("nadia@example.com", created.Email);
        Assert.Equal(LeadRoles.Employee, created.Role);
        Assert.Equal(h.TeamId, created.TeamId);
        Assert.True(created.CanOwnLeads);

        var user = await h.Db.Users.SingleAsync(u => u.UserId == created.UserId);
        Assert.NotEqual("Temporary#123", user.Password);
        Assert.True(BCrypt.Net.BCrypt.Verify("Temporary#123", user.Password));
        Assert.Equal(created.EmployeeId,
            await h.Db.Employees.Where(e => e.UserId == user.UserId).Select(e => e.Id).SingleAsync());
    }

    [Fact]
    public async Task Existing_customer_login_can_be_promoted_only_through_admin_staff_workflow()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new StaffManagementService(h.Db);
        var client = await h.Db.Users.SingleAsync(u => u.UserId == h.ClientUserId);

        var created = await service.CreateAsync(new CreateStaffAccountDto
        {
            ExistingUserId = client.UserId,
            FullName = client.FullName,
            Email = client.Email,
            Role = LeadRoles.Manager,
            TeamId = h.TeamId,
            JobTitle = "Sales Manager",
            Department = "Sales",
            Phone = "03001110000"
        });

        Assert.Equal(LeadRoles.Manager, created.Role);
        Assert.Equal(client.UserId, created.UserId);
        Assert.Equal(3, await h.Db.Users.Where(u => u.UserId == client.UserId).Select(u => u.RoleId).SingleAsync());
    }

    [Fact]
    public async Task Duplicate_login_or_employee_link_is_rejected()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new StaffManagementService(h.Db);

        var emailError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new CreateStaffAccountDto
            {
                FullName = "Duplicate",
                Email = "sales@dams.test",
                TemporaryPassword = "Temporary#123",
                Role = LeadRoles.Employee,
                JobTitle = "Sales",
                Department = "Sales",
                Phone = "03002223333"
            }));
        Assert.Contains("already exists", emailError.Message);

        var linkError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new CreateStaffAccountDto
            {
                ExistingUserId = h.SalesUserId,
                FullName = "Duplicate",
                Email = "sales@dams.test",
                Role = LeadRoles.Employee,
                JobTitle = "Sales",
                Department = "Sales",
                Phone = "03002223333"
            }));
        Assert.Contains("already linked", linkError.Message);
    }

    [Fact]
    public async Task Directory_respects_manager_team_employee_team_and_customer_isolation()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new StaffManagementService(h.Db);

        var managerRows = await service.GetDirectoryAsync(h.Manager);
        Assert.Contains(managerRows, e => e.EmployeeId == h.SalesEmployeeId);
        Assert.DoesNotContain(managerRows, e => e.EmployeeId == h.OtherSalesEmployeeId);

        var employeeRows = await service.GetDirectoryAsync(h.Sales);
        Assert.Contains(employeeRows, e => e.EmployeeId == h.ManagerEmployeeId);
        Assert.DoesNotContain(employeeRows, e => e.EmployeeId == h.OtherSalesEmployeeId);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => service.GetDirectoryAsync(h.Client));
    }

    [Fact]
    public async Task Security_change_revokes_refresh_session()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new StaffManagementService(h.Db);
        var user = await h.Db.Users.SingleAsync(u => u.UserId == h.SalesUserId);
        user.RefreshToken = "old-session";
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);
        await h.Db.SaveChangesAsync();

        var updated = await service.UpdateAsync(h.SalesEmployeeId, new UpdateStaffAccountDto
        {
            Role = LeadRoles.Manager,
            TeamId = h.TeamId,
            NewTemporaryPassword = "Changed#123"
        });

        h.Db.ChangeTracker.Clear();
        user = await h.Db.Users.SingleAsync(u => u.UserId == h.SalesUserId);
        Assert.Equal(LeadRoles.Manager, updated.Role);
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiresAt);
        Assert.True(BCrypt.Net.BCrypt.Verify("Changed#123", user.Password));
    }

    [Fact]
    public async Task Lead_list_supports_unit_and_inactivity_filters()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var id = await h.CreateLeadAsync(new LeadIntakeDto
        {
            FirstName = "Inactive",
            Phone = "03007778888",
            SourceCode = "manual",
            InterestedProjectId = h.ProjectId,
            InterestedUnitId = h.UnitId
        });

        var lead = await h.Db.Leads.SingleAsync(l => l.Id == id);
        lead.LastActivityAt = DateTime.UtcNow.AddDays(-8);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var result = await h.Leads.GetLeadsAsync(new LeadFilterDto
        {
            UnitId = h.UnitId,
            InactiveOnly = true
        }, h.Admin);

        Assert.Single(result.Items);
        Assert.Equal(id, result.Items[0].Id);
    }

    [Fact]
    public async Task Follow_up_reschedule_updates_next_action_and_records_old_and_new_due_dates()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var originalDue = DateTime.UtcNow.AddDays(1);
        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            AssignedEmployeeId = h.SalesEmployeeId,
            Type = LeadFollowUpType.Call,
            Title = "Call after quotation",
            DueAt = originalDue
        }, h.Sales);
        var newDue = DateTime.UtcNow.AddDays(2);

        var updated = await h.FollowUps.RescheduleAsync(followUp.Id, new RescheduleLeadFollowUpDto
        {
            DueAt = newDue,
            Reason = "Customer requested a later call."
        }, h.Sales);

        Assert.Equal(newDue, updated.DueAt, TimeSpan.FromSeconds(1));
        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(newDue, lead.NextActionAt!.Value, TimeSpan.FromSeconds(1));
        var activity = (await h.TimelineAsync(leadId)).Single(a => a.Type == LeadActivityType.FollowUpRescheduled);
        Assert.Contains("Customer requested", activity.Notes);
        Assert.NotNull(activity.PreviousValue);
        Assert.NotNull(activity.NewValue);
    }

    [Fact]
    public async Task Employee_captured_manual_lead_is_automatically_owned_and_remains_visible()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var created = await h.Leads.IngestAsync(new LeadIntakeDto
        {
            FirstName = "Walk In",
            Phone = "03006665555",
            SourceCode = "walk_in"
        }, h.Sales);

        Assert.NotNull(created.Lead);
        Assert.Equal(h.SalesEmployeeId, created.Lead!.AssignedEmployeeId);
        Assert.Equal(h.TeamId, created.Lead.AssignedTeamId);
        Assert.Equal(LeadStage.FirstContactPending, created.Lead.Stage);
        Assert.NotNull(await h.Leads.GetByIdAsync(created.Lead.Id, h.Sales));
    }

    [Fact]
    public async Task Team_manager_must_keep_an_active_manager_or_admin_login_role()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var staff = new StaffManagementService(h.Db);

        var roleError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            staff.UpdateAsync(h.ManagerEmployeeId, new UpdateStaffAccountDto
            {
                Role = LeadRoles.Employee,
                TeamId = h.TeamId,
                Status = EmployeeStatus.Active
            }));
        Assert.Contains("Reassign", roleError.Message);

        var configError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Configuration.UpdateTeamAsync(h.TeamId, new SaveTeamDto
            {
                Name = "North Sales",
                ManagerEmployeeId = h.SalesEmployeeId,
                IsActive = true
            }, h.Admin));
        Assert.Contains("Sales Manager", configError.Message);
    }
}
