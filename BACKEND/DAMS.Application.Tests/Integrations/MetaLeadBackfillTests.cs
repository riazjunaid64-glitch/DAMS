using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.Services.Integrations;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// KAN-35: leads the webhook never delivered are read back from each form's own edge and handed
/// to the normal event processor, without duplicating anything the webhook did deliver.
/// </summary>
public class MetaLeadBackfillTests
{
    private static MetaLead Lead(string leadgenId, string name, DateTime createdAt, string formId = "form-1")
    {
        var lead = FakeMetaGraphClient.Lead(leadgenId,
            [("full_name", name), ("phone_number", $"+92 300 {Math.Abs(leadgenId.GetHashCode()) % 10000000:0000000}")],
            formId: formId);
        lead.CreatedTime = createdAt;
        return lead;
    }

    /// <summary>A connected, enabled Page with one synced lead form.</summary>
    private static async Task<(ExternalIntegrationConnection Connection, ExternalIntegrationResource Page)> SetUpAsync(
        MetaIntegrationHarness h, bool enabled = true)
    {
        var (connection, page) = await h.ConnectPageAsync(enabled: enabled);
        h.Db.ExternalIntegrationResources.Add(new ExternalIntegrationResource
        {
            ExternalIntegrationConnectionId = connection.Id,
            Provider = IntegrationProviders.Meta,
            ResourceType = ExternalResourceTypes.LeadForm,
            ExternalId = "form-1",
            ParentExternalId = page.ExternalId,
            Name = "Floria enquiry",
            IsActive = true
        });
        await h.Db.SaveChangesAsync();
        return (connection, page);
    }

    private static ImportMetaLeadsDto ForPage(ExternalIntegrationResource page, int daysBack) =>
        new() { ResourceId = page.Id, Since = DateOnly.FromDateTime(PakistanTime.Today.AddDays(-daysBack)) };

    [Fact]
    public async Task AnImport_QueuesMissedLeadsAsBackfillEvents_ThatBecomeLeadsThroughTheNormalPath()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        var lead = Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddDays(-3));
        h.Graph.FormLeads["form-1"] = [lead];
        h.Graph.Leads["lead-1"] = lead;

        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 7), h.Leads.Admin);

        Assert.Equal((1, 1, 0, 0), (result.Found, result.New, result.AlreadyInDams, result.Failed));
        var queued = await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(MetaLeadBackfillService.BackfillEventType, queued.EventType);
        Assert.Equal(ExternalIntegrationEventStatus.Pending, queued.Status);
        // Read with the Page's own token — the credential leads are fetched with.
        Assert.Equal("page-token-page-1", Assert.Single(h.Graph.FormLeadRequests).Token);

        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));
        var created = await h.Db.Leads.AsNoTracking().SingleAsync();
        Assert.Equal("Ali", created.FirstName);
        Assert.Equal(lead.CreatedTime, created.ExternalSubmittedAt);
    }

    [Fact]
    public async Task ALeadTheWebhookAlreadyDelivered_IsCountedNotRepeated()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        var lead = Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddHours(-2));
        h.Graph.Leads["lead-1"] = lead;
        h.Graph.FormLeads["form-1"] = [lead];
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 1), h.Leads.Admin);
        var again = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 1), h.Leads.Admin);

        Assert.Equal((1, 0, 1), (result.Found, result.New, result.AlreadyInDams));
        Assert.Equal((1, 0, 1), (again.Found, again.New, again.AlreadyInDams));
        Assert.Equal(1, await h.Db.ExternalIntegrationEvents.CountAsync());
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task ALeadIgnoredWhileItsPageWasOff_IsRecoveredOnceThePageIsOn()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h, enabled: false);
        var lead = Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddHours(-2));
        h.Graph.Leads["lead-1"] = lead;
        h.Graph.FormLeads["form-1"] = [lead];
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        Assert.Equal(ExternalIntegrationEventStatus.Ignored, (await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync()).Status);

        page.IsEnabled = true;
        await h.Db.SaveChangesAsync();
        var result = await h.Backfill.ReconcileAsync(connection.Id);

        Assert.Equal(1, result.New);
        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));
        Assert.Equal(1, await h.Db.Leads.CountAsync());
        Assert.Equal(1, await h.Db.ExternalIntegrationEvents.CountAsync());
        // The webhook did deliver it, so it is no sign of a webhook dropping leads.
        Assert.Equal(0, (await h.Db.ExternalIntegrationConnections.AsNoTracking().SingleAsync()).ReconciliationMissedLeads);
    }

    [Fact]
    public async Task EverySync_ReconcilesTheConfiguredWindowOfEnabledPages()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        h.Graph.Pages = [new() { ResourceType = ExternalResourceTypes.FacebookPage, ExternalId = page.ExternalId, Name = page.Name, ResourceToken = "page-token-page-1" }];
        h.Graph.LeadForms = [new() { ResourceType = ExternalResourceTypes.LeadForm, ExternalId = "form-1", ParentExternalId = page.ExternalId, Name = "Floria enquiry" }];
        h.Graph.FormLeads["form-1"] =
        [
            Lead("lead-recent", "Recent Buyer", DateTime.UtcNow.AddHours(-30)),
            Lead("lead-old", "Old Buyer", DateTime.UtcNow.AddHours(-60))
        ];

        await h.Sync.SyncConnectionAsync(connection.Id);

        var request = Assert.Single(h.Graph.FormLeadRequests);
        Assert.InRange(request.Since, DateTime.UtcNow.AddHours(-48.1), DateTime.UtcNow.AddHours(-47.9));
        var queued = await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync();
        Assert.EndsWith(":lead-recent", queued.EventKey);

        h.Graph.FormLeadRequests.Clear();
        h.Options.ReconciliationLookbackHours = 0;
        await h.Sync.SyncConnectionAsync(connection.Id);
        Assert.Empty(h.Graph.FormLeadRequests);
    }

    [Fact]
    public async Task RecoveredLeads_DoNotHideAWebhookThatStoppedDelivering()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        h.Db.ExternalIntegrationEvents.Add(new ExternalIntegrationEvent
        {
            Provider = IntegrationProviders.Meta, ExternalIntegrationConnectionId = connection.Id,
            ExternalIntegrationResourceId = page.Id, EventType = "leadgen",
            EventKey = MetaWebhookIntakeService.EventKey(connection.Id, page.ExternalId, "lead-webhook"),
            RawPayloadJson = "{}", ReceivedAt = DateTime.UtcNow.AddDays(-5), Status = ExternalIntegrationEventStatus.Processed
        });
        await h.Db.SaveChangesAsync();
        h.Graph.FormLeads["form-1"] = [Lead("lead-recovered", "Ali Khan", DateTime.UtcNow.AddHours(-5))];
        await h.Backfill.ReconcileAsync(connection.Id);

        h.Options.QuietPageAlertDays = 3;

        await h.Alerts.RaiseAlertsAsync();
        Assert.Single(await h.Db.Notifications.Where(n => n.Title.StartsWith("No Meta leads for 3 days")).ToListAsync());
        var shown = Assert.Single(await h.Integration.GetConnectionsAsync());
        Assert.True(shown.LastLeadReceivedAt < DateTime.UtcNow.AddDays(-4));
    }

    [Fact]
    public async Task ReconciliationStillRuns_WhenMetaRefusesTheAccountsSignIn()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await SetUpAsync(h);
        h.Graph.DiscoveryFailure = new MetaAuthorizationException("Session has expired.", code: 190);
        h.Graph.FormLeads["form-1"] = [Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddHours(-3))];

        await h.Sync.SyncConnectionAsync(connection.Id);

        Assert.Equal(1, await h.Db.ExternalIntegrationEvents.CountAsync());
    }

    [Theory]
    [InlineData(91)]
    [InlineData(-1)]
    public async Task AnImport_IsLimitedToNinetyDaysAndNotTheFuture(int daysBack)
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Backfill.ImportAsync(connection.Id, ForPage(page, daysBack), h.Leads.Admin));
        Assert.Empty(h.Graph.FormLeadRequests);
    }

    [Fact]
    public async Task AnImportOfExactlyNinetyDays_IsAllowed()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);

        await h.Backfill.ImportAsync(connection.Id, new ImportMetaLeadsDto
        {
            ResourceId = page.Id,
            Since = DateOnly.FromDateTime(PakistanTime.Today.AddDays(-89))
        }, h.Leads.Admin);

        Assert.Single(h.Graph.FormLeadRequests);
    }

    [Fact]
    public async Task APermissionRefusal_IsReportedAndLeavesTheConnectionAlone()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        h.Graph.FormLeadsFailure = new MetaAuthorizationException("(#200) Requires pages_manage_ads permission", code: 200);

        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 7), h.Leads.Admin);

        Assert.Equal((0, 0, 1), (result.Found, result.New, result.Failed));
        Assert.Contains("pages_manage_ads", result.Warning);
        Assert.Equal(ExternalIntegrationConnectionStatus.Connected,
            (await h.Db.ExternalIntegrationConnections.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task AnImport_IsForAdminsAndForEnabledPagesOnly()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h, enabled: false);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Backfill.ImportAsync(connection.Id, ForPage(page, 7), h.Leads.Manager));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Backfill.ImportAsync(connection.Id, ForPage(page, 7), h.Leads.Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Backfill.ImportAsync(connection.Id, new ImportMetaLeadsDto { Since = DateOnly.FromDateTime(PakistanTime.Today) }, h.Leads.Admin));
    }

    [Fact]
    public async Task OneFormCanBeImportedOnItsOwn()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await SetUpAsync(h);
        h.Graph.FormLeads["form-1"] = [Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddDays(-2))];

        var result = await h.Backfill.ImportAsync(connection.Id, new ImportMetaLeadsDto
        {
            FormExternalId = "form-1",
            Since = DateOnly.FromDateTime(PakistanTime.Today.AddDays(-7))
        }, h.Leads.Admin);

        Assert.Equal(1, result.New);
    }

    // ── Old leads arrive quietly (open decision 5, as recommended on KAN-35) ─────────

    private static Task<List<Notification>> NewLeadAlertsAsync(MetaIntegrationHarness h, int leadId) =>
        h.Db.Notifications.AsNoTracking()
            .Where(n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId
                        && n.Type == NotificationType.LeadCreated)
            .ToListAsync();

    [Fact]
    public async Task AnOldRecoveredLead_IsAddedWithoutANewLeadAlert_WhileARecentOneAlertsAsUsual()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        var old = Lead("lead-old", "Old Buyer", DateTime.UtcNow.AddDays(-10));
        var recent = Lead("lead-recent", "Recent Buyer", DateTime.UtcNow.AddHours(-5));
        h.Graph.FormLeads["form-1"] = [old, recent];
        h.Graph.Leads["lead-old"] = old;
        h.Graph.Leads["lead-recent"] = recent;

        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 30), h.Leads.Admin);
        Assert.Equal((2, 2, 1), (result.Found, result.New, result.AddedWithoutAlert));
        Assert.Equal(2, await h.Processor.ProcessPendingEventsAsync(10));

        var oldLead = await h.Db.Leads.AsNoTracking().SingleAsync(l => l.ExternalLeadId == "lead-old");
        var recentLead = await h.Db.Leads.AsNoTracking().SingleAsync(l => l.ExternalLeadId == "lead-recent");
        Assert.Empty(await NewLeadAlertsAsync(h, oldLead.Id));
        Assert.NotEmpty(await NewLeadAlertsAsync(h, recentLead.Id));

        // Still dated when Meta took it, and its history says why nobody was told.
        Assert.Equal(old.CreatedTime, oldLead.ExternalSubmittedAt);
        Assert.Contains(await h.Db.LeadActivities.AsNoTracking().Where(a => a.LeadId == oldLead.Id).ToListAsync(),
            a => a.Type == LeadActivityType.LeadCreated && a.Summary.Contains("no new-lead alert"));
    }

    [Fact]
    public async Task AnOldLeadDeliveredWhileItsPageWasOff_IsAlsoRecoveredQuietly()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h, enabled: false);
        var lead = Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddDays(-20));
        h.Graph.Leads["lead-1"] = lead;
        h.Graph.FormLeads["form-1"] = [lead];
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));

        page.IsEnabled = true;
        await h.Db.SaveChangesAsync();
        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 30), h.Leads.Admin);
        await h.Processor.ProcessPendingEventsAsync(10);

        Assert.Equal((1, 1), (result.New, result.AddedWithoutAlert));
        var reopened = await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(MetaLeadBackfillService.BackfillEventType, reopened.EventType);
        Assert.Empty(await NewLeadAlertsAsync(h, (await h.Db.Leads.AsNoTracking().SingleAsync()).Id));
    }

    [Fact]
    public async Task AnOldWebhookLead_StillAlerts()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await SetUpAsync(h);
        // Parked for days while the connection waited to be reconnected, say: it came by webhook.
        h.Graph.Leads["lead-1"] = Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddDays(-5));
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        Assert.NotEmpty(await NewLeadAlertsAsync(h, (await h.Db.Leads.AsNoTracking().SingleAsync()).Id));
    }

    // ── Failed events are not "already in DAMS" ─────────────────────────────────────

    [Fact]
    public async Task ALeadWhoseEventFailed_IsCountedApart_AndLeftForTheEventList()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        h.Db.ExternalIntegrationEvents.Add(new ExternalIntegrationEvent
        {
            Provider = IntegrationProviders.Meta, ExternalIntegrationConnectionId = connection.Id,
            ExternalIntegrationResourceId = page.Id, EventType = "leadgen",
            EventKey = MetaWebhookIntakeService.EventKey(connection.Id, page.ExternalId, "lead-1"),
            RawPayloadJson = "{}", ReceivedAt = DateTime.UtcNow.AddDays(-1),
            Status = ExternalIntegrationEventStatus.Failed, LastError = "Lead lead-1 does not exist."
        });
        await h.Db.SaveChangesAsync();
        h.Graph.FormLeads["form-1"] = [Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddDays(-1))];

        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 7), h.Leads.Admin);

        Assert.Equal((1, 0, 0, 1), (result.Found, result.New, result.AlreadyInDams, result.PreviouslyFailed));
        Assert.Equal(ExternalIntegrationEventStatus.Failed,
            (await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync()).Status);
    }

    // ── Windows larger than one read ─────────────────────────────────────────────────

    [Fact]
    public async Task AWindowWithMoreLeadsThanOneRead_IsSplitUntilEveryLeadIsRead()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        h.Graph.FormLeadsPerRead = 2;
        h.Graph.FormLeads["form-1"] = Enumerable.Range(1, 7)
            .Select(day => Lead($"lead-{day}", $"Buyer Number{day}", DateTime.UtcNow.AddDays(-day).AddMinutes(-30)))
            .ToList();

        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 10), h.Leads.Admin);

        Assert.Equal((7, 7, 0), (result.Found, result.New, result.Failed));
        Assert.Null(result.Warning);
        Assert.True(h.Graph.FormLeadRequests.Count > 1);
        Assert.Equal(7, await h.Db.ExternalIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task AnHourWithMoreLeadsThanOneRead_IsReportedAsIncomplete()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        h.Graph.FormLeadsPerRead = 1;
        var at = DateTime.UtcNow.AddHours(-3);
        h.Graph.FormLeads["form-1"] = [Lead("lead-1", "Ali Khan", at), Lead("lead-2", "Sara Khan", at)];

        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 1), h.Leads.Admin);

        Assert.Contains("within one hour", result.Warning);
        Assert.Equal(1, result.Found);
    }

    // ── The last minutes belong to the webhook ───────────────────────────────────────

    [Fact]
    public async Task Reconciliation_LeavesTheLastMinutesToTheWebhook()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        var fresh = Lead("lead-fresh", "Ali Khan", DateTime.UtcNow.AddMinutes(-2));
        h.Graph.FormLeads["form-1"] = [fresh];
        h.Graph.Leads["lead-fresh"] = fresh;

        var result = await h.Backfill.ReconcileAsync(connection.Id);

        var request = Assert.Single(h.Graph.FormLeadRequests);
        Assert.InRange(request.Until,
            DateTime.UtcNow.AddMinutes(-MetaLeadBackfillService.WebhookGraceMinutes - 1),
            DateTime.UtcNow.AddMinutes(-MetaLeadBackfillService.WebhookGraceMinutes));
        Assert.Equal(0, result.Found);

        // Its webhook, arriving now, is recorded as the delivery it is.
        Assert.Equal(1, await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-fresh")));
        Assert.NotNull(Assert.Single(await h.Integration.GetConnectionsAsync()).LastLeadReceivedAt);
    }

    // ── One bad lead does not cost the import ────────────────────────────────────────

    [Fact]
    public async Task AnUnexpectedErrorOnOneLead_FailsOnlyThatLead()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        h.Graph.FormLeads["form-1"] =
        [
            Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddHours(-3)),
            Lead("lead-bad", "Sara Khan", DateTime.UtcNow.AddHours(-4)),
            Lead("lead-3", "Omar Khan", DateTime.UtcNow.AddHours(-5))
        ];
        var failing = true;
        void Fail(object? sender, SavingChangesEventArgs e)
        {
            if (failing && h.Db.ChangeTracker.Entries<ExternalIntegrationEvent>()
                    .Any(x => x.State == EntityState.Added && x.Entity.EventKey.EndsWith(":lead-bad")))
                throw new InvalidOperationException("The database is unavailable.");
        }
        h.Db.SavingChanges += Fail;

        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 1), h.Leads.Admin);

        Assert.Equal((3, 2, 1), (result.Found, result.New, result.Failed));
        Assert.Contains("could not be queued", result.Warning);

        // Nothing of the failed save is left behind to break the next one.
        failing = false;
        var again = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 1), h.Leads.Admin);
        Assert.Equal((1, 2), (again.New, again.AlreadyInDams));
        h.Db.SavingChanges -= Fail;
    }

    // ── Scheduled reconciliation is heard ────────────────────────────────────────────

    private static Task<List<Notification>> IntegrationAlertsAsync(MetaIntegrationHarness h, string titlePrefix) =>
        h.Db.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationType.IntegrationAttentionRequired && n.Title.StartsWith(titlePrefix)
                        && n.RecipientUserId == h.Leads.AdminUserId)
            .ToListAsync();

    [Fact]
    public async Task ReconciliationThatKeepsFailing_AlertsEveryAdmin_AndAOneOffDoesNot()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await SetUpAsync(h);
        h.Graph.FormLeadsFailure = new MetaAuthorizationException("(#200) Requires pages_manage_ads permission", code: 200);

        await h.Backfill.ReconcileAsync(connection.Id);
        await h.Alerts.RaiseAlertsAsync();
        Assert.Empty(await IntegrationAlertsAsync(h, "Missed Meta leads cannot be recovered"));

        await h.Backfill.ReconcileAsync(connection.Id);
        await h.Alerts.RaiseAlertsAsync();
        var alert = Assert.Single(await IntegrationAlertsAsync(h, "Missed Meta leads cannot be recovered"));
        Assert.Equal(h.Leads.AdminUserId, alert.RecipientUserId);
        Assert.Contains("pages_manage_ads", alert.Message);
        // Reported, never acted on: the connection's own status is the event processor's call.
        Assert.Equal(ExternalIntegrationConnectionStatus.Connected,
            (await h.Db.ExternalIntegrationConnections.AsNoTracking().SingleAsync()).Status);

        // Once it gets through, the episode is over.
        h.Graph.FormLeadsFailure = null;
        await h.Backfill.ReconcileAsync(connection.Id);
        var stored = await h.Db.ExternalIntegrationConnections.AsNoTracking().SingleAsync();
        Assert.Null(stored.ReconciliationError);
        Assert.Null(stored.ReconciliationFailingSince);
    }

    [Fact]
    public async Task AnUnreadableCredential_IsRecordedAsAFailingReconciliation()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync(new UnreadableSecretProtector());
        var (connection, _) = await SetUpAsync(h);

        await h.Backfill.ReconcileAsync(connection.Id);

        var stored = await h.Db.ExternalIntegrationConnections.AsNoTracking().SingleAsync();
        Assert.Contains("could not be read", stored.ReconciliationError);
        Assert.NotNull(stored.ReconciliationFailingSince);
        Assert.Empty(h.Graph.FormLeadRequests);
    }

    [Fact]
    public async Task LeadsTheWebhookNeverDelivered_AlertEveryAdminOnceADay()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await SetUpAsync(h);
        h.Graph.FormLeads["form-1"] = [Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddHours(-5))];

        await h.Backfill.ReconcileAsync(connection.Id);
        await h.Alerts.RaiseAlertsAsync();
        h.Graph.FormLeads["form-1"].Add(Lead("lead-2", "Sara Khan", DateTime.UtcNow.AddHours(-4)));
        await h.Backfill.ReconcileAsync(connection.Id);
        await h.Alerts.RaiseAlertsAsync();

        var alert = Assert.Single(await IntegrationAlertsAsync(h, "Meta webhook missed leads"));
        Assert.StartsWith("1 lead from", alert.Message);
    }
}
