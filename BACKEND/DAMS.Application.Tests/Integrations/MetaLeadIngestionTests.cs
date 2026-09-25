using DAMS.Application.Common;
using DAMS.Application.Services.Integrations;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// End-to-end behaviour of the webhook → event → lead pipeline, with the Graph API faked at
/// its boundary. No Meta account, no network, no credentials.
/// </summary>
public class MetaLeadIngestionTests
{
    private static (string Name, string? Value)[] StandardFields =>
    [
        ("full_name", "Ali Khan"),
        ("phone_number", "+92 300 1234567"),
        ("email", "ali@example.com"),
        ("city", "Lahore")
    ];

    [Fact]
    public async Task AWebhookEvent_BecomesALeadWithItsFullAttribution()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead(
            "lead-1", StandardFields, platform: "fb", pageId: page.ExternalId,
            campaignName: "Summer Launch", adName: "Carousel A", formId: "form-1");

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        var processed = await h.Processor.ProcessPendingEventsAsync(10);

        Assert.Equal(1, processed);

        var lead = await h.Db.Leads.SingleAsync();
        Assert.Equal("Ali", lead.FirstName);
        Assert.Equal("Khan", lead.LastName);
        Assert.Equal("3001234567", lead.NormalizedPhone);
        Assert.Equal("ali@example.com", lead.Email);
        Assert.Equal("Lahore", lead.City);
        Assert.Equal("meta", lead.ExternalProvider);

        var submission = await h.Db.LeadExternalSubmissions.SingleAsync();
        Assert.Equal(connection.Id, submission.ExternalIntegrationConnectionId);
        Assert.Equal("facebook", submission.Platform);
        Assert.Equal("Summer Launch", submission.CampaignName);
        Assert.Equal("Carousel A", submission.AdName);
        Assert.Equal(page.ExternalId, submission.PageExternalId);
        Assert.NotNull(submission.RawPayloadJson);

        var stored = await h.Db.ExternalIntegrationEvents.SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Processed, stored.Status);
        Assert.Equal(lead.Id, stored.LeadId);
        Assert.Null(stored.LockedUntil);
    }

    [Fact]
    public async Task RedeliveringTheSameWebhook_CreatesNoSecondEvent()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        var body = MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1");

        Assert.Equal(1, await h.Intake.RecordAsync(body));
        Assert.Equal(0, await h.Intake.RecordAsync(body));

        Assert.Equal(1, await h.Db.ExternalIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task OneDeliveryRepeatingTheSameLead_RecordsItOnceAndKeepsTheRest()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        // Both changes name the same lead, alongside a genuinely different one. Letting the
        // duplicate through would make the unique index reject the whole batch and lose the
        // unrelated enquiry with it.
        var body = $$"""
            {
              "object": "page",
              "entry": [{
                "id": "{{page.ExternalId}}",
                "changes": [
                  { "field": "leadgen", "value": { "page_id": "{{page.ExternalId}}", "leadgen_id": "lead-1" } },
                  { "field": "leadgen", "value": { "page_id": "{{page.ExternalId}}", "leadgen_id": "lead-1" } },
                  { "field": "leadgen", "value": { "page_id": "{{page.ExternalId}}", "leadgen_id": "lead-2" } }
                ]
              }]
            }
            """;

        Assert.Equal(2, await h.Intake.RecordAsync(body));
        Assert.Equal(2, await h.Db.ExternalIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task ALeadWhosePhoneAndEmailMatchDifferentLeads_IsHeld_NotAddedToEither()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();
        await h.Leads.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Phone Owner", phone: "0300-1234567", email: "someone@example.com"), h.Leads.Admin);
        await h.Leads.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Email Owner", phone: "0321-7654321", email: "ali@example.com"), h.Leads.Admin);

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", StandardFields, pageId: page.ExternalId,
            campaignName: "Summer Launch", adName: "Corner units");
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));

        Assert.Equal(0, await h.Processor.ProcessPendingEventsAsync(10));

        // Done, not failed: retrying could never choose a lead, and nothing went wrong, so no
        // error is recorded. No lead was touched or created.
        var integrationEvent = await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Processed, integrationEvent.Status);
        Assert.Null(integrationEvent.LeadId);
        Assert.Null(integrationEvent.LastError);
        Assert.Equal(2, await h.Db.Leads.CountAsync());
        Assert.Empty(await h.Db.LeadExternalSubmissions.ToListAsync());

        var hold = await h.Db.LeadIntakeHolds.AsNoTracking().SingleAsync();
        Assert.Equal(("meta", "lead-1", LeadIntakeHoldStatus.Open), (hold.Provider, hold.ExternalLeadId, hold.Status));
        Assert.Equal(0, await h.Processor.ProcessPendingEventsAsync(10));

        // Resolved later, the receipt still carries the ad attribution captured when it arrived.
        var chosen = await h.Db.Leads.AsNoTracking().Where(l => l.FirstName == "Email Owner").Select(l => l.Id).SingleAsync();
        await h.Leads.Leads.ResolveIntakeHoldAsync(hold.Id, new DTOs.LeadDtos.ResolveLeadIntakeHoldDto { LeadId = chosen }, h.Leads.Admin);

        var submission = await h.Db.LeadExternalSubmissions.AsNoTracking().SingleAsync();
        Assert.Equal(chosen, submission.LeadId);
        Assert.Equal(("Summer Launch", "Corner units", page.ExternalId, page.Name),
            (submission.CampaignName, submission.AdName, submission.PageExternalId, submission.PageName));
        Assert.NotNull(submission.ExternalIntegrationConnectionId);
        Assert.NotNull(submission.RawPayloadJson);
        Assert.Contains("Ali Khan", submission.FieldDataJson);
    }

    [Fact]
    public async Task ProcessingAnAlreadyProcessedLead_CreatesNoSecondLead()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", StandardFields, pageId: page.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        // A processed event is not claimable, so a second sweep finds nothing to do.
        Assert.Equal(0, await h.Processor.ProcessPendingEventsAsync(10));
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task RetryingWhenTheSubmissionAlreadyExists_UpdatesTheEventWithoutCreatingAnotherLead()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        h.Options.MaxAttempts = 1;
        var (connection, page) = await h.ConnectPageAsync();
        h.Graph.Leads["existing-lead"] = FakeMetaGraphClient.Lead(
            "existing-lead", StandardFields, pageId: page.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "existing-lead"));
        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));
        var existingLead = await h.Db.Leads.SingleAsync();
        Assert.Single(await h.Db.LeadExternalSubmissions.ToListAsync());

        var failed = new ExternalIntegrationEvent
        {
            Provider = IntegrationProviders.Meta,
            ExternalIntegrationConnectionId = connection.Id,
            ExternalIntegrationResourceId = page.Id,
            EventType = "leadgen",
            EventKey = "manual-recovery:existing-lead",
            ResourceExternalId = page.ExternalId,
            RawPayloadJson = "{\"leadgen_id\":\"existing-lead\"}",
            Status = ExternalIntegrationEventStatus.Failed,
            ProcessedAt = DateTime.UtcNow,
            LastError = "Simulated failed delivery.",
            AvailableAt = DateTime.UtcNow
        };
        h.Db.ExternalIntegrationEvents.Add(failed);
        await h.Db.SaveChangesAsync();

        await h.Integration.RetryEventAsync(connection.Id, failed.Id, h.Leads.Admin);
        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));

        var recovered = await h.Db.ExternalIntegrationEvents.SingleAsync(e => e.Id == failed.Id);
        Assert.Equal(ExternalIntegrationEventStatus.Processed, recovered.Status);
        Assert.Equal(existingLead.Id, recovered.LeadId);
        Assert.Equal(1, await h.Db.Leads.CountAsync());
        Assert.Equal(1, await h.Db.LeadExternalSubmissions.CountAsync());
    }

    [Fact]
    public async Task TheSamePersonEnquiringTwice_ProducesOneLeadAndTwoSubmissions()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", StandardFields, pageId: page.ExternalId);
        h.Graph.Leads["lead-2"] = FakeMetaGraphClient.Lead("lead-2", StandardFields, pageId: page.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-2"));
        await h.Processor.ProcessPendingEventsAsync(10);

        Assert.Equal(1, await h.Db.Leads.CountAsync());
        Assert.Equal(2, await h.Db.LeadExternalSubmissions.CountAsync());
    }

    [Fact]
    public async Task EveryFormAnswerIsPreserved_IncludingOnesDamsDoesNotUnderstand()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1",
        [
            ("full_name", "Ali Khan"),
            ("email", "ali@example.com"),
            ("when_are_you_looking_to_buy", "Within 3 months"),
            ("preferred_floor", "Ground")
        ], pageId: page.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        var submission = await h.Db.LeadExternalSubmissions.SingleAsync();
        Assert.NotNull(submission.FieldDataJson);
        Assert.Contains("when_are_you_looking_to_buy", submission.FieldDataJson);
        Assert.Contains("Within 3 months", submission.FieldDataJson);
        Assert.Contains("Ground", submission.FieldDataJson);
    }

    [Fact]
    public async Task ABudgetQuestion_DoesNotSilentlyPopulateTheBudgetFields()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1",
        [
            ("full_name", "Ali Khan"),
            ("email", "ali@example.com"),
            ("what_is_your_budget", "50 lakh"),
            ("which_project", "Palm Residency")
        ], pageId: page.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        var lead = await h.Db.Leads.SingleAsync();
        // Guessing a number from free text would put invented figures in front of the sales
        // team. The answers are kept, but they map to nothing.
        Assert.Null(lead.BudgetMin);
        Assert.Null(lead.BudgetMax);
        Assert.Null(lead.InterestedProjectId);

        var submission = await h.Db.LeadExternalSubmissions.SingleAsync();
        Assert.Contains("50 lakh", submission.FieldDataJson!);
        Assert.Contains("Palm Residency", submission.FieldDataJson!);
    }

    [Theory]
    [InlineData("ig", "instagram", "instagram")]
    [InlineData("fb", "facebook", "facebook")]
    [InlineData(null, null, "meta")]
    [InlineData("something_new", null, "meta")]
    public async Task ThePlatformIsRecordedOnlyWhenMetaStatesIt(
        string? platform, string? expectedPlatform, string expectedSourceCode)
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead(
            "lead-1", StandardFields, platform: platform, pageId: page.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        var submission = await h.Db.LeadExternalSubmissions.SingleAsync();
        // Every lead-ad webhook arrives through a Page, so an unstated platform must never be
        // assumed to be Facebook — that would relabel Instagram leads wholesale.
        Assert.Equal(expectedPlatform, submission.Platform);

        var lead = await h.Db.Leads.Include(l => l.Source).SingleAsync();
        Assert.Equal(expectedSourceCode, lead.Source.Code);
    }

    [Fact]
    public async Task ALeadFromAPageNobodyEnabled_IsIgnoredRatherThanIngested()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync(enabled: false);

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", StandardFields, pageId: page.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        Assert.Equal(0, await h.Db.Leads.CountAsync());

        // Recorded, not discarded: an admin can see the enquiry arrived and why nothing happened.
        var stored = await h.Db.ExternalIntegrationEvents.SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Ignored, stored.Status);
        Assert.NotNull(stored.LastError);
    }

    [Fact]
    public async Task AWebhookForAnUnknownPage_IsRecordedButNeverBecomesALead()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        await h.ConnectPageAsync(pageId: "page-1");

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody("page-999", "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        Assert.Equal(0, await h.Db.Leads.CountAsync());
        var stored = await h.Db.ExternalIntegrationEvents.SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Ignored, stored.Status);
    }

    [Fact]
    public async Task ATransientFailure_IsRetriedWithGrowingBackoff()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        h.Graph.LeadFailures.Enqueue(new MetaTransientException("Rate limited."));
        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", StandardFields, pageId: page.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        var stored = await h.Db.ExternalIntegrationEvents.SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Retry, stored.Status);
        Assert.Equal(1, stored.Attempts);
        Assert.True(stored.AvailableAt > DateTime.UtcNow.AddSeconds(30), "the first retry waits about a minute");
        Assert.Null(stored.LockedUntil);
        Assert.Equal(0, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task ATransientFailureThatNeverClears_EventuallyFails()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));

        // MaxAttempts is 3 in the harness; drive it past that, making each attempt due.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            h.Graph.LeadFailures.Enqueue(new MetaTransientException("Still rate limited."));

            var due = await h.Db.ExternalIntegrationEvents.SingleAsync();
            due.AvailableAt = DateTime.UtcNow.AddSeconds(-1);
            await h.Db.SaveChangesAsync();

            await h.Processor.ProcessPendingEventsAsync(10);
        }

        var stored = await h.Db.ExternalIntegrationEvents.SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Failed, stored.Status);
        Assert.NotNull(stored.ProcessedAt);
    }

    [Fact]
    public async Task ALeadIdTooLongToStore_FailsAtOnce_AndTheNextLeadStillArrives()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        // Meta answers with an id no lead or submission column can hold. Retrying cannot
        // change that, so the event is failed on the first attempt.
        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead(new string('9', 250), StandardFields, pageId: page.ExternalId);
        h.Graph.Leads["lead-2"] = FakeMetaGraphClient.Lead("lead-2", StandardFields, pageId: page.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-2"));

        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));

        var events = await h.Db.ExternalIntegrationEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Failed, events[0].Status);
        Assert.Equal(1, events[0].Attempts);
        Assert.Null(events[0].LeadId);
        Assert.Equal(ExternalIntegrationEventStatus.Processed, events[1].Status);
        Assert.Equal("lead-2", (await h.Db.Leads.SingleAsync()).ExternalLeadId);
    }

    [Fact]
    public async Task AWebhookIdTooLongToStore_IsRecordedAsFailed_BesideTheValidEventInTheSameDelivery()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        var oversizedId = new string('9', 400);
        var body = $$$"""
            {
              "object": "page",
              "entry": [{
                "id": "{{{page.ExternalId}}}",
                "changes": [
                  {"field": "leadgen", "value": {"page_id": "{{{page.ExternalId}}}", "leadgen_id": "{{{oversizedId}}}"}},
                  {"field": "leadgen", "value": {"page_id": "{{{page.ExternalId}}}", "leadgen_id": "lead-2"}}
                ]
              }]
            }
            """;

        Assert.Equal(2, await h.Intake.RecordAsync(body));
        // The oversized event's stand-in key is stable, so a redelivery is still a duplicate.
        Assert.Equal(0, await h.Intake.RecordAsync(body));

        var events = await h.Db.ExternalIntegrationEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Equal(ExternalIntegrationEventStatus.Failed, events[0].Status);
        Assert.True(events[0].EventKey.Length <= 300);
        Assert.Null(events[0].ResourceExternalId);
        Assert.Contains(oversizedId, events[0].RawPayloadJson);
        Assert.Equal(ExternalIntegrationEventStatus.Pending, events[1].Status);
    }

    [Fact]
    public async Task AnEventAbandonedOnItsLastAttempt_IsFailed_InsteadOfReclaimedForever()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", StandardFields, pageId: page.ExternalId);
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));

        // What a worker that died mid-attempt leaves behind: its last attempt counted, still
        // Processing, the lease since lapsed. MaxAttempts is 3 in the harness.
        var abandoned = await h.Db.ExternalIntegrationEvents.SingleAsync();
        abandoned.Status = ExternalIntegrationEventStatus.Processing;
        abandoned.Attempts = 3;
        abandoned.LockedUntil = DateTime.UtcNow.AddMinutes(-1);
        await h.Db.SaveChangesAsync();

        Assert.Equal(0, await h.Processor.ProcessPendingEventsAsync(10));

        var stored = await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Failed, stored.Status);
        Assert.Equal(3, stored.Attempts);
        Assert.Null(stored.LockedUntil);
        Assert.Empty(h.Graph.LeadRequests);
        Assert.Equal(0, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task ARejectedToken_FlagsTheConnectionInsteadOfBurningRetries()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync();

        h.Graph.LeadFailures.Enqueue(new MetaAuthorizationException("Error validating access token."));

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        var updated = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        Assert.Equal(ExternalIntegrationConnectionStatus.NeedsReauthorization, updated.Status);
        Assert.NotNull(updated.LastError);

        var stored = await h.Db.ExternalIntegrationEvents.SingleAsync();
        // Parked, not failed, and the attempt is not counted — the enquiry must survive the
        // window during which nobody has reconnected yet.
        Assert.Equal(ExternalIntegrationEventStatus.Retry, stored.Status);
        Assert.Equal(0, stored.Attempts);
        Assert.True(stored.AvailableAt > DateTime.UtcNow.AddHours(5));
    }

    [Fact]
    public async Task AnUnreadableStoredCredential_IsTreatedAsANeedToReconnect()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync(new UnreadableSecretProtector());
        var (connection, page) = await h.ConnectPageAsync();

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        var updated = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        Assert.Equal(ExternalIntegrationConnectionStatus.NeedsReauthorization, updated.Status);
        Assert.Equal(0, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task EachConnectionUsesItsOwnPageToken()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, pageA) = await h.ConnectPageAsync(pageId: "page-a", externalAccountId: "meta-user-a");
        var (_, pageB) = await h.ConnectPageAsync(pageId: "page-b", externalAccountId: "meta-user-b");

        h.Graph.Leads["lead-a"] = FakeMetaGraphClient.Lead("lead-a", StandardFields, pageId: pageA.ExternalId);
        h.Graph.Leads["lead-b"] = FakeMetaGraphClient.Lead("lead-b",
            [("full_name", "Sana Tariq"), ("email", "sana@example.com")], pageId: pageB.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(pageA.ExternalId, "lead-a"));
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(pageB.ExternalId, "lead-b"));
        await h.Processor.ProcessPendingEventsAsync(10);

        // Crossing tokens between connections would leak one client's access to another's data.
        Assert.Contains(("lead-a", "page-token-page-a"), h.Graph.LeadRequests);
        Assert.Contains(("lead-b", "page-token-page-b"), h.Graph.LeadRequests);
        Assert.Equal(2, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task AnEventLeasedByAnotherWorker_IsNotClaimed()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", StandardFields, pageId: page.ExternalId);
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));

        var held = await h.Db.ExternalIntegrationEvents.SingleAsync();
        held.Status = ExternalIntegrationEventStatus.Processing;
        held.LockedUntil = DateTime.UtcNow.AddMinutes(5);
        held.LockedBy = "another-worker:1";
        await h.Db.SaveChangesAsync();

        Assert.Equal(0, await h.Processor.ProcessPendingEventsAsync(10));
        Assert.Equal(0, await h.Db.Leads.CountAsync());

        // Once the lease lapses, whichever worker runs next picks the work back up.
        held.LockedUntil = DateTime.UtcNow.AddMinutes(-1);
        await h.Db.SaveChangesAsync();

        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));
    }

    [Fact]
    public async Task ALeadFromMeta_RaisesTheOrdinaryNewLeadNotification()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", StandardFields, pageId: page.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        // Going through ILeadService.IngestAsync rather than creating leads directly is what
        // makes this work without the integration knowing notifications exist.
        var notifications = await h.Db.Notifications
            .Where(n => n.Type == NotificationType.LeadCreated)
            .ToListAsync();

        Assert.NotEmpty(notifications);
    }

    [Fact]
    public async Task TheEventProcessor_NeverClaimsAnotherProvidersEvent()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", StandardFields, pageId: page.ExternalId);
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));

        // A future provider (Google, a portal) would share this same table. Nothing about the
        // claim query may pick up its rows — the Meta worker must not try to fetch a Google
        // lead through the Graph API and fail it before Google's own worker ever sees it.
        h.Db.ExternalIntegrationEvents.Add(new DAMS.Domain.Entities.ExternalIntegrationEvent
        {
            Provider = "google",
            EventType = "leadgen",
            EventKey = "google:page-1:lead-1",
            RawPayloadJson = "{}",
            AvailableAt = DateTime.UtcNow
        });
        await h.Db.SaveChangesAsync();

        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));

        var googleEvent = await h.Db.ExternalIntegrationEvents.SingleAsync(e => e.Provider == "google");
        Assert.Equal(ExternalIntegrationEventStatus.Pending, googleEvent.Status);
        Assert.Equal(0, googleEvent.Attempts);
    }

    [Fact]
    public async Task AConnectionMissingOnlyAdsRead_StillDeliversLeads()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        // Every lead-critical scope is granted; only the advertising-discovery one is not —
        // realistic for an app still waiting on Meta's Advanced Access review.
        h.Graph.Authorization.GrantedScopes =
        [
            DAMS.Application.Common.MetaScopes.PagesShowList,
            DAMS.Application.Common.MetaScopes.PagesReadEngagement,
            DAMS.Application.Common.MetaScopes.PagesManageMetadata,
            DAMS.Application.Common.MetaScopes.LeadsRetrieval
        ];

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);
        var state = System.Web.HttpUtility.ParseQueryString(new Uri(start.AuthorizationUrl).Query)["state"]!;
        await h.Integration.CompleteCallbackAsync("code-1", state, null);

        var connection = await h.Db.ExternalIntegrationConnections.SingleAsync();
        // Missing ads_read must not be treated as a reason lead delivery cannot work.
        Assert.Equal(ExternalIntegrationConnectionStatus.Connected, connection.Status);

        var page = new DAMS.Domain.Entities.ExternalIntegrationResource
        {
            ExternalIntegrationConnectionId = connection.Id,
            Provider = "meta",
            ResourceType = "facebook_page",
            ExternalId = "page-1",
            IsEnabled = true,
            IsActive = true,
            ResourceTokenProtected = connection.AccessTokenProtected
        };
        h.Db.ExternalIntegrationResources.Add(page);
        await h.Db.SaveChangesAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", StandardFields, pageId: "page-1");
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody("page-1", "lead-1"));

        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task ADisconnectedConnection_StopsProducingLeads()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", StandardFields, pageId: page.ExternalId);
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));

        await h.Integration.DisconnectAsync(connection.Id, h.Leads.Admin);
        await h.Processor.ProcessPendingEventsAsync(10);

        Assert.Equal(0, await h.Db.Leads.CountAsync());
        var stored = await h.Db.ExternalIntegrationEvents.SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Ignored, stored.Status);
    }
}
