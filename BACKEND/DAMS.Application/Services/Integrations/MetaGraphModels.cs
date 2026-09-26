namespace DAMS.Application.Services.Integrations
{
    /// <summary>One answer a person gave on a lead form, exactly as Meta returned it.</summary>
    public sealed record MetaFieldAnswer(string Name, string? Value);

    /// <summary>The labels of the ad a lead came from. Meta shares them only with ads_management.</summary>
    public sealed record MetaLeadAdNames(string? AdName, string? AdSetName, string? CampaignName);

    /// <summary>
    /// A retrieved lead. <see cref="RawJson"/> is the provider's complete response and is what
    /// gets stored, so a question DAMS does not understand today is still recoverable later.
    /// </summary>
    public sealed class MetaLead
    {
        public string LeadgenId { get; set; } = string.Empty;
        public string? FormId { get; set; }
        public string? FormName { get; set; }
        public string? PageId { get; set; }
        public string? AdId { get; set; }
        public string? AdName { get; set; }
        public string? AdSetId { get; set; }
        public string? AdSetName { get; set; }
        public string? CampaignId { get; set; }
        public string? CampaignName { get; set; }
        /// <summary>Meta's own platform hint ("fb", "ig"). Absent more often than not.</summary>
        public string? Platform { get; set; }
        public bool? IsOrganic { get; set; }
        public DateTime? CreatedTime { get; set; }
        public List<MetaFieldAnswer> FieldData { get; set; } = [];
        public string RawJson { get; set; } = string.Empty;
    }

    /// <summary>
    /// The result of walking one paginated Graph edge. <see cref="Truncated"/> is true when the
    /// walk was stopped by <c>MaxGraphPages</c> rather than running out of pages naturally — in
    /// that case <see cref="Items"/> is known to be incomplete, and a caller that treats
    /// "not returned" as "no longer exists" must not do so for this batch.
    /// </summary>
    public sealed class MetaDiscoveryPage
    {
        public List<MetaDiscoveredResource> Items { get; init; } = [];
        public bool Truncated { get; init; }

        /// <summary>
        /// False only for a Page listing that fell back to a request without Instagram field
        /// expansion (see MetaGraphClient.GetPagesAsync). A caller must not treat this run as
        /// having said anything about Instagram accounts at all — not "there are none", and
        /// not grounds to deactivate ones already on file.
        /// </summary>
        public bool IncludesInstagramAccounts { get; init; } = true;
    }

    /// <summary>An asset discovered during sync, in provider-neutral shape.</summary>
    public sealed class MetaDiscoveredResource
    {
        public string ResourceType { get; set; } = string.Empty;
        public string ExternalId { get; set; } = string.Empty;
        public string? ParentExternalId { get; set; }
        public string? Name { get; set; }
        public string? ExternalStatus { get; set; }
        public string? MetadataJson { get; set; }
        /// <summary>Page access token, present only for pages. Encrypted by the caller before storage.</summary>
        public string? ResourceToken { get; set; }
    }

    /// <summary>What a completed token exchange yielded.</summary>
    public sealed class MetaAuthorizationResult
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<string> GrantedScopes { get; set; } = [];
    }
}
