using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// A Meta lead arrives with nobody assigned. The unassigned queue belongs to the managers,
/// so they — not only the admins — must hear about it, and about every later enquiry from
/// the same person until a salesperson owns the lead.
/// </summary>
public class MetaUnassignedLeadNotificationTests
{
    private static (string Name, string? Value)[] Fields(string email = "ali@example.com") =>
    [
        ("full_name", "Ali Khan"),
        ("phone_number", "+92 300 1234567"),
        ("email", email)
    ];

    private static async Task<Lead> ReceiveAsync(MetaIntegrationHarness h, string pageId, string leadgenId)
    {
        h.Graph.Leads[leadgenId] = FakeMetaGraphClient.Lead(leadgenId, Fields(), pageId: pageId);
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(pageId, leadgenId));
        await h.Processor.ProcessPendingEventsAsync(10);
        return await h.Db.Leads.SingleAsync();
    }

    private static Task<List<Notification>> AlertsAsync(MetaIntegrationHarness h, int leadId, string titlePrefix) =>
        h.Db.Notifications
            .Where(n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId
                        && n.Type == NotificationType.LeadCreated && n.Title.StartsWith(titlePrefix))
            .ToListAsync();

    [Fact]
    public async Task ANewUnassignedMetaLead_IsQueuedAndReachesTheManagersWhoHandItOut()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        var lead = await ReceiveAsync(h, page.ExternalId, "lead-1");

        Assert.Equal(LeadAssignmentState.Unassigned, lead.AssignmentState);

        // It sits in the ordinary queue a manager works from.
        var queue = await h.Leads.Leads.GetLeadsAsync(new LeadFilterDto { Unassigned = true }, h.Leads.Manager);
        Assert.Contains(queue.Items, l => l.Id == lead.Id);

        var alerts = await AlertsAsync(h, lead.Id, "New lead");
        Assert.Contains(alerts, n => n.RecipientUserId == h.Leads.AdminUserId);
        Assert.Contains(alerts, n => n.RecipientUserId == h.Leads.ManagerUserId);

        // Salespeople do not own the queue and are not told about leads they cannot see.
        Assert.DoesNotContain(alerts, n => n.RecipientUserId == h.Leads.SalesUserId
                                           || n.RecipientUserId == h.Leads.OtherSalesUserId);
        Assert.Equal(alerts.Count, alerts.Select(n => n.RecipientUserId).Distinct().Count());
    }

    [Fact]
    public async Task AManagerOnAnotherTeam_StillHearsAboutTheUnassignedQueue_ButAFormerManagerDoesNot()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var otherManager = await AddManagerAsync(h, "south@dams.test", EmployeeStatus.Active);
        var formerManager = await AddManagerAsync(h, "former@dams.test", EmployeeStatus.Terminated);
        var (_, page) = await h.ConnectPageAsync();

        var lead = await ReceiveAsync(h, page.ExternalId, "lead-1");

        var alerts = await AlertsAsync(h, lead.Id, "New lead");
        Assert.Contains(alerts, n => n.RecipientUserId == otherManager);
        Assert.DoesNotContain(alerts, n => n.RecipientUserId == formerManager);
    }

    [Fact]
    public async Task ARepeatEnquiryOnAnUnassignedLead_AlertsTheManagersOncePerEnquiry()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();
        var lead = await ReceiveAsync(h, page.ExternalId, "lead-1");

        // The same person submits the form again: it enriches the lead, not a second lead.
        var enriched = await ReceiveAsync(h, page.ExternalId, "lead-2");
        Assert.Equal(lead.Id, enriched.Id);

        var repeats = await AlertsAsync(h, lead.Id, "Repeat enquiry");
        Assert.Contains(repeats, n => n.RecipientUserId == h.Leads.ManagerUserId);
        Assert.Contains(repeats, n => n.RecipientUserId == h.Leads.AdminUserId);
        Assert.DoesNotContain(repeats, n => n.RecipientUserId == h.Leads.SalesUserId);

        // A genuinely different enquiry generates its own alert.
        await ReceiveAsync(h, page.ExternalId, "lead-3");
        Assert.Equal(2, (await AlertsAsync(h, lead.Id, "Repeat enquiry"))
            .Count(n => n.RecipientUserId == h.Leads.ManagerUserId));
    }

    [Fact]
    public async Task OnceAssigned_TheSalespersonGetsTheAssignmentAndLaterEnquiries_NotTheManagers()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();
        var lead = await ReceiveAsync(h, page.ExternalId, "lead-1");

        await h.Leads.Leads.AssignAsync(lead.Id, new AssignLeadDto { EmployeeId = h.Leads.SalesEmployeeId }, h.Leads.Manager);

        Assert.True(await h.Db.Notifications.AnyAsync(n =>
            n.EntityId == lead.Id && n.Type == NotificationType.LeadAssigned
            && n.RecipientUserId == h.Leads.SalesUserId));

        await ReceiveAsync(h, page.ExternalId, "lead-2");

        var repeats = await AlertsAsync(h, lead.Id, "Repeat enquiry");
        Assert.Equal(h.Leads.SalesUserId, Assert.Single(repeats).RecipientUserId);
    }

    [Fact]
    public async Task ARepeatEnquiryOnALeadParkedOnATeam_AlertsThatTeamsManager()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();
        var lead = await ReceiveAsync(h, page.ExternalId, "lead-1");

        // Handed to a team, but no salesperson yet: there is still no owner to tell.
        await h.Leads.Leads.AssignAsync(lead.Id, new AssignLeadDto { TeamId = h.Leads.TeamId }, h.Leads.Admin);

        await ReceiveAsync(h, page.ExternalId, "lead-2");

        var repeats = await AlertsAsync(h, lead.Id, "Repeat enquiry");
        Assert.Contains(repeats, n => n.RecipientUserId == h.Leads.ManagerUserId);
        Assert.DoesNotContain(repeats, n => n.RecipientUserId == h.Leads.SalesUserId);
    }

    private static async Task<int> AddManagerAsync(MetaIntegrationHarness h, string email, EmployeeStatus status)
    {
        var user = new User
        {
            FullName = email,
            Email = email,
            NormalizedEmail = DAMS.Domain.Identity.EmailIdentity.Normalize(email),
            Password = "hash",
            RoleId = 3
        };
        h.Db.Users.Add(user);
        await h.Db.SaveChangesAsync();

        h.Db.Employees.Add(new Employee
        {
            FullName = email,
            JobTitle = "Sales Manager",
            Department = "Sales",
            Phone = "03001113333",
            JoinDate = DateTime.UtcNow.AddYears(-1),
            Status = status,
            UserId = user.UserId
        });
        await h.Db.SaveChangesAsync();
        return user.UserId;
    }
}
