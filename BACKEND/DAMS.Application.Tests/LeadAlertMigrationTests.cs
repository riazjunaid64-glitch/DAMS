using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The lead CRM's alerts after the move onto the central platform.
///
/// The point of these is that the CRM's own rules did not change — the same events fire for
/// the same people at the same times — while the resulting notification now lives in one
/// store, appears in one inbox, and is subject to one set of preferences and one dedup rule.
/// </summary>
public sealed class LeadAlertMigrationTests
{
    [Fact]
    public async Task ALeadAlertIsWrittenToTheCentralStoreWithItsLeadAttachedAndADeepLink()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var alert = await h.Db.Notifications.AsNoTracking()
            .FirstAsync(n => n.RecipientUserId == h.SalesUserId && n.Type == NotificationType.LeadAssigned);

        Assert.Equal(NotificationModule.Leads, alert.Module);
        Assert.Equal(NotificationCategory.LeadAssignments, alert.Category);
        Assert.Equal(NotificationEntityType.Lead, alert.EntityType);
        Assert.Equal(leadId, alert.EntityId);
        Assert.Equal($"/crm/leads/{leadId}", alert.DeepLink);

        // The lead's own details travel with it, so an email or push template can name the
        // record without the CRM knowing what a template variable is.
        Assert.Contains("leadName", alert.DataJson);
        Assert.Contains("Bilal Khan", alert.DataJson);
    }

    [Fact]
    public async Task ALeadAlertOpensStraightIntoTheLeadWorkspaceForItsOwner()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var alert = await h.Db.Notifications.AsNoTracking()
            .FirstAsync(n => n.RecipientUserId == h.SalesUserId);

        var result = await h.Inbox.OpenAsync(alert.Id, h.Notify(h.Sales));

        Assert.True(result.Allowed);
        Assert.Equal($"/crm/leads/{leadId}", result.DeepLink);
    }

    [Fact]
    public async Task EveryLeadAlertGetsExactlyOneInAppDeliveryAndNoProviderWork()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var deliveries = await h.Db.NotificationDeliveries.AsNoTracking().ToListAsync();

        Assert.NotEmpty(deliveries);
        // Email and push are off out of the box, so nothing is queued against a provider that
        // has not been configured.
        Assert.All(deliveries, d => Assert.Equal(NotificationChannel.InApp, d.Channel));
        Assert.All(deliveries, d => Assert.Equal(NotificationDeliveryStatus.Pending, d.Status));
    }

    [Fact]
    public async Task RepeatedAlertScansStillProduceNoDuplicatesAfterTheMigration()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var lead = await h.Db.Leads.FirstAsync(l => l.Id == leadId);
        lead.AssignedAt = DateTime.UtcNow.AddHours(-10);
        await h.Db.SaveChangesAsync();

        await h.Alerts.RunScanAsync();
        var afterFirst = await h.Db.Notifications.CountAsync();

        await h.Alerts.RunScanAsync();
        await h.Alerts.RunScanAsync();

        Assert.Equal(afterFirst, await h.Db.Notifications.CountAsync());

        // Every dedup key is still unique across the whole store, not just within the lead
        // module — that is what stops a second platform re-raising the same alert.
        var keys = await h.Db.Notifications.AsNoTracking().Select(n => n.DedupKey).ToListAsync();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public async Task LeadAlertsAppearInTheSameInboxAsEverythingElseAndCanBeFilteredToTheirCategory()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Call back",
            DueAt = DateTime.UtcNow.AddHours(2),
            AssignedEmployeeId = h.OtherSalesEmployeeId
        }, h.Admin);

        var omar = h.Notify(h.OtherSales);

        var all = await h.Inbox.GetAsync(omar, new NotificationFilterDto());
        Assert.NotEmpty(all.Items);

        var followUps = await h.Inbox.GetAsync(omar, new NotificationFilterDto
        {
            Category = NotificationCategory.FollowUps
        });
        Assert.NotEmpty(followUps.Items);
        Assert.All(followUps.Items, i => Assert.Equal(NotificationCategory.FollowUps, i.Category));

        await Assert.ThrowsAsync<DAMS.Application.Common.LeadAuthorizationException>(() =>
            h.Inbox.GetAsync(omar, new NotificationFilterDto
            {
                Category = NotificationCategory.PaymentsAndReceipts
            }));
    }

    [Fact]
    public async Task AnEscalationIsFlaggedAsOneSoSupervisorsCanTellItApartFromTheirOwnWork()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var lead = await h.Db.Leads.FirstAsync(l => l.Id == leadId);
        lead.AssignedAt = DateTime.UtcNow.AddHours(-10);
        await h.Db.SaveChangesAsync();

        await h.Alerts.RunScanAsync();

        var escalation = await h.Db.Notifications.AsNoTracking()
            .FirstAsync(n => n.RecipientUserId == h.ManagerUserId && n.IsEscalation);

        Assert.Equal(NotificationType.ManagerAttentionRequired, escalation.Type);
        Assert.Equal(NotificationCategory.ManagerEscalations, escalation.Category);
        Assert.Equal(NotificationPriority.High, escalation.Priority);
    }

    [Fact]
    public async Task TheEmployeeDashboardCountsLeadAlertsFromTheCentralStore()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var dashboard = await h.Reporting.GetEmployeeDashboardAsync(h.Sales);
        Assert.True(dashboard.UnreadNotifications > 0);

        await h.Inbox.MarkAllReadAsync(h.Notify(h.Sales), category: null);

        var cleared = await h.Reporting.GetEmployeeDashboardAsync(h.Sales);
        Assert.Equal(0, cleared.UnreadNotifications);
    }
}
