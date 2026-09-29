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
        int? existingUserId = null,
        int? existingEmployeeId = null) => new()
    {
        ExistingUserId = existingUserId,
        ExistingEmployeeId = existingEmployeeId,
        FullName = name,
        Email = email,
        Role = role,
        JobTitle = "Sales Executive",
        Department = "Sales",
        Phone = "03009998888"
    };

    [Fact]
    public async Task Admin_creates_a_password_less_invited_login_linked_to_the_employee()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        var result = await service.CreateAsync(h.Admin, NewStaff());
        var created = result.Account;

        Assert.Equal("nadia@example.com", created.Email);
        Assert.Equal(LeadRoles.Employee, created.Role);
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
            existingEmployeeId: employee.Id));

        Assert.Equal(employee.Id, result.Account.EmployeeId);
        Assert.Equal(employeeCount, await h.Db.Employees.CountAsync());
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
    public async Task An_existing_active_login_is_linked_without_an_invitation_and_can_sign_in_as_staff()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var staff = new StaffManagementService(h.Db, invitations);
        var auth = new AuthService(h.Db, new FixedTokenService());

        const string password = "client-pass-1";
        const string rawRefresh = "client-refresh-token";
        var client = await h.Db.Users.SingleAsync(u => u.UserId == h.ClientUserId);
        // This client has already proved the mailbox. An Active client who has not is a
        // different case and must not keep the password.
        Assert.NotNull(client.EmailVerifiedAt);
        client.Password = BCrypt.Net.BCrypt.HashPassword(password);
        client.RefreshToken = Sha256(rawRefresh);
        client.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);
        await h.Db.SaveChangesAsync();
        var passwordBefore = client.Password;
        h.Db.ChangeTracker.Clear();

        var result = await staff.CreateAsync(h.Admin, NewStaff(
            name: "Client Person",
            email: "client@dams.test",
            role: LeadRoles.Employee,
            existingUserId: h.ClientUserId));

        Assert.Equal(h.ClientUserId, result.Account.UserId);
        Assert.Equal(LeadRoles.Employee, result.Account.Role);
        Assert.Equal(StaffAccountAccess.Active, result.Account.Access);
        Assert.False(result.InvitationRequired);
        Assert.Empty(invitations.Calls);

        h.Db.ChangeTracker.Clear();
        var after = await h.Db.Users.Include(u => u.Role).SingleAsync(u => u.UserId == h.ClientUserId);
        Assert.Equal(passwordBefore, after.Password);
        Assert.Null(after.RefreshToken);
        Assert.Null(after.RefreshTokenExpiresAt);
        Assert.Equal(UserAccountStatus.Active, after.AccountStatus);
        Assert.Equal(LeadRoles.Employee, after.Role.Role_name);
        Assert.Equal(EmployeeStatus.Active,
            await h.Db.Employees.Where(e => e.UserId == h.ClientUserId).Select(e => e.Status).SingleAsync());

        // The refresh token from the client session must not mint a staff access token.
        Assert.Null(await auth.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = rawRefresh }));
        var session = await auth.LoginAsync(new LoginRequestDto { Email = "client@dams.test", Password = password });
        Assert.NotNull(session);
        Assert.Null(await auth.LoginAsync(new LoginRequestDto { Email = "client@dams.test", Password = "not-the-password" }));
    }

    [Fact]
    public async Task An_unverified_active_client_becomes_an_invited_staff_login_and_loses_the_old_password()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var staff = new StaffManagementService(h.Db, invitations);
        var auth = new AuthService(h.Db, new FixedTokenService());

        const string oldPassword = "legacy-client-password";
        const string rawRefresh = "legacy-client-refresh";
        var legacy = new User
        {
            FullName = "Legacy Client",
            Email = "legacy.active@example.com",
            NormalizedEmail = EmailIdentity.Normalize("legacy.active@example.com"),
            Password = BCrypt.Net.BCrypt.HashPassword(oldPassword),
            RoleId = 2,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = null,
            RefreshToken = Sha256(rawRefresh),
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(2)
        };
        h.Db.Users.Add(legacy);
        await h.Db.SaveChangesAsync();
        h.Db.ClientEmailVerifications.Add(new ClientEmailVerification
        {
            UserId = legacy.UserId,
            TokenHash = Sha256("legacy-client-verification"),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(12)
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        // Still a customer login until an admin links it. A manager cannot take it over.
        await Assert.ThrowsAsync<LeadAuthorizationException>(() => staff.CreateAsync(
            h.Manager, NewStaff(name: legacy.FullName, email: legacy.Email, existingUserId: legacy.UserId)));
        Assert.Empty(invitations.Calls);

        var result = await staff.CreateAsync(h.Admin, NewStaff(
            name: legacy.FullName,
            email: legacy.Email,
            role: LeadRoles.Employee,
            existingUserId: legacy.UserId));

        Assert.Equal(StaffAccountAccess.Invited, result.Account.Access);
        Assert.NotEqual(StaffAccountAccess.Active, result.Account.Access);
        Assert.True(result.InvitationRequired);
        Assert.Equal(legacy.UserId, Assert.Single(invitations.Calls).UserId);

        h.Db.ChangeTracker.Clear();
        var after = await h.Db.Users.Include(u => u.Role).SingleAsync(u => u.UserId == legacy.UserId);
        Assert.Equal(UserAccountStatus.Invited, after.AccountStatus);
        Assert.Equal(LeadRoles.Employee, after.Role.Role_name);
        Assert.Null(after.Password);
        Assert.Null(after.RefreshToken);
        Assert.Null(after.RefreshTokenExpiresAt);
        Assert.NotNull(await h.Db.ClientEmailVerifications.AsNoTracking()
            .Where(v => v.UserId == legacy.UserId)
            .Select(v => v.RevokedAt)
            .SingleAsync());

        Assert.Null(await auth.LoginAsync(new LoginRequestDto { Email = legacy.Email, Password = oldPassword }));
        Assert.Null(await auth.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = rawRefresh }));
    }

    [Fact]
    public async Task Linking_staff_without_a_verification_stamp_keeps_the_password_and_drops_a_changed_role_session()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var staff = new StaffManagementService(h.Db, invitations);

        const string password = "manager-pass-1";
        const string rawRefresh = "manager-refresh-token";
        var login = new User
        {
            FullName = "Unlinked Manager",
            Email = "unlinked.manager@example.com",
            NormalizedEmail = EmailIdentity.Normalize("unlinked.manager@example.com"),
            Password = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = 3,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = null,
            RefreshToken = Sha256(rawRefresh),
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        h.Db.Users.Add(login);
        await h.Db.SaveChangesAsync();
        var passwordBefore = login.Password;
        h.Db.ChangeTracker.Clear();

        var result = await staff.CreateAsync(h.Admin, NewStaff(
            name: login.FullName,
            email: login.Email,
            role: LeadRoles.Employee,
            existingUserId: login.UserId));

        Assert.Equal(StaffAccountAccess.Active, result.Account.Access);
        Assert.Equal(LeadRoles.Employee, result.Account.Role);
        Assert.False(result.InvitationRequired);
        Assert.Empty(invitations.Calls);

        h.Db.ChangeTracker.Clear();
        var after = await h.Db.Users.SingleAsync(u => u.UserId == login.UserId);
        Assert.Equal(passwordBefore, after.Password);
        Assert.Equal(UserAccountStatus.Active, after.AccountStatus);
        Assert.Null(after.RefreshToken);
        Assert.Null(after.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task A_pending_client_login_becomes_an_invited_staff_login_with_a_redeemable_invitation()
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

        const string oldPassword = "old-client-password";
        const string openToken = "pending-client-verification-token";
        var pending = new User
        {
            FullName = "Pending Client",
            Email = "pending.client@example.com",
            NormalizedEmail = EmailIdentity.Normalize("pending.client@example.com"),
            Password = BCrypt.Net.BCrypt.HashPassword(oldPassword),
            RoleId = 2,
            AccountStatus = UserAccountStatus.PendingEmailVerification,
            RefreshToken = "pending-session",
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(2)
        };
        h.Db.Users.Add(pending);
        await h.Db.SaveChangesAsync();
        h.Db.ClientEmailVerifications.AddRange(
            new ClientEmailVerification
            {
                UserId = pending.UserId,
                TokenHash = Sha256(openToken),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(12)
            },
            new ClientEmailVerification
            {
                UserId = pending.UserId,
                TokenHash = Sha256("already-used-token"),
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                ExpiresAt = DateTime.UtcNow.AddHours(10),
                VerifiedAt = DateTime.UtcNow.AddHours(-1)
            });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var offered = await staff.GetLinkableUsersAsync(h.Admin);
        Assert.Equal(UserAccountStatus.PendingEmailVerification,
            offered.Single(u => u.UserId == pending.UserId).AccountStatus);
        Assert.DoesNotContain(await staff.GetLinkableUsersAsync(h.Manager), u => u.UserId == pending.UserId);

        // A manager still cannot turn a customer login into staff, pending or not.
        await Assert.ThrowsAsync<LeadAuthorizationException>(() => staff.CreateAsync(
            h.Manager, NewStaff(name: pending.FullName, email: pending.Email, existingUserId: pending.UserId)));
        Assert.Empty(email.Sent);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(UserAccountStatus.PendingEmailVerification,
            (await h.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == pending.UserId)).AccountStatus);

        var result = await staff.CreateAsync(h.Admin, NewStaff(
            name: pending.FullName,
            email: pending.Email,
            role: LeadRoles.Employee,
            existingUserId: pending.UserId));

        Assert.Equal(pending.UserId, result.Account.UserId);
        Assert.Equal(LeadRoles.Employee, result.Account.Role);
        Assert.Equal(StaffAccountAccess.Invited, result.Account.Access);
        Assert.NotEqual(StaffAccountAccess.Active, result.Account.Access);
        Assert.True(result.InvitationRequired);
        Assert.True(result.InvitationSent);

        var listed = (await staff.GetAccountsAsync()).Single(a => a.UserId == pending.UserId);
        Assert.Equal(StaffAccountAccess.Invited, listed.Access);
        Assert.NotEqual(StaffAccountAccess.Active, listed.Access);

        h.Db.ChangeTracker.Clear();
        var linked = await h.Db.Users.Include(u => u.Role).SingleAsync(u => u.UserId == pending.UserId);
        Assert.Equal(UserAccountStatus.Invited, linked.AccountStatus);
        Assert.Equal(LeadRoles.Employee, linked.Role.Role_name);
        Assert.Null(linked.Password);
        Assert.Null(linked.RefreshToken);
        Assert.Null(linked.RefreshTokenExpiresAt);

        var verifications = await h.Db.ClientEmailVerifications.AsNoTracking()
            .Where(v => v.UserId == pending.UserId)
            .ToListAsync();
        Assert.NotNull(verifications.Single(v => v.VerifiedAt == null).RevokedAt);
        Assert.Null(verifications.Single(v => v.VerifiedAt != null).RevokedAt);

        var clientVerification = new ClientEmailVerificationService(h.Db, settings, email, h.Clock);
        var spent = await clientVerification.VerifyAsync(openToken, "a-client-password");
        Assert.False(spent.Verified);

        Assert.Null(await auth.LoginAsync(new LoginRequestDto { Email = pending.Email, Password = oldPassword }));

        const string chosen = "chosen-by-the-staff-1";
        var link = Regex.Match(email.Sent[^1].TextBody, @"https?://\S*/activate-account#token=\S+");
        Assert.True(link.Success, "No activation link was present in the invitation email.");
        var token = Uri.UnescapeDataString(link.Value.Split("#token=", StringSplitOptions.None)[1]);
        var activation = await invitations.ActivateAsync(token, chosen);
        Assert.True(activation.Activated, activation.Error);

        h.Db.ChangeTracker.Clear();
        Assert.Equal(UserAccountStatus.Active,
            (await h.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == pending.UserId)).AccountStatus);
        var session = await auth.LoginAsync(new LoginRequestDto { Email = pending.Email, Password = chosen });
        Assert.NotNull(session);
        Assert.Null(await auth.LoginAsync(new LoginRequestDto { Email = pending.Email, Password = oldPassword }));
    }

    [Fact]
    public async Task A_pending_login_already_on_an_employee_is_never_listed_as_active()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var staff = new StaffManagementService(h.Db, invitations);
        var auth = new AuthService(h.Db, new FixedTokenService());

        const string oldPassword = "legacy-client-password";
        var user = new User
        {
            FullName = "Legacy Pending",
            Email = "legacy.pending@example.com",
            NormalizedEmail = EmailIdentity.Normalize("legacy.pending@example.com"),
            Password = BCrypt.Net.BCrypt.HashPassword(oldPassword),
            RoleId = 4,
            AccountStatus = UserAccountStatus.PendingEmailVerification,
            RefreshToken = "legacy-session",
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(2)
        };
        var employee = new Employee
        {
            User = user,
            FullName = "Legacy Pending",
            Email = "legacy.pending@example.com",
            JobTitle = "Sales Executive",
            Department = "Sales",
            Phone = "03001230000",
            JoinDate = DateTime.UtcNow.Date,
            Status = EmployeeStatus.Active
        };
        h.Db.Employees.Add(employee);
        await h.Db.SaveChangesAsync();
        h.Db.ClientEmailVerifications.Add(new ClientEmailVerification
        {
            UserId = user.UserId,
            TokenHash = Sha256("legacy-open-token"),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(6)
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var listed = (await staff.GetAccountsAsync()).Single(a => a.Email == "legacy.pending@example.com");
        Assert.Equal(StaffAccountAccess.Invited, listed.Access);
        Assert.NotEqual(StaffAccountAccess.Active, listed.Access);

        employee = await h.Db.Employees.SingleAsync(e => e.Id == listed.EmployeeId);
        employee.Status = EmployeeStatus.OnLeave;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            staff.ResendInvitationAsync(h.Admin, listed.EmployeeId));
        Assert.Contains("not in Active employment", blocked.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(invitations.Calls);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(UserAccountStatus.PendingEmailVerification,
            (await h.Db.Users.AsNoTracking().SingleAsync(u => u.Email == "legacy.pending@example.com")).AccountStatus);

        employee = await h.Db.Employees.SingleAsync(e => e.Id == listed.EmployeeId);
        employee.Status = EmployeeStatus.Active;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var off = await staff.DisableAccessAsync(h.Admin, listed.EmployeeId);
        Assert.Equal(StaffAccountAccess.Disabled, off.Access);
        var on = await staff.EnableAccessAsync(h.Admin, listed.EmployeeId);
        Assert.Equal(StaffAccountAccess.Invited, on.Access);
        Assert.NotEqual(StaffAccountAccess.Active, on.Access);

        h.Db.ChangeTracker.Clear();
        var restored = await h.Db.Users.SingleAsync(u => u.Email == "legacy.pending@example.com");
        Assert.Equal(UserAccountStatus.Invited, restored.AccountStatus);
        Assert.Null(restored.Password);
        Assert.Null(restored.RefreshToken);
        Assert.NotNull(await h.Db.ClientEmailVerifications.AsNoTracking()
            .Where(v => v.UserId == restored.UserId && v.VerifiedAt == null)
            .Select(v => v.RevokedAt)
            .SingleAsync());
        Assert.Null(await auth.LoginAsync(new LoginRequestDto
        {
            Email = "legacy.pending@example.com",
            Password = oldPassword
        }));

        // Back to pending so resend is what repairs a row the list already calls "Invite sent".
        restored.AccountStatus = UserAccountStatus.PendingEmailVerification;
        restored.Password = BCrypt.Net.BCrypt.HashPassword(oldPassword);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var resent = await staff.ResendInvitationAsync(h.Admin, listed.EmployeeId);
        Assert.True(resent.Issued);
        var call = Assert.Single(invitations.Calls);
        Assert.Equal(restored.UserId, call.UserId);

        h.Db.ChangeTracker.Clear();
        var invited = await h.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == restored.UserId);
        Assert.Equal(UserAccountStatus.Invited, invited.AccountStatus);
        Assert.Null(invited.Password);
        Assert.Equal(StaffAccountAccess.Invited,
            (await staff.GetAccountsAsync()).Single(a => a.EmployeeId == listed.EmployeeId).Access);
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

        Assert.DoesNotContain(await service.GetLinkableUsersAsync(h.Admin), u => u.UserId == disabled.UserId);

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

        var result = await service.CreateAsync(h.Admin, NewStaff());

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
    public async Task Salespeople_cannot_provision_or_resend_staff_access()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            service.CreateAsync(h.Sales, NewStaff()));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            service.ResendInvitationAsync(h.Sales, h.SalesEmployeeId));

        Assert.Empty(invitations.Calls);
    }

    [Fact]
    public async Task A_manager_can_provision_staff_but_never_hand_out_or_touch_the_Admin_role()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        var created = await service.CreateAsync(h.Manager, NewStaff());
        Assert.Equal(LeadRoles.Employee, created.Account.Role);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            service.CreateAsync(h.Manager, NewStaff(name: "Would Be Admin", email: "wba@example.com", role: LeadRoles.Admin)));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            service.UpdateAsync(h.Manager, h.SalesEmployeeId, new UpdateStaffAccountDto { Role = LeadRoles.Admin }));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            service.UpdateAsync(h.Manager, h.ManagerEmployeeId, new UpdateStaffAccountDto { Role = LeadRoles.Admin }));

        // An unlinked Admin login is neither offered to nor linkable by a manager.
        Assert.DoesNotContain(await service.GetLinkableUsersAsync(h.Manager), u => u.UserId == h.AdminUserId);
        Assert.Contains(await service.GetLinkableUsersAsync(h.Admin), u => u.UserId == h.AdminUserId);
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            service.CreateAsync(h.Manager, NewStaff(existingUserId: h.AdminUserId)));

        // An Admin who is also an employee cannot be demoted by a manager.
        var adminEmployee = new Employee
        {
            UserId = h.AdminUserId, FullName = "Ayesha Admin", Email = "admin-employee@dams.test",
            Phone = "03000000000", JobTitle = "Director", Department = "Management", Status = EmployeeStatus.Active
        };
        h.Db.Employees.Add(adminEmployee);
        await h.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            service.UpdateAsync(h.Manager, adminEmployee.Id, new UpdateStaffAccountDto { Role = LeadRoles.Employee }));

        h.Db.ChangeTracker.Clear();
        Assert.Equal(LeadRoles.Admin, (await h.Db.Users.Include(u => u.Role).SingleAsync(u => u.UserId == h.AdminUserId)).Role.Role_name);
    }

    [Fact]
    public async Task A_manager_can_neither_see_nor_take_over_a_customer_login()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new StaffManagementService(h.Db, new FakeInvitations());

        // A customer portal login is not offered to a manager, and cannot be linked by id either.
        Assert.DoesNotContain(await service.GetLinkableUsersAsync(h.Manager), u => u.UserId == h.ClientUserId);
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            service.CreateAsync(h.Manager, NewStaff(existingUserId: h.ClientUserId)));

        h.Db.ChangeTracker.Clear();
        var client = await h.Db.Users.Include(u => u.Role).SingleAsync(u => u.UserId == h.ClientUserId);
        Assert.NotEqual(LeadRoles.Employee, client.Role.Role_name);
        Assert.NotEqual(LeadRoles.Manager, client.Role.Role_name);
        Assert.False(await h.Db.Employees.AnyAsync(e => e.UserId == h.ClientUserId));

        // An Admin still sees every unlinked login, customers included.
        Assert.Contains(await service.GetLinkableUsersAsync(h.Admin), u => u.UserId == h.ClientUserId);
    }

    [Fact]
    public async Task A_manager_cannot_change_employment_status_but_can_save_it_unchanged()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new StaffManagementService(h.Db, new FakeInvitations());

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            service.UpdateAsync(h.Manager, h.SalesEmployeeId,
                new UpdateStaffAccountDto { Role = LeadRoles.Employee, Status = EmployeeStatus.Terminated }));

        h.Db.ChangeTracker.Clear();
        Assert.Equal(EmployeeStatus.Active, (await h.Db.Employees.SingleAsync(e => e.Id == h.SalesEmployeeId)).Status);

        var saved = await service.UpdateAsync(h.Manager, h.SalesEmployeeId,
            new UpdateStaffAccountDto { Role = LeadRoles.Employee, Status = EmployeeStatus.Active });
        Assert.Equal(EmployeeStatus.Active, saved.Status);

        h.Db.ChangeTracker.Clear();
        var terminated = await service.UpdateAsync(h.Admin, h.SalesEmployeeId,
            new UpdateStaffAccountDto { Role = LeadRoles.Employee, Status = EmployeeStatus.OnLeave });
        Assert.Equal(EmployeeStatus.OnLeave, terminated.Status);
    }

    [Fact]
    public async Task Admin_can_resend_only_to_a_linked_login_that_is_still_waiting()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var service = new StaffManagementService(h.Db, invitations);

        // A staff member who has been invited but has not activated yet.
        var created = await service.CreateAsync(h.Admin, NewStaff());
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

        var invited = await service.CreateAsync(h.Admin, NewStaff());
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
    public async Task Directory_shows_every_colleague_to_sales_staff_and_nothing_to_customers()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new StaffManagementService(h.Db, new FakeInvitations());

        var managerRows = await service.GetDirectoryAsync(h.Manager);
        Assert.Contains(managerRows, e => e.EmployeeId == h.SalesEmployeeId);
        Assert.Contains(managerRows, e => e.EmployeeId == h.OtherSalesEmployeeId);

        var employeeRows = await service.GetDirectoryAsync(h.Sales);
        Assert.Contains(employeeRows, e => e.EmployeeId == h.ManagerEmployeeId);
        Assert.Contains(employeeRows, e => e.EmployeeId == h.OtherSalesEmployeeId);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => service.GetDirectoryAsync(h.Client));
    }

    [Fact]
    public async Task Role_change_still_revokes_the_refresh_session_and_leaves_the_password_alone()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var service = new StaffManagementService(h.Db, new FakeInvitations());
        var user = await h.Db.Users.SingleAsync(u => u.UserId == h.SalesUserId);
        var passwordBefore = user.Password;
        user.RefreshToken = "old-session";
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);
        await h.Db.SaveChangesAsync();

        var updated = await service.UpdateAsync(h.Admin, h.SalesEmployeeId, new UpdateStaffAccountDto
        {
            Role = LeadRoles.Manager
        });

        h.Db.ChangeTracker.Clear();
        user = await h.Db.Users.SingleAsync(u => u.UserId == h.SalesUserId);
        Assert.Equal(LeadRoles.Manager, updated.Role);

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
        Assert.Equal(LeadStage.FirstContactPending, created.Lead.Stage);
        Assert.NotNull(await h.Leads.GetByIdAsync(created.Lead.Id, h.Sales));
    }

    [Fact]
    public async Task Only_an_admin_can_give_the_accountant_role()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var staff = new StaffManagementService(h.Db, new FakeInvitations());

        var granted = await staff.UpdateAsync(h.Admin, h.SalesEmployeeId, new UpdateStaffAccountDto
        {
            Role = AppRoles.Accountant,
            Status = EmployeeStatus.Active
        });
        Assert.Equal(AppRoles.Accountant, granted.Role);

        var denied = await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            staff.UpdateAsync(h.Manager, h.OtherSalesEmployeeId, new UpdateStaffAccountDto
            {
                Role = AppRoles.Accountant,
                Status = EmployeeStatus.Active
            }));
        Assert.Contains("Accountant", denied.Message);
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
        var created = (await staff.CreateAsync(h.Admin, NewStaff(email: typedEmail))).Account;

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

        await staff.CreateAsync(h.Admin, NewStaff(email: "nadia@example.com"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            staff.CreateAsync(h.Admin, NewStaff(name: "Nadia Again", email: "NADIA@example.com")));
        Assert.Contains("already exists", error.Message);
    }

    [Fact]
    public async Task Disable_and_enable_access_follow_the_staff_rules()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var staff = new StaffManagementService(h.Db, new FakeInvitations());
        var auth = new AuthService(h.Db, new FixedTokenService());

        var sales = await h.Db.Users.SingleAsync(u => u.UserId == h.SalesUserId);
        sales.Password = BCrypt.Net.BCrypt.HashPassword("sales-pass-1");
        sales.RefreshToken = "stale-session";
        sales.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var session = await auth.LoginAsync(new LoginRequestDto { Email = "sales@dams.test", Password = "sales-pass-1" });
        Assert.NotNull(session);

        var disabled = await staff.DisableAccessAsync(h.Admin, h.SalesEmployeeId);
        Assert.Equal(StaffAccountAccess.Disabled, disabled.Access);

        h.Db.ChangeTracker.Clear();
        var salesAfter = await h.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == h.SalesUserId);
        Assert.Equal(UserAccountStatus.Disabled, salesAfter.AccountStatus);
        Assert.Null(salesAfter.RefreshToken);
        Assert.Null(salesAfter.RefreshTokenExpiresAt);
        Assert.Null(await auth.LoginAsync(new LoginRequestDto { Email = "sales@dams.test", Password = "sales-pass-1" }));
        Assert.Null(await auth.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = session!.RefreshToken }));

        var stillAssigned = await h.Db.Leads.CountAsync(l => l.AssignedEmployeeId == h.SalesEmployeeId);
        var enabled = await staff.EnableAccessAsync(h.Admin, h.SalesEmployeeId);
        Assert.Equal(StaffAccountAccess.Active, enabled.Access);
        Assert.NotNull(await auth.LoginAsync(new LoginRequestDto { Email = "sales@dams.test", Password = "sales-pass-1" }));
        Assert.Equal(stillAssigned, await h.Db.Leads.CountAsync(l => l.AssignedEmployeeId == h.SalesEmployeeId));

        var audit = await h.Db.StaffAccessAudits.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.False(audit[0].AccessEnabled);
        Assert.True(audit[1].AccessEnabled);
        Assert.All(audit, a => Assert.Equal(h.AdminUserId, a.PerformedByUserId));

        var self = await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            staff.DisableAccessAsync(h.Manager, h.ManagerEmployeeId));
        Assert.Contains("own access", self.Message, StringComparison.OrdinalIgnoreCase);

        var adminEmployee = new Employee
        {
            FullName = "Ayesha Admin",
            JobTitle = "Director",
            Department = "Management",
            Phone = "03001110000",
            JoinDate = DateTime.UtcNow.Date,
            Status = EmployeeStatus.Active,
            UserId = h.AdminUserId
        };
        h.Db.Employees.Add(adminEmployee);
        await h.Db.SaveChangesAsync();

        var own = await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            staff.DisableAccessAsync(h.Admin, adminEmployee.Id));
        Assert.Contains("own access", own.Message, StringComparison.OrdinalIgnoreCase);

        var managerBlocked = await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            staff.DisableAccessAsync(h.Manager, adminEmployee.Id));
        Assert.Contains("Admin", managerBlocked.Message);

        var secondAdmin = new User
        {
            FullName = "Second Admin",
            Email = "admin2@dams.test",
            NormalizedEmail = EmailIdentity.Normalize("admin2@dams.test"),
            Password = "hash",
            RoleId = 1,
            AccountStatus = UserAccountStatus.Active
        };
        h.Db.Users.Add(secondAdmin);
        await h.Db.SaveChangesAsync();
        var secondEmployee = new Employee
        {
            FullName = "Second Admin",
            JobTitle = "Director",
            Department = "Management",
            Phone = "03001110001",
            JoinDate = DateTime.UtcNow.Date,
            Status = EmployeeStatus.Active,
            UserId = secondAdmin.UserId
        };
        h.Db.Employees.Add(secondEmployee);
        await h.Db.SaveChangesAsync();

        var turnedOff = await staff.DisableAccessAsync(h.Admin, secondEmployee.Id);
        Assert.Equal(StaffAccountAccess.Disabled, turnedOff.Access);

        var actor = await h.Db.Users.SingleAsync(u => u.UserId == h.AdminUserId);
        actor.AccountStatus = UserAccountStatus.Disabled;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await staff.EnableAccessAsync(h.Admin, secondEmployee.Id);

        var lastAdmin = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            staff.DisableAccessAsync(h.Admin, secondEmployee.Id));
        Assert.Contains("last active admin", lastAdmin.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Manager_can_turn_off_a_manager_or_employee_and_invited_logins_stay_invited()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var staff = new StaffManagementService(h.Db, new FakeInvitations());

        var employee = await staff.DisableAccessAsync(h.Manager, h.SalesEmployeeId);
        Assert.Equal(StaffAccountAccess.Disabled, employee.Access);

        var otherManager = new User
        {
            FullName = "Second Manager",
            Email = "manager2@dams.test",
            NormalizedEmail = EmailIdentity.Normalize("manager2@dams.test"),
            Password = BCrypt.Net.BCrypt.HashPassword("manager-pass-1"),
            RoleId = 3,
            AccountStatus = UserAccountStatus.Active
        };
        h.Db.Users.Add(otherManager);
        await h.Db.SaveChangesAsync();
        var otherManagerEmployee = new Employee
        {
            FullName = "Second Manager",
            JobTitle = "Sales Manager",
            Department = "Sales",
            Phone = "03001110002",
            JoinDate = DateTime.UtcNow.Date,
            Status = EmployeeStatus.Active,
            UserId = otherManager.UserId
        };
        h.Db.Employees.Add(otherManagerEmployee);
        await h.Db.SaveChangesAsync();

        var turnedOff = await staff.DisableAccessAsync(h.Manager, otherManagerEmployee.Id);
        Assert.Equal(StaffAccountAccess.Disabled, turnedOff.Access);
        var restored = await staff.EnableAccessAsync(h.Manager, otherManagerEmployee.Id);
        Assert.Equal(StaffAccountAccess.Active, restored.Access);

        var history = await staff.GetAccessHistoryAsync(h.Manager, otherManagerEmployee.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal(h.Manager.DisplayName, history[0].PerformedByName);
        Assert.True(history[0].AccessEnabled);

        var invited = await staff.CreateAsync(h.Manager, NewStaff(email: "invitee@example.com"));
        var stillInvited = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            staff.EnableAccessAsync(h.Manager, invited.Account.EmployeeId));
        Assert.Contains("invited", stillInvited.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StaffAccountAccess.Invited,
            (await staff.GetAccountsAsync()).Single(a => a.EmployeeId == invited.Account.EmployeeId).Access);

        await Assert.ThrowsAsync<StaffNotFoundException>(() =>
            staff.DisableAccessAsync(h.Admin, 999_999));
    }

    [Fact]
    public async Task An_invited_login_turned_off_and_back_on_is_waiting_for_its_invitation_again()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var invitations = new FakeInvitations();
        var staff = new StaffManagementService(h.Db, invitations);
        var invited = await staff.CreateAsync(h.Admin, NewStaff(email: "paused@example.com"));
        var employeeId = invited.Account.EmployeeId;

        var off = await staff.DisableAccessAsync(h.Admin, employeeId);
        Assert.Equal(StaffAccountAccess.Disabled, off.Access);

        // It never set a password, so "on" must mean invited again, not Active with no way in.
        var on = await staff.EnableAccessAsync(h.Admin, employeeId);
        Assert.Equal(StaffAccountAccess.Invited, on.Access);
        h.Db.ChangeTracker.Clear();
        var user = await h.Db.Users.SingleAsync(u => u.UserId == invited.Account.UserId);
        Assert.Equal(UserAccountStatus.Invited, user.AccountStatus);
        Assert.Null(user.Password);

        // And the invitation can be sent again, which requires Invited.
        invitations.Calls.Clear();
        var resent = await staff.ResendInvitationAsync(h.Admin, employeeId);
        Assert.True(resent.Issued);
        Assert.Single(invitations.Calls);
    }

    private static string Sha256(string rawToken) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawToken)));

    private sealed class FixedTokenService : ITokenService
    {
        public string GenerateAccessToken(User user, string roleName) => "access-token";
        public string GenerateRefreshToken() => "refresh-token";
    }
}
