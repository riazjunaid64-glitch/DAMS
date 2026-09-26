using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.IntegrationDtos
{
    // Nothing in this file has a field capable of carrying a token, a secret or a credential,
    // and a test asserts that by reflection. Adding one would leak it to every CRM admin's
    // browser, so if a new field seems to need one, the answer is that it does not.

    public class MetaConnectStartDto
    {
        /// <summary>Where the browser should be sent to begin the Meta consent flow.</summary>
        public string AuthorizationUrl { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }
    }

    public class StartMetaConnectDto
    {
        /// <summary>Relative CRM path to return to. Absolute URLs are rejected — that would be an open redirect.</summary>
        [StringLength(300)]
        public string? ReturnPath { get; set; }
    }

    public class MetaConnectionDto
    {
        public int Id { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public ExternalIntegrationConnectionStatus Status { get; set; }
        public DateTime ConnectedAt { get; set; }
        public string? ConnectedByName { get; set; }
        public DateTime? LastSyncedAt { get; set; }
        public DateTime? LastErrorAt { get; set; }
        public string? LastError { get; set; }
        public DateTime? TokenExpiresAt { get; set; }

        /// <summary>
        /// Set while Meta refuses the account's own sign-in during sync but lead delivery carries
        /// on with the Page tokens: a warning to reconnect, not an outage.
        /// </summary>
        public DateTime? SyncRejectedAt { get; set; }

        /// <summary>When Meta last delivered a lead webhook for this connection, whatever became of it.</summary>
        public DateTime? LastLeadReceivedAt { get; set; }

        public List<string> GrantedScopes { get; set; } = [];
        public int PageCount { get; set; }
        public int InstagramCount { get; set; }
        public int AdAccountCount { get; set; }
        public int LeadFormCount { get; set; }
        public int EnabledResourceCount { get; set; }
        public int PendingEventCount { get; set; }
        public int FailedEventCount { get; set; }
    }

    public class MetaResourceDto
    {
        public int Id { get; set; }
        public string ResourceType { get; set; } = string.Empty;
        public string ExternalId { get; set; } = string.Empty;
        public string? ParentExternalId { get; set; }
        public string? Name { get; set; }
        public string? ExternalStatus { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsActive { get; set; }
        public bool IsSubscribed { get; set; }
        public DateTime? LastSeenAt { get; set; }

        /// <summary>Lead forms only: whether an administrator has linked it to a project or its answers to lead fields.</summary>
        public bool HasFormMapping { get; set; }

        /// <summary>Lead forms only: the project its leads are linked to.</summary>
        public string? FormMappingProjectName { get; set; }
    }

    public class MetaResourceGroupDto
    {
        public string ResourceType { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public List<MetaResourceDto> Items { get; set; } = [];
    }

    public class SetMetaResourceEnabledDto
    {
        public bool IsEnabled { get; set; }
    }

    public class MetaSyncResultDto
    {
        public int Discovered { get; set; }
        public int Updated { get; set; }
        public int Deactivated { get; set; }
        public DateTime SyncedAt { get; set; }
        /// <summary>Set when part of the sync was skipped — for example one ad account refused access.</summary>
        public string? Warning { get; set; }
    }

    /// <summary>
    /// An admin's request to recover a Page's or one form's leads from Meta. Exactly one of the
    /// two is named; a Page means every lead form synced for it.
    /// </summary>
    public class ImportMetaLeadsDto
    {
        /// <summary>The Facebook Page resource, when importing every form it has.</summary>
        public int? ResourceId { get; set; }

        /// <summary>One lead form's Meta id, when importing just that form.</summary>
        [StringLength(200)]
        public string? FormExternalId { get; set; }

        /// <summary>A Pakistan business date; leads submitted from its start onwards. At most 90 days back.</summary>
        public DateOnly Since { get; set; }
    }

    /// <summary>What an import or a reconciliation found, by what became of each lead.</summary>
    public class MetaLeadImportResultDto
    {
        /// <summary>Leads Meta returned for the window.</summary>
        public int Found { get; set; }

        /// <summary>Queued for the normal processor — including ones the webhook recorded while the Page was off.</summary>
        public int New { get; set; }

        public int AlreadyInDams { get; set; }

        /// <summary>Leads that could not be queued, plus forms Meta would not let DAMS read.</summary>
        public int Failed { get; set; }

        public string? Warning { get; set; }
    }

    public class MetaEventDto
    {
        public int Id { get; set; }
        public string EventType { get; set; } = string.Empty;
        public ExternalIntegrationEventStatus Status { get; set; }
        public int Attempts { get; set; }
        public DateTime ReceivedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public int? LeadId { get; set; }
        public string? ResourceName { get; set; }
        public string? LastError { get; set; }
        public int RetryCount { get; set; }
        public DateTime? LastRetriedAt { get; set; }
        public string? LastRetriedByName { get; set; }
    }

    /// <summary>One answer from a provider form, as shown on the lead's detail screen.</summary>
    public class ExternalFieldAnswerDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Value { get; set; }
        /// <summary>False when DAMS kept the answer but has no field to put it in.</summary>
        public bool IsMapped { get; set; }

        /// <summary>
        /// The question as the form words it, and the chosen option's text, looked up from the
        /// synced form when the answer is read. Never stored: the stored answer stays exactly
        /// as the provider sent it.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Label { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ValueLabel { get; set; }
    }

    /// <summary>One question on a provider lead form, as read by the form sync.</summary>
    public class LeadFormQuestionDto
    {
        public string Key { get; set; } = string.Empty;
        public string? Label { get; set; }
        public string? Type { get; set; }
        public List<LeadFormOptionDto> Options { get; set; } = [];
    }

    public class LeadFormOptionDto
    {
        public string Key { get; set; } = string.Empty;
        /// <summary>The option's text as the person saw it.</summary>
        public string? Value { get; set; }
    }

    /// <summary>Which lead field one question fills, and the value each of its options stands for.</summary>
    public class LeadFormAnswerMappingDto
    {
        [Required]
        [StringLength(200)]
        public string QuestionKey { get; set; } = string.Empty;

        public LeadFormAnswerTarget Target { get; set; }

        public List<LeadFormOptionMappingDto> Options { get; set; } = [];
    }

    public class LeadFormOptionMappingDto
    {
        [Required]
        [StringLength(200)]
        public string OptionKey { get; set; } = string.Empty;

        /// <summary>The option's text when the mapping was saved, so an answer sent as text still matches.</summary>
        [StringLength(300)]
        public string? OptionLabel { get; set; }

        /// <summary>A PurchaseIntent or PaymentPreference name, or the property type text.</summary>
        [Required]
        [StringLength(100)]
        public string Value { get; set; } = string.Empty;
    }

    public class LeadFormMappingDto
    {
        public string FormExternalId { get; set; } = string.Empty;
        public string? FormName { get; set; }

        /// <summary>Empty until a sync has read the form's questions.</summary>
        public List<LeadFormQuestionDto> Questions { get; set; } = [];

        public int? InterestedProjectId { get; set; }
        public string? InterestedProjectName { get; set; }
        public List<LeadFormAnswerMappingDto> Answers { get; set; } = [];

        /// <summary>Base64 RowVersion of the saved mapping; null while none has been saved.</summary>
        public string? Version { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class SaveLeadFormMappingDto
    {
        public int? InterestedProjectId { get; set; }

        public List<LeadFormAnswerMappingDto> Answers { get; set; } = [];

        /// <summary>The version the mapping was loaded with; null when there was none yet.</summary>
        public string? Version { get; set; }
    }

    public class LeadExternalSubmissionDto
    {
        public int Id { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string? Platform { get; set; }
        public string? ConnectionDisplayName { get; set; }
        public string ExternalLeadId { get; set; } = string.Empty;
        public string? ExternalFormReference { get; set; }
        public string? ExternalFormName { get; set; }
        public string? PageName { get; set; }
        public string? AdAccountExternalId { get; set; }
        public string? CampaignName { get; set; }
        public string? AdSetName { get; set; }
        public string? AdName { get; set; }
        public DateTime? ExternalSubmittedAt { get; set; }
        public DateTime ReceivedAt { get; set; }

        /// <summary>
        /// Every answer the person gave, mapped or not. The raw provider payload is deliberately
        /// not part of this list: it is served on its own, to fewer roles, by
        /// <see cref="LeadExternalSubmissionRawDto"/>.
        /// </summary>
        public List<ExternalFieldAnswerDto> FieldData { get; set; } = [];
    }

    /// <summary>
    /// What the provider actually sent for one submission, for when the business-friendly fields
    /// on the lead are not enough. Read-only: nothing here is ever written back.
    /// </summary>
    public class LeadExternalSubmissionRawDto
    {
        public int SubmissionId { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string ExternalLeadId { get; set; } = string.Empty;

        /// <summary>The provider's complete response for the lead. Null on receipts that never stored one.</summary>
        public string? RawPayloadJson { get; set; }

        /// <summary>The webhook event that delivered it, when one exists and belongs to this lead.</summary>
        public LeadIntegrationEventRawDto? Event { get; set; }
    }

    public class LeadIntegrationEventRawDto
    {
        public int Id { get; set; }
        public string EventType { get; set; } = string.Empty;
        public ExternalIntegrationEventStatus Status { get; set; }
        public DateTime ReceivedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string RawPayloadJson { get; set; } = string.Empty;
    }
}
