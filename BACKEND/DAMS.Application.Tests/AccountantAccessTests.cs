using System.Reflection;
using DAMS.Api.Controllers;
using DAMS.Application.Common;
using DAMS.Application.DTOs.EmployeeDtos;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The Accountant works everywhere except the Lead CRM, and cannot decide who signs in to DAMS.
/// The controller checks read the same attributes ASP.NET does: every [Authorize] on the class
/// and the action must pass, and [AllowAnonymous] lets anyone through.
/// </summary>
public sealed class AccountantAccessTests
{
    private static readonly Type[] Crm =
    {
        typeof(LeadsController),
        typeof(LeadDashboardController),
        typeof(LeadEngagementController),
        typeof(LeadConfigurationController),
        typeof(MetaIntegrationController),
        typeof(StaffController)
    };

    private static readonly Type[] Company =
    {
        typeof(BookingController),
        typeof(CapitalPartnersController),
        typeof(CommissionRebatesController),
        typeof(CustomerController),
        typeof(CustomerDocumentsController),
        typeof(EmployeeController),
        typeof(ExpenseCategoriesController),
        typeof(FinanceAccountsController),
        typeof(FinanceController),
        typeof(LoansController),
        typeof(ProjectController),
        typeof(RevenueCategoriesController),
        typeof(StaffCashController),
        typeof(UnitController),
        typeof(VendorsController),
        typeof(WhtController)
    };

    public static TheoryData<Type> CrmControllers => Rows(Crm);

    public static TheoryData<Type> CompanyControllers => Rows(Company);

    [Theory]
    [MemberData(nameof(CrmControllers))]
    public void Every_signed_in_CRM_endpoint_refuses_the_accountant(Type controller)
    {
        var actions = Actions(controller).Where(a => !IsAnonymous(controller, a)).ToList();
        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.False(
            Allows(controller, action, AppRoles.Accountant),
            $"{controller.Name}.{action.Name} lets an Accountant in."));
    }

    [Fact]
    public void The_only_anonymous_CRM_endpoint_is_the_meta_sign_in_callback()
    {
        // Meta's browser redirect carries no token; its one-time state is what authenticates it.
        var anonymous = Crm.SelectMany(c => Actions(c).Where(a => IsAnonymous(c, a)).Select(a => $"{c.Name}.{a.Name}"));

        Assert.Equal(new[] { "MetaIntegrationController.Callback" }, anonymous);
    }

    [Theory]
    [MemberData(nameof(CompanyControllers))]
    public void Every_company_endpoint_admits_the_accountant_and_the_admin(Type controller)
    {
        var actions = Actions(controller);
        Assert.NotEmpty(actions);
        Assert.All(actions, action =>
        {
            Assert.True(Allows(controller, action, AppRoles.Accountant),
                $"{controller.Name}.{action.Name} refuses an Accountant.");
            Assert.True(Allows(controller, action, AppRoles.Admin),
                $"{controller.Name}.{action.Name} refuses an Admin.");
        });
    }

    [Fact]
    public void Booking_requests_are_readable_by_the_accountant_but_only_an_admin_approves_or_rejects()
    {
        var controller = typeof(BookingRequestController);
        foreach (var name in new[] { "GetBookingRequests", "GetBookingRequest", "GetStats" })
            Assert.True(Allows(controller, controller.GetMethod(name)!, AppRoles.Accountant), name);

        // Approving converts the lead in the CRM; rejecting closes its held enquiries.
        foreach (var name in new[] { "ApproveBookingRequest", "RejectBookingRequest" })
        {
            var action = controller.GetMethod(name)!;
            Assert.False(Allows(controller, action, AppRoles.Accountant), name);
            Assert.True(Allows(controller, action, AppRoles.Admin), name);
        }
    }

    [Fact]
    public void Notification_administration_stays_admin_only()
    {
        var controller = typeof(NotificationAdminController);
        Assert.All(Actions(controller), action =>
            Assert.False(Allows(controller, action, AppRoles.Accountant), action.Name));
    }

    // ── Employees: the accountant cannot give or take away DAMS access ──────────

    [Fact]
    public async Task The_accountant_cannot_create_an_employee_linked_to_a_login()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var login = await AddUnlinkedLoginAsync(h);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            Employees(h).CreateEmployeeAsync(NewEmployee(login), actorIsAdmin: false));

        var created = await Employees(h).CreateEmployeeAsync(NewEmployee(login), actorIsAdmin: true);
        Assert.Equal(login, created.UserId);
    }

    [Fact]
    public async Task The_accountant_cannot_relink_or_unlink_a_login()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var login = await AddUnlinkedLoginAsync(h);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            Employees(h).UpdateEmployeeAsync(h.SalesEmployeeId, new UpdateEmployeeDto { UserId = login }, actorIsAdmin: false));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            Employees(h).UpdateEmployeeAsync(h.SalesEmployeeId, new UpdateEmployeeDto { UserId = -1 }, actorIsAdmin: false));

        h.Db.ChangeTracker.Clear();
        Assert.Equal(h.SalesUserId, (await h.Db.Employees.AsNoTracking().FirstAsync(e => e.Id == h.SalesEmployeeId)).UserId);
    }

    [Fact]
    public async Task The_accountant_cannot_reactivate_or_end_the_employment_of_someone_with_a_login()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var employee = await h.Db.Employees.FirstAsync(e => e.Id == h.ManagerEmployeeId);
        employee.Status = EmployeeStatus.Terminated;
        await h.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            Employees(h).UpdateEmployeeAsync(h.ManagerEmployeeId,
                new UpdateEmployeeDto { Status = EmployeeStatus.Active }, actorIsAdmin: false));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            Employees(h).UpdateEmployeeAsync(h.SalesEmployeeId,
                new UpdateEmployeeDto { Status = EmployeeStatus.Terminated }, actorIsAdmin: false));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            Employees(h).DeleteEmployeeAsync(h.SalesEmployeeId, actorIsAdmin: false));

        h.Db.ChangeTracker.Clear();
        Assert.Equal(EmployeeStatus.Terminated,
            (await h.Db.Employees.AsNoTracking().FirstAsync(e => e.Id == h.ManagerEmployeeId)).Status);
    }

    [Fact]
    public async Task The_accountant_still_runs_the_rest_of_an_employee_record()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        // Resending the current link and status, as an edit form does, is not a change.
        var edited = await Employees(h).UpdateEmployeeAsync(h.SalesEmployeeId, new UpdateEmployeeDto
        {
            JobTitle = "Senior Sales Executive",
            Salary = 90_000m,
            Status = EmployeeStatus.Active,
            UserId = h.SalesUserId
        }, actorIsAdmin: false);
        Assert.Equal("Senior Sales Executive", edited.JobTitle);

        // Someone with no login gains no DAMS access from a status change.
        var noLogin = await Employees(h).CreateEmployeeAsync(NewEmployee(userId: null), actorIsAdmin: false);
        var ended = await Employees(h).UpdateEmployeeAsync(noLogin.Id,
            new UpdateEmployeeDto { Status = EmployeeStatus.Terminated }, actorIsAdmin: false);
        Assert.Equal(EmployeeStatus.Terminated, ended.Status);
    }

    // ── Leads cannot be owned by someone outside the CRM ────────────────────────

    [Fact]
    public async Task A_lead_cannot_be_assigned_to_an_accountant()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        var accountant = await AddAccountantAsync(h);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = accountant }, h.Admin));

        Assert.Contains("does not work in the Lead CRM", error.Message);
        Assert.Null((await h.LoadLeadAsync(leadId)).AssignedEmployeeId);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static TheoryData<Type> Rows(IEnumerable<Type> controllers)
    {
        var rows = new TheoryData<Type>();
        foreach (var controller in controllers)
            rows.Add(controller);
        return rows;
    }

    private static List<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

    private static List<object> Attributes(Type controller, MethodInfo action) =>
        controller.GetCustomAttributes(inherit: true)
            .Concat(action.GetCustomAttributes(inherit: true))
            .ToList();

    private static bool IsAnonymous(Type controller, MethodInfo action) =>
        Attributes(controller, action).OfType<IAllowAnonymous>().Any();

    private static bool Allows(Type controller, MethodInfo action, string role)
    {
        if (IsAnonymous(controller, action))
            return true;

        return Attributes(controller, action).OfType<IAuthorizeData>()
            .Where(a => !string.IsNullOrWhiteSpace(a.Roles))
            .All(a => a.Roles!.Split(',').Select(r => r.Trim()).Contains(role));
    }

    private static EmployeeService Employees(LeadTestHarness h) =>
        new(h.Db, new FinanceAccountService(h.Db));

    private static CreateEmployeeDto NewEmployee(int? userId) => new()
    {
        FullName = "Fatima Finance",
        JobTitle = "Accountant",
        Department = "Finance",
        Phone = "03004445555",
        JoinDate = new DateTime(2026, 1, 1),
        UserId = userId
    };

    private static async Task<int> AddUnlinkedLoginAsync(LeadTestHarness h)
    {
        var user = new User
        {
            FullName = "Fatima Finance",
            Email = "fatima@dams.test",
            NormalizedEmail = "FATIMA@DAMS.TEST",
            Password = "hash",
            RoleId = 5
        };
        h.Db.Users.Add(user);
        await h.Db.SaveChangesAsync();
        return user.UserId;
    }

    private static async Task<int> AddAccountantAsync(LeadTestHarness h)
    {
        var employee = new Employee
        {
            FullName = "Fatima Finance",
            JobTitle = "Accountant",
            Department = "Finance",
            Phone = "03004445555",
            JoinDate = new DateTime(2026, 1, 1),
            Status = EmployeeStatus.Active,
            UserId = await AddUnlinkedLoginAsync(h)
        };
        h.Db.Employees.Add(employee);
        await h.Db.SaveChangesAsync();
        return employee.Id;
    }
}
