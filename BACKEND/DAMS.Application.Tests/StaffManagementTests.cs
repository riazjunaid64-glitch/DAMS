using System.Text.RegularExpressions;
using DAMS.Application.Common;
using DAMS.Application.DTOs.Auth;
using DAMS.Application.DTOs.EmployeeDtos;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Domain.Identity;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class StaffManagementTests
{
    /// <summary>
    /// Records what staff provisioning asked of the invitation engine. The engine's own
    /// cryptography, expiry and email are proved in <see cref="StaffInvitationServiceTests"/>;
    /// what matters here is whether it was called at all, for whom, and by whom.
    /// </summary>
    private sealed class FakeInvitations : IStaffInvitationService
    {
        public List<(int UserId, int InvitedByUserId, bool Resend)> Calls { get; } = new();

        /// <summary>Set to make delivery fail the way an unreachable mail server does.</summary>
        public string? DeliveryError { get; set; }

        public int NextInvitationId { get; set; } = 1;

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);

        public Task<StaffInvitationResult> IssueAsync(
            int userId, int invitedByUserId, CancellationToken cancellationToken = default) =>
            Record(userId, invitedByUserId, resend: false);

        public Task<StaffInvitationResult> ResendAsync(
            int userId, int invitedByUserId, CancellationToken cancellationToken = default) =>
            Record(userId, invitedByUserId, resend: true);

        /// <summary>
        /// Staff provisioning has no business spending a token, so the fake fails loudly if it
        /// ever tries rather than quietly returning something plausible.
        /// </summary>
        public Task<StaffActivationResult> ActivateAsync(
            string rawToken, string chosenPassword, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Staff provisioning must never activate an account.");

        private Task<StaffInvitationResult> Record(int userId, int invitedByUserId, bool resend)
        {
            Calls.Add((userId, invitedByUserId, resend));
            var id = NextInvitationId++;
            return Task.FromResult(DeliveryError == null
                ? StaffInvitationResult.Delivered(id, ExpiresAt)
                : StaffInvitationResult.Undelivered(
                    id, ExpiresAt, StaffInvitationFailure.EmailDeliveryFailed, DeliveryError));
        }
    }

    private static CreateStaffAccountDto NewStaff(
        string name = "Nadia Sales",
        string email = "NADIA@EXAMPLE.COM",
        string role = LeadRoles.Employee,
        int? teamId = null,
        int? existingUserId = null,
        int? existingEmployeeId = null) => new()
    {
        ExistingUserId = existingUserId,
        ExistingEmployeeId = existingEmployeeId,
        FullName = name,
        Email = email,
        Role = role,
        TeamId = teamId,
        JobTitle = "Sales Executive",
        Department = "Sales",
        Phone = "03009998888"
    };

    [Fact]
    public async Task Admin_creates_a_password_less_invited_login_linked_to_employee_and_team()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        var result = await service.CreateAsync(h.Admin, NewStaff(teamId: h.TeamId));
        var created = result.Account;

        Assert.Equal("nadia@example.com", created.Email);
        Assert.Equal(LeadRoles.Employee, created.Role);
        Assert.Equal(h.TeamId, created.TeamId);
        Assert.Equal(StaffAccountAccess.Invited, created.Access);

        // The account exists but nobody — Admin included — has chosen a password for it.
        var user = await h.Db.Users.SingleAsync(u => u.UserId == created.UserId);
        Assert.Null(user.Password);
        Assert.Equal(UserAccountStatus.Invited, user.AccountStatus);
        Assert.Equal(created.EmployeeId,
            await h.Db.Employees.Where(e => e.UserId == user.UserId).Select(e => e.Id).SingleAsync());

        // Invited exactly once, for that login, by the authenticated Admin.
        var call = Assert.Single(invitations.Calls);
        Assert.Equal(user.UserId, call.UserId);
        Assert.Equal(h.AdminUserId, call.InvitedByUserId);
        Assert.False(call.Resend);
        Assert.True(result.InvitationRequired);
        Assert.True(result.InvitationSent);
    }

    [Fact]
    public async Task An_unlinked_employee_gains_an_invited_login_without_being_duplicated()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        // An employee created through normal HR management, with no DAMS access yet.
        var employee = new Employee
        {
            FullName = "Hira Support",
            JobTitle = "Coordinator",
            Department = "Sales",
            Phone = "03004445555",
            JoinDate = DateTime.UtcNow.Date,
            Status = EmployeeStatus.Active
        };
        h.Db.Employees.Add(employee);
        await h.Db.SaveChangesAsync();
        var employeeCount = await h.Db.Employees.CountAsync();

        var result = await service.CreateAsync(h.Admin, NewStaff(
            name: "Hira Support",
            email: "hira@example.com",
            teamId: h.TeamId,
            existingEmployeeId: employee.Id));

        Assert.Equal(employee.Id, result.Account.EmployeeId);
        Assert.Equal(employeeCount, await h.Db.Employees.CountAsync());
        Assert.Equal(h.TeamId, result.Account.TeamId);
        Assert.Equal(StaffAccountAccess.Invited, result.Account.Access);

        var user = await h.Db.Users.SingleAsync(u => u.Email == "hira@example.com");
        Assert.Null(user.Password);
        Assert.Equal(user.UserId, Assert.Single(invitations.Calls).UserId);
    }

    [Fact]
    public async Task An_existing_active_login_keeps_its_password_and_is_never_sent_an_activation_link()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);
        var client = await h.Db.Users.SingleAsync(u => u.UserId == h.ClientUserId);
        var passwordBefore = client.Password;
        h.Db.ChangeTracker.Clear();

        var result = await service.CreateAsync(h.Admin, NewStaff(
            name: client.FullName,
            email: client.Email,
            role: LeadRoles.Manager,
            teamId: h.TeamId,
            existingUserId: client.UserId));

        Assert.Equal(LeadRoles.Manager, result.Account.Role);
        Assert.Equal(client.UserId, result.Account.UserId);
        Assert.Equal(StaffAccountAccess.Active, result.Account.Access);

        h.Db.ChangeTracker.Clear();
        var after = await h.Db.Users.SingleAsync(u => u.UserId == client.UserId);
        Assert.Equal(passwordBefore, after.Password);
        Assert.Equal(UserAccountStatus.Active, after.AccountStatus);
        // Single role, replaced rather than accumulated: Client becomes Manager.
        Assert.Equal(3, after.RoleId);

        Assert.Empty(invitations.Calls);
        Assert.False(result.InvitationRequired);
        Assert.Null(result.InvitationExpiresAt);
    }

    [Fact]
    public async Task An_existing_invited_login_is_linked_and_reissued_an_invitation()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        var waiting = new User
        {
            FullName = "Waiting Staff",
            Email = "waiting@dams.test",
            Password = null,
            RoleId = 4,
            AccountStatus = UserAccountStatus.Invited
        };
        h.Db.Users.Add(waiting);
        await h.Db.SaveChangesAsync();

        var result = await service.CreateAsync(h.Admin, NewStaff(
            name: waiting.FullName,
            email: waiting.Email,
            teamId: h.TeamId,
            existingUserId: waiting.UserId));

        Assert.Equal(waiting.UserId, result.Account.UserId);
        Assert.Equal(StaffAccountAccess.Invited, result.Account.Access);
        Assert.True(result.InvitationRequired);

        var call = Assert.Single(invitations.Calls);
        Assert.Equal(waiting.UserId, call.UserId);
        Assert.Equal(h.AdminUserId, call.InvitedByUserId);

        h.Db.ChangeTracker.Clear();
        Assert.Null(await h.Db.Users.Where(u => u.UserId == waiting.UserId).Select(u => u.Password).SingleAsync());
    }

    [Fact]
    public async Task A_disabled_login_is_rejected_rather_than_silently_reactivated()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        var disabled = new User
        {
            FullName = "Former Staff",
            Email = "former@dams.test",
            Password = "old-hash",
            RoleId = 4,
            AccountStatus = UserAccountStatus.Disabled
        };
        h.Db.Users.Add(disabled);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            h.Admin, NewStaff(
                name: disabled.FullName,
                email: disabled.Email,
                existingUserId: disabled.UserId)));
        Assert.Contains("disabled", error.Message, StringComparison.OrdinalIgnoreCase);

        h.Db.ChangeTracker.Clear();
        var after = await h.Db.Users.SingleAsync(u => u.UserId == disabled.UserId);
        Assert.Equal(UserAccountStatus.Disabled, after.AccountStatus);
        Assert.Empty(invitations.Calls);
    }

    [Fact]
    public async Task A_failed_invitation_email_still_leaves_a_usable_invited_account()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations { DeliveryError = "The mail server did not respond." };
        var service = new StaffManagementService(h.Db, invitations);

        var result = await service.CreateAsync(h.Admin, NewStaff(teamId: h.TeamId));

        // The account is not rolled back and no fallback password is invented — the Admin is
        // simply told delivery failed so they can resend.
        Assert.True(result.InvitationRequired);
        Assert.False(result.InvitationSent);
        Assert.Equal("The mail server did not respond.", result.InvitationError);

        h.Db.ChangeTracker.Clear();
        var user = await h.Db.Users.SingleAsync(u => u.UserId == result.Account.UserId);
        Assert.Null(user.Password);
        Assert.Equal(UserAccountStatus.Invited, user.AccountStatus);
        Assert.Equal(result.Account.EmployeeId,
            await h.Db.Employees.Where(e => e.UserId == user.UserId).Select(e => e.Id).SingleAsync());
    }

    [Fact]
    public async Task Duplicate_login_or_employee_link_is_rejected()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        var emailError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            h.Admin, NewStaff(name: "Duplicate", email: "sales@dams.test")));
        Assert.Contains("already exists", emailError.Message);

        var linkError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            h.Admin, NewStaff(name: "Duplicate", email: "sales@dams.test", existingUserId: h.SalesUserId)));
        Assert.Contains("already linked", linkError.Message);

        var employeeError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            h.Admin, NewStaff(
                name: "Duplicate", email: "another@dams.test", existingEmployeeId: h.SalesEmployeeId)));
        Assert.Contains("already has a login", employeeError.Message);

        // Nothing was invited on any rejected path.
        Assert.Empty(invitations.Calls);
    }

    [Fact]
    public async Task Only_an_admin_can_provision_or_resend_staff_access()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            service.CreateAsync(h.Manager, NewStaff()));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            service.ResendInvitationAsync(h.Sales, h.SalesEmployeeId));

        Assert.Empty(invitations.Calls);
    }

    [Fact]
    public async Task Admin_can_resend_only_to_a_linked_login_that_is_still_waiting()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        // A staff member who has been invited but has not activated yet.
        var created = await service.CreateAsync(h.Admin, NewStaff(teamId: h.TeamId));
        invitations.Calls.Clear();

        var resent = await service.ResendInvitationAsync(h.Admin, created.Account.EmployeeId);

        Assert.True(resent.Issued);
        Assert.True(resent.EmailSent);
        var call = Assert.Single(invitations.Calls);
        Assert.True(call.Resend);
        Assert.Equal(created.Account.UserId, call.UserId);
        Assert.Equal(h.AdminUserId, call.InvitedByUserId);

        // The resend must not touch the account itself.
        h.Db.ChangeTracker.Clear();
        var user = await h.Db.Users.SingleAsync(u => u.UserId == created.Account.UserId);
        Assert.Equal(UserAccountStatus.Invited, user.AccountStatus);
        Assert.Null(user.Password);
    }

    [Fact]
    public async Task Resend_rejects_an_active_account_a_disabled_one_and_an_employee_with_no_login()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        var active = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResendInvitationAsync(h.Admin, h.SalesEmployeeId));
        Assert.Contains("already active", active.Message);

        var disabledUser = await h.Db.Users.SingleAsync(u => u.UserId == h.OtherSalesUserId);
        disabledUser.AccountStatus = UserAccountStatus.Disabled;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var disabled = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResendInvitationAsync(h.Admin, h.OtherSalesEmployeeId));
        Assert.Contains("disabled", disabled.Message, StringComparison.OrdinalIgnoreCase);

        var unlinked = new Employee
        {
            FullName = "No Login",
            JobTitle = "Coordinator",
            Department = "Sales",
            Phone = "03007770000",
            JoinDate = DateTime.UtcNow.Date,
            Status = EmployeeStatus.Active
        };
        h.Db.Employees.Add(unlinked);
        await h.Db.SaveChangesAsync();

        var noAccount = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResendInvitationAsync(h.Admin, unlinked.Id));
        Assert.Contains("no login account", noAccount.Message);

        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResendInvitationAsync(h.Admin, 999_999));
        Assert.Contains("not found", missing.Message);

        Assert.Empty(invitations.Calls);
    }

    [Fact]
    public async Task Admin_account_list_separates_no_account_from_invited_active_and_disabled()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        var unlinked = new Employee
        {
            FullName = "Zara NoLogin",
            JobTitle = "Coordinator",
            Department = "Sales",
            Phone = "03007771111",
            JoinDate = DateTime.UtcNow.Date,
            Status = EmployeeStatus.Active
        };
        h.Db.Employees.Add(unlinked);
        var disabledUser = await h.Db.Users.SingleAsync(u => u.UserId == h.OtherSalesUserId);
        disabledUser.AccountStatus = UserAccountStatus.Disabled;
        await h.Db.SaveChangesAsync();

        var invited = await service.CreateAsync(h.Admin, NewStaff(teamId: h.TeamId));
        var expiry = DateTime.UtcNow.AddHours(24);
        h.Db.StaffInvitations.Add(new StaffInvitation
        {
            UserId = invited.Account.UserId!.Value,
            InvitedByUserId = h.AdminUserId,
            TokenHash = "a-sha256-hash",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiry
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var accounts = await service.GetAccountsAsync();

        Assert.Equal(StaffAccountAccess.None,
            accounts.Single(a => a.EmployeeId == unlinked.Id).Access);
        Assert.Equal(StaffAccountAccess.Active,
            accounts.Single(a => a.EmployeeId == h.SalesEmployeeId).Access);
        Assert.Equal(StaffAccountAccess.Disabled,
            accounts.Single(a => a.EmployeeId == h.OtherSalesEmployeeId).Access);

        var invitedRow = accounts.Single(a => a.EmployeeId == invited.Account.EmployeeId);
        Assert.Equal(StaffAccountAccess.Invited, invitedRow.Access);
        Assert.Equal(expiry, invitedRow.InvitationExpiresAt!.Value, TimeSpan.FromSeconds(1));

        // The Admin surface never carries anything that could be used as a credential.
        var serialized = System.Text.Json.JsonSerializer.Serialize(accounts);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Directory_respects_manager_team_employee_team_and_customer_isolation()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new StaffManagementService(h.Db, new FakeInvitations());

        var managerRows = await service.GetDirectoryAsync(h.Manager);
        Assert.Contains(managerRows, e => e.EmployeeId == h.SalesEmployeeId);
        Assert.DoesNotContain(managerRows, e => e.EmployeeId == h.OtherSalesEmployeeId);

        var employeeRows = await service.GetDirectoryAsync(h.Sales);
        Assert.Contains(employeeRows, e => e.EmployeeId == h.ManagerEmployeeId);
        Assert.DoesNotContain(employeeRows, e => e.EmployeeId == h.OtherSalesEmployeeId);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => service.GetDirectoryAsync(h.Client));
    }

    [Fact]
    public async Task Role_and_team_change_still_revokes_the_refresh_session_and_leaves_the_password_alone()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new StaffManagementService(h.Db, new FakeInvitations());
        var user = await h.Db.Users.SingleAsync(u => u.UserId == h.SalesUserId);
        var passwordBefore = user.Password;
        user.RefreshToken = "old-session";
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);
        await h.Db.SaveChangesAsync();

        var updated = await service.UpdateAsync(h.SalesEmployeeId, new UpdateStaffAccountDto
        {
            Role = LeadRoles.Manager,
            TeamId = h.TeamId
        });

        h.Db.ChangeTracker.Clear();
        user = await h.Db.Users.SingleAsync(u => u.UserId == h.SalesUserId);
        Assert.Equal(LeadRoles.Manager, updated.Role);
        Assert.Equal(h.TeamId, updated.TeamId);

        // Dropping the Admin password field must not have taken session revocation with it.
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiresAt);

        // An Admin changing someone's role has no way to change their password.
        Assert.Equal(passwordBefore, user.Password);
    }

    [Fact]
    public void No_staff_management_dto_offers_an_admin_a_way_to_set_someone_elses_password()
    {
        // Reflection rather than a call site: a reintroduced field under any name would be
        // caught here even if nothing in the service read it yet.
        var fields = typeof(CreateStaffAccountDto).GetProperties()
            .Concat(typeof(UpdateStaffAccountDto).GetProperties())
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(fields, n => n.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fields, n => n.Contains("Token", StringComparison.OrdinalIgnoreCase));
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
        var staff = new StaffManagementService(h.Db, new FakeInvitations());

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
    // ── The whole staff lifecycle, end to end ───────────────────────────────────

    /// <summary>
    /// Provisioning, activation and the front door are three services that only meet in
    /// production. They agree on a login through its stored comparison key, so a staff account
    /// created without one activates successfully and then cannot be signed into at all —
    /// <see cref="AuthService.LoginAsync"/> looks the account up by NormalizedEmail and finds
    /// nothing. This walks the real path with the real services to prove it does not happen.
    /// </summary>
    [Theory]
    [InlineData("NADIA@EXAMPLE.COM", "nadia@example.com")]
    [InlineData("  Nadia@Example.Com  ", "Nadia@Example.Com")]
    public async Task A_new_staff_account_can_sign_in_after_activating_its_invitation(
        string typedEmail, string signInEmail)
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var settings = new NotificationSettingsStore(h.Db);
        await settings.SetAsync(NotificationSettingKeys.PublicBaseUrl, "https://dams.test", h.AdminUserId);
        await settings.SetAsync(NotificationSettingKeys.CompanyName, "DAMS Estates", h.AdminUserId);
        await settings.SetAsync(NotificationSettingKeys.AppName, "DAMS", h.AdminUserId);
        await h.Db.SaveChangesAsync();

        var email = new NotificationTestHarness.FakeEmailSender();
        var invitations = new StaffInvitationService(h.Db, settings, email, h.Clock);
        var staff = new StaffManagementService(h.Db, invitations);
        var auth = new AuthService(h.Db, new FixedTokenService());

        // 1. Admin creates the staff account.
        var created = (await staff.CreateAsync(h.Admin, NewStaff(email: typedEmail, teamId: h.TeamId))).Account;

        // Whatever was typed, the account carries the same comparison key the login uses.
        h.Db.ChangeTracker.Clear();
        var provisioned = await h.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == created.UserId);
        Assert.Equal(EmailIdentity.Normalize(provisioned.Email), provisioned.NormalizedEmail);

        // 2. The invited person spends the token from their own email and chooses a password.
        const string chosen = "chosen-by-the-staff-1";
        var link = Regex.Match(email.Sent[^1].TextBody, @"https?://\S*/activate-account#token=\S+");
        Assert.True(link.Success, "No activation link was present in the invitation email.");
        var token = Uri.UnescapeDataString(link.Value.Split("#token=", StringSplitOptions.None)[1]);

        var activation = await invitations.ActivateAsync(token, chosen);
        Assert.True(activation.Activated, activation.Error);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(UserAccountStatus.Active,
            (await h.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == created.UserId)).AccountStatus);

        // 3. And can then actually sign in — the step that used to fail.
        var session = await auth.LoginAsync(new LoginRequestDto { Email = signInEmail, Password = chosen });
        Assert.NotNull(session);
        Assert.Equal("access-token", session.AccessToken);

        // The same account however the address is capitalised, and only with the real password.
        Assert.NotNull(await auth.LoginAsync(
            new LoginRequestDto { Email = " nAdIa@ExAmPlE.cOm ", Password = chosen }));
        Assert.Null(await auth.LoginAsync(
            new LoginRequestDto { Email = signInEmail, Password = "not-the-password" }));
    }

    /// <summary>
    /// Two staff accounts cannot share an address, and the check that stops it is the same
    /// comparison the login uses — otherwise a differently-cased duplicate passes provisioning
    /// and is then refused by the unique index at save time.
    /// </summary>
    [Fact]
    public async Task A_second_staff_login_cannot_take_the_same_address_in_a_different_case()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var staff = new StaffManagementService(h.Db, new FakeInvitations());

        await staff.CreateAsync(h.Admin, NewStaff(email: "nadia@example.com", teamId: h.TeamId));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            staff.CreateAsync(h.Admin, NewStaff(name: "Nadia Again", email: "NADIA@example.com")));
        Assert.Contains("already exists", error.Message);
    }

    private sealed class FixedTokenService : ITokenService
    {
        public string GenerateAccessToken(User user, string roleName) => "access-token";
        public string GenerateRefreshToken() => "refresh-token";
    }
}
