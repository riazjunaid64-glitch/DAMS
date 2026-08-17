using System.ComponentModel.DataAnnotations;
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
        public List<string> GrantedScopes { get; set; } = [];
        public int PageCount { get; set; }
        public int InstagramCount { get; set; }
        public int AdAccountCount { get; set; }
        public int LeadFormCount { get; set; }
        public int EnabledResourceCount { get; set; }
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
    }

    /// <summary>One answer from a provider form, as shown on the lead's detail screen.</summary>
    public class ExternalFieldAnswerDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Value { get; set; }
        /// <summary>False when DAMS kept the answer but has no field to put it in.</summary>
        public bool IsMapped { get; set; }
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
        /// Every answer the person gave, mapped or not. The raw provider payload is
        /// deliberately not exposed — it is for troubleshooting in the database, not for the UI.
        /// </summary>
        public List<ExternalFieldAnswerDto> FieldData { get; set; } = [];
    }
}
