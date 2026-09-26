using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.Services.Integrations;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// An administrator links a Meta lead form to a project and maps its answers to lead fields.
/// Built on the live Floria Heights form: three qualifying questions before the contact details.
/// </summary>
public class LeadFormMappingTests
{
    private const string FormId = "1180848317322015";
    private const string InstallmentQuestion = "are_you_interested_in_a_5-year_installment_plan?";
    private const string ApartmentQuestion = "which_apartment_type_are_you_interested_in?";
    private const string BuyingForQuestion = "are_you_buying_for_?";

    private static List<LeadFormQuestionDto> FloriaQuestions =>
    [
        Question(InstallmentQuestion, "Are you interested in a 5-year installment plan?",
            ("yes", "Yes"), ("need_more_details", "Need more details"), ("no_(_on_cash)", "No ( on cash)")),
        Question(ApartmentQuestion, "Which apartment type are you interested in?",
            ("studio_apartment", "Studio Apartment"), ("1_bedroom_apartment", "1 Bedroom Apartment"),
            ("2_bedroom_apartment", "2 Bedroom Apartment"), ("3_bedroom_apartment", "3 Bedroom Apartment")),
        Question(BuyingForQuestion, "Are you buying for ?",
            ("investment", "Investment"), ("personal_living", "Personal Living")),
        new LeadFormQuestionDto { Key = "full_name", Label = "Full name", Type = "FULL_NAME" },
        new LeadFormQuestionDto { Key = "phone_number", Label = "Phone number", Type = "PHONE" }
    ];

    private static SaveLeadFormMappingDto FloriaMapping(int projectId) => new()
    {
        InterestedProjectId = projectId,
        Answers =
        [
            Mapping(InstallmentQuestion, LeadFormAnswerTarget.PaymentPreference,
                ("yes", "Installments"), ("need_more_details", "NeedsDetails"), ("no_(_on_cash)", "Cash")),
            Mapping(ApartmentQuestion, LeadFormAnswerTarget.PropertyType,
                ("studio_apartment", "Studio Apartment"), ("1_bedroom_apartment", "1 Bedroom Apartment"),
                ("2_bedroom_apartment", "2 Bedroom Apartment"), ("3_bedroom_apartment", "3 Bedroom Apartment")),
            Mapping(BuyingForQuestion, LeadFormAnswerTarget.PurchaseIntent,
                ("investment", "Investment"), ("personal_living", "SelfUse"))
        ]
    };

    // ── Ingestion ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AMappedFormsLead_GetsItsProjectAndAnswers_AndTheAnswersReadAsTheFormWordsThem()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var page = await SetUpFloriaAsync(h);

        var lead = await IngestAsync(h, page, "lead-1",
            (InstallmentQuestion, "no_(_on_cash)"),
            (ApartmentQuestion, "2_bedroom_apartment"),
            (BuyingForQuestion, "personal_living"));

        Assert.Equal(h.Leads.ProjectId, lead.InterestedProjectId);
        Assert.Equal("2 Bedroom Apartment", lead.PropertyType);
        Assert.Equal(LeadPurchaseIntent.SelfUse, lead.PurchaseIntent);
        Assert.Equal(LeadPaymentPreference.Cash, lead.PaymentPreference);

        var answers = Assert.Single(await h.Leads.Leads.GetExternalSubmissionsAsync(lead.Id, h.Leads.Admin)).FieldData;
        var buyingFor = answers.Single(a => a.Name == BuyingForQuestion);
        Assert.True(buyingFor.IsMapped);
        Assert.Equal("personal_living", buyingFor.Value);
        Assert.Equal("Are you buying for ?", buyingFor.Label);
        Assert.Equal("Personal Living", buyingFor.ValueLabel);
        Assert.All(answers, a => Assert.True(a.IsMapped));

        // Leads from the form are found under the project, like any other lead for it.
        var list = await h.Leads.Leads.GetLeadsAsync(
            new DTOs.LeadDtos.LeadFilterDto { ProjectId = h.Leads.ProjectId }, h.Leads.Admin);
        Assert.Contains(list.Items, l => l.Id == lead.Id);
        var cash = await h.Leads.Leads.GetLeadsAsync(
            new DTOs.LeadDtos.LeadFilterDto { PaymentPreference = LeadPaymentPreference.Cash }, h.Leads.Admin);
        Assert.Contains(cash.Items, l => l.Id == lead.Id);
    }

    [Fact]
    public async Task AnAnswerSentAsTheOptionsText_MapsTheSameAsItsKey()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var page = await SetUpFloriaAsync(h);

        var lead = await IngestAsync(h, page, "lead-1",
            (InstallmentQuestion, "No ( on cash)"),
            (ApartmentQuestion, "Studio Apartment"),
            (BuyingForQuestion, "Investment"));

        Assert.Equal("Studio Apartment", lead.PropertyType);
        Assert.Equal(LeadPurchaseIntent.Investment, lead.PurchaseIntent);
        Assert.Equal(LeadPaymentPreference.Cash, lead.PaymentPreference);
    }

    [Fact]
    public async Task AMissingAnswer_LeavesItsFieldUnset_AndTheRestStillMap()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var page = await SetUpFloriaAsync(h);

        var lead = await IngestAsync(h, page, "lead-1", (BuyingForQuestion, "investment"));

        Assert.Equal(h.Leads.ProjectId, lead.InterestedProjectId);
        Assert.Equal(LeadPurchaseIntent.Investment, lead.PurchaseIntent);
        Assert.Null(lead.PropertyType);
        Assert.Equal(LeadPaymentPreference.Unknown, lead.PaymentPreference);
    }

    [Fact]
    public async Task AnOptionTheMappingDoesNotCover_IsKeptAndShownUnmapped()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var page = await SetUpFloriaAsync(h, mapping: new SaveLeadFormMappingDto
        {
            Answers = [Mapping(ApartmentQuestion, LeadFormAnswerTarget.PropertyType, ("studio_apartment", "Studio"))]
        });

        var lead = await IngestAsync(h, page, "lead-1",
            (ApartmentQuestion, "3_bedroom_apartment"), (BuyingForQuestion, "villa"));

        Assert.Null(lead.PropertyType);
        Assert.Null(lead.InterestedProjectId);

        var answers = Assert.Single(await h.Leads.Leads.GetExternalSubmissionsAsync(lead.Id, h.Leads.Admin)).FieldData;
        var apartment = answers.Single(a => a.Name == ApartmentQuestion);
        Assert.False(apartment.IsMapped);
        Assert.Equal("3_bedroom_apartment", apartment.Value);
        Assert.Equal("3 Bedroom Apartment", apartment.ValueLabel);

        // Not one of the form's options at all: still shown, word for word.
        var buyingFor = answers.Single(a => a.Name == BuyingForQuestion);
        Assert.False(buyingFor.IsMapped);
        Assert.Equal("villa", buyingFor.Value);
        Assert.Null(buyingFor.ValueLabel);
    }

    [Fact]
    public async Task AFormWithNoMapping_BehavesExactlyAsBefore()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var page = await SetUpFloriaAsync(h, mapped: false);

        var lead = await IngestAsync(h, page, "lead-1",
            (InstallmentQuestion, "yes"), (ApartmentQuestion, "studio_apartment"), (BuyingForQuestion, "investment"));

        Assert.Null(lead.InterestedProjectId);
        Assert.Null(lead.PropertyType);
        Assert.Equal(LeadPurchaseIntent.Unknown, lead.PurchaseIntent);
        Assert.Equal(LeadPaymentPreference.Unknown, lead.PaymentPreference);

        var stored = await h.Db.LeadExternalSubmissions.SingleAsync();
        var answers = JsonSerializer.Deserialize<List<ExternalFieldAnswerDto>>(stored.FieldDataJson!)!;
        Assert.All(answers.Where(a => a.Name != "full_name" && a.Name != "phone_number"), a => Assert.False(a.IsMapped));
        // Labels are looked up when read, never written into what was stored.
        Assert.DoesNotContain("Label", stored.FieldDataJson);
    }

    [Fact]
    public async Task ARepeatEnquiry_FillsOnlyWhatTheLeadIsMissing()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var page = await SetUpFloriaAsync(h);

        var otherProject = new Project { ProjectName = "Other Tower", Location = "Lahore", CreatedById = h.Leads.AdminUserId };
        h.Db.Projects.Add(otherProject);
        await h.Db.SaveChangesAsync();

        var existing = (await h.Leads.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Ali", phone: "0300-1234567", email: null), h.Leads.Admin)).Lead!;
        var tracked = await h.Db.Leads.SingleAsync(l => l.Id == existing.Id);
        tracked.InterestedProjectId = otherProject.Id;
        tracked.PropertyType = "Penthouse";
        tracked.PurchaseIntent = LeadPurchaseIntent.Rental;
        await h.Db.SaveChangesAsync();

        var lead = await IngestAsync(h, page, "lead-1",
            (InstallmentQuestion, "yes"), (ApartmentQuestion, "studio_apartment"), (BuyingForQuestion, "investment"));

        Assert.Equal(existing.Id, lead.Id);
        Assert.Equal(otherProject.Id, lead.InterestedProjectId);
        Assert.Equal("Penthouse", lead.PropertyType);
        Assert.Equal(LeadPurchaseIntent.Rental, lead.PurchaseIntent);
        // The one gap the lead had is filled.
        Assert.Equal(LeadPaymentPreference.Installments, lead.PaymentPreference);
    }

    [Fact]
    public async Task ChangingAMapping_LeavesStoredSubmissionsAsTheyWere()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var page = await SetUpFloriaAsync(h, mapped: false);

        var first = await IngestAsync(h, page, "lead-1", (BuyingForQuestion, "investment"));
        var storedBefore = (await h.Db.LeadExternalSubmissions.SingleAsync()).FieldDataJson;

        await h.Integration.SaveLeadFormMappingAsync(FormId, FloriaMapping(h.Leads.ProjectId), h.Leads.Admin);

        var reloaded = await h.Db.Leads.AsNoTracking().SingleAsync(l => l.Id == first.Id);
        Assert.Equal(LeadPurchaseIntent.Unknown, reloaded.PurchaseIntent);
        Assert.Null(reloaded.InterestedProjectId);
        Assert.Equal(storedBefore, (await h.Db.LeadExternalSubmissions.AsNoTracking().SingleAsync()).FieldDataJson);
    }

    // ── The admin setting ───────────────────────────────────────────────────────

    [Fact]
    public async Task SavingAMapping_StoresTheFormsOwnKeysAndText_AndRoundTrips()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        await SetUpFloriaAsync(h, mapped: false);

        var request = FloriaMapping(h.Leads.ProjectId);
        request.Answers[2].QuestionKey = "Are you buying for ?";
        request.Answers[2].Options[1].OptionKey = "PERSONAL_LIVING";
        request.Answers[2].Options[1].Value = "selfuse";

        var saved = await h.Integration.SaveLeadFormMappingAsync(FormId, request, h.Leads.Admin);

        Assert.Equal("Floria Heights", saved.InterestedProjectName);
        Assert.Equal(5, saved.Questions.Count);
        Assert.NotNull(saved.Version);
        var buyingFor = saved.Answers.Single(a => a.Target == LeadFormAnswerTarget.PurchaseIntent);
        Assert.Equal(BuyingForQuestion, buyingFor.QuestionKey);
        var personal = buyingFor.Options.Single(o => o.OptionKey == "personal_living");
        Assert.Equal("Personal Living", personal.OptionLabel);
        Assert.Equal("SelfUse", personal.Value);

        var resources = await h.Integration.GetResourcesAsync(
            (await h.Db.ExternalIntegrationConnections.SingleAsync()).Id);
        var form = resources.Single(g => g.ResourceType == ExternalResourceTypes.LeadForm).Items.Single();
        Assert.True(form.HasFormMapping);
        Assert.Equal("Floria Heights", form.FormMappingProjectName);
    }

    [Theory]
    [InlineData("no_such_question", "investment", "Investment")]
    [InlineData(BuyingForQuestion, "villa", "Investment")]
    [InlineData(BuyingForQuestion, "investment", "Unknown")]
    [InlineData(BuyingForQuestion, "investment", "2")]
    public async Task AMappingTheFormCannotHonour_IsRefused(string questionKey, string optionKey, string value)
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        await SetUpFloriaAsync(h, mapped: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Integration.SaveLeadFormMappingAsync(FormId,
            new SaveLeadFormMappingDto
            {
                Answers = [Mapping(questionKey, LeadFormAnswerTarget.PurchaseIntent, (optionKey, value))]
            }, h.Leads.Admin));

        Assert.False(await h.Db.ExternalLeadFormMappings.AnyAsync());
    }

    [Fact]
    public async Task TwoQuestionsFillingTheSameField_AreRefused()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        await SetUpFloriaAsync(h, mapped: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Integration.SaveLeadFormMappingAsync(FormId,
            new SaveLeadFormMappingDto
            {
                Answers =
                [
                    Mapping(ApartmentQuestion, LeadFormAnswerTarget.PropertyType, ("studio_apartment", "Studio")),
                    Mapping(BuyingForQuestion, LeadFormAnswerTarget.PropertyType, ("investment", "Studio"))
                ]
            }, h.Leads.Admin));

        Assert.Contains("property type", error.Message);
    }

    [Fact]
    public async Task AnswersCannotBeMappedBeforeTheFormsQuestionsAreSynced_ButTheProjectCan()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        await SetUpFloriaAsync(h, mapped: false, questionsSynced: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Integration.SaveLeadFormMappingAsync(FormId, FloriaMapping(h.Leads.ProjectId), h.Leads.Admin));

        var saved = await h.Integration.SaveLeadFormMappingAsync(FormId,
            new SaveLeadFormMappingDto { InterestedProjectId = h.Leads.ProjectId }, h.Leads.Admin);
        Assert.Equal(h.Leads.ProjectId, saved.InterestedProjectId);
    }

    [Fact]
    public async Task AFormDamsHasNotDiscovered_CannotBeMapped()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        await Assert.ThrowsAsync<LeadNotFoundException>(() =>
            h.Integration.SaveLeadFormMappingAsync("unknown-form",
                new SaveLeadFormMappingDto { InterestedProjectId = h.Leads.ProjectId }, h.Leads.Admin));
    }

    [Fact]
    public async Task SavingOverSomeoneElsesChange_IsRefused()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        await SetUpFloriaAsync(h, mapped: false);

        await h.Integration.SaveLeadFormMappingAsync(FormId,
            new SaveLeadFormMappingDto { InterestedProjectId = h.Leads.ProjectId }, h.Leads.Admin);

        // The in-memory provider does not generate row versions; SQL Server would.
        var row = await h.Db.ExternalLeadFormMappings.SingleAsync();
        row.RowVersion = [1];
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        // A second screen opened before the first mapping existed.
        await Assert.ThrowsAsync<LeadConcurrencyException>(() => h.Integration.SaveLeadFormMappingAsync(FormId,
            new SaveLeadFormMappingDto { InterestedProjectId = null }, h.Leads.Admin));
        h.Db.ChangeTracker.Clear();

        // One opened on a version that has since moved on.
        await Assert.ThrowsAsync<LeadConcurrencyException>(() => h.Integration.SaveLeadFormMappingAsync(FormId,
            new SaveLeadFormMappingDto { InterestedProjectId = null, Version = Convert.ToBase64String([0]) },
            h.Leads.Admin));
        h.Db.ChangeTracker.Clear();

        var current = await h.Integration.GetLeadFormMappingAsync(FormId);
        Assert.Equal(h.Leads.ProjectId, current.InterestedProjectId);

        var saved = await h.Integration.SaveLeadFormMappingAsync(FormId,
            new SaveLeadFormMappingDto { Answers = FloriaMapping(0).Answers, Version = current.Version },
            h.Leads.Admin);
        Assert.Null(saved.InterestedProjectId);
        Assert.Equal(3, saved.Answers.Count);
    }

    [Fact]
    public async Task SavingAnEmptyMapping_RemovesIt()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        await SetUpFloriaAsync(h);

        var current = await h.Integration.GetLeadFormMappingAsync(FormId);
        var cleared = await h.Integration.SaveLeadFormMappingAsync(FormId,
            new SaveLeadFormMappingDto { Version = current.Version }, h.Leads.Admin);

        Assert.Null(cleared.Version);
        Assert.Empty(cleared.Answers);
        Assert.False(await h.Db.ExternalLeadFormMappings.AnyAsync());
    }

    // ── Sync ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncKeepsAFormsQuestions_AndAResyncWithoutThemDoesNotEraseThem()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync();
        h.Graph.Pages = [new MetaDiscoveredResource { ResourceType = ExternalResourceTypes.FacebookPage, ExternalId = page.ExternalId, Name = page.Name }];
        h.Graph.LeadForms =
        [
            new MetaDiscoveredResource
            {
                ResourceType = ExternalResourceTypes.LeadForm, ExternalId = FormId, ParentExternalId = page.ExternalId,
                Name = "Floria form", MetadataJson = LeadFormQuestions.Serialize(FloriaQuestions)
            }
        ];

        await h.Sync.SyncConnectionAsync(connection.Id);
        h.Graph.LeadForms[0].MetadataJson = null;
        await h.Sync.SyncConnectionAsync(connection.Id);

        var form = await h.Db.ExternalIntegrationResources.AsNoTracking()
            .SingleAsync(r => r.ResourceType == ExternalResourceTypes.LeadForm);
        Assert.Equal(5, LeadFormQuestions.Read(form.MetadataJson).Count);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>The Floria Heights form on an enabled page, synced, and mapped unless told otherwise.</summary>
    private static async Task<ExternalIntegrationResource> SetUpFloriaAsync(
        MetaIntegrationHarness h, bool mapped = true, SaveLeadFormMappingDto? mapping = null, bool questionsSynced = true)
    {
        var (connection, page) = await h.ConnectPageAsync();

        h.Db.ExternalIntegrationResources.Add(new ExternalIntegrationResource
        {
            ExternalIntegrationConnectionId = connection.Id,
            Provider = IntegrationProviders.Meta,
            ResourceType = ExternalResourceTypes.LeadForm,
            ExternalId = FormId,
            ParentExternalId = page.ExternalId,
            Name = "Untitled form 31/03/2026, 19:17",
            IsActive = true,
            LastSyncedAt = DateTime.UtcNow,
            MetadataJson = questionsSynced ? LeadFormQuestions.Serialize(FloriaQuestions) : null
        });
        await h.Db.SaveChangesAsync();

        if (mapped)
            await h.Integration.SaveLeadFormMappingAsync(
                FormId, mapping ?? FloriaMapping(h.Leads.ProjectId), h.Leads.Admin);

        return page;
    }

    private static async Task<Lead> IngestAsync(
        MetaIntegrationHarness h, ExternalIntegrationResource page, string leadgenId,
        params (string Name, string? Value)[] answers)
    {
        (string Name, string? Value)[] fields =
        [
            ("full_name", "Ali Khan"),
            ("phone_number", "+92 300 1234567"),
            .. answers
        ];

        h.Graph.Leads[leadgenId] = FakeMetaGraphClient.Lead(leadgenId, fields, pageId: page.ExternalId, formId: FormId);
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, leadgenId, FormId));
        await h.Processor.ProcessPendingEventsAsync(10);

        var eventRow = await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync(e => e.EventKey.Contains(leadgenId));
        Assert.Equal(ExternalIntegrationEventStatus.Processed, eventRow.Status);

        return await h.Db.Leads.AsNoTracking().SingleAsync(l => l.Id == eventRow.LeadId);
    }

    private static LeadFormQuestionDto Question(string key, string label, params (string Key, string Value)[] options) => new()
    {
        Key = key,
        Label = label,
        Type = "CUSTOM",
        Options = options.Select(o => new LeadFormOptionDto { Key = o.Key, Value = o.Value }).ToList()
    };

    private static LeadFormAnswerMappingDto Mapping(
        string questionKey, LeadFormAnswerTarget target, params (string OptionKey, string Value)[] options) => new()
    {
        QuestionKey = questionKey,
        Target = target,
        Options = options.Select(o => new LeadFormOptionMappingDto { OptionKey = o.OptionKey, Value = o.Value }).ToList()
    };
}
