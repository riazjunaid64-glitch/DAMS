using DAMS.Domain.Entities;

namespace DAMS.Application.Common
{
    /// <summary>
    /// Where an external submission came from — page, campaign, ad set, ad, form and the
    /// provider's own answers — as recorded on its <see cref="LeadExternalSubmission"/> receipt.
    ///
    /// Captured once, when the provider's response is in hand. An enquiry held for review keeps
    /// this with it, so the receipt made when an administrator resolves it days later carries
    /// the same attribution an immediately processed one would.
    /// </summary>
    public sealed class SubmissionAttribution
    {
        public int? ConnectionId { get; set; }
        public string? Platform { get; set; }
        public string? PageExternalId { get; set; }
        public string? PageName { get; set; }
        public string? AdAccountExternalId { get; set; }
        public string? CampaignExternalId { get; set; }
        public string? CampaignName { get; set; }
        public string? AdSetExternalId { get; set; }
        public string? AdSetName { get; set; }
        public string? AdExternalId { get; set; }
        public string? AdName { get; set; }
        public string? ExternalFormName { get; set; }
        public string? RawPayloadJson { get; set; }
        public string? FieldDataJson { get; set; }

        public void ApplyTo(LeadExternalSubmission submission)
        {
            submission.ExternalIntegrationConnectionId = ConnectionId;
            submission.Platform = Platform;
            submission.PageExternalId = PageExternalId;
            submission.PageName = PageName;
            submission.AdAccountExternalId = AdAccountExternalId;
            submission.CampaignExternalId = CampaignExternalId;
            submission.CampaignName = CampaignName;
            submission.AdSetExternalId = AdSetExternalId;
            submission.AdSetName = AdSetName;
            submission.AdExternalId = AdExternalId;
            submission.AdName = AdName;
            submission.ExternalFormName = ExternalFormName;
            submission.RawPayloadJson = RawPayloadJson;
            submission.FieldDataJson = FieldDataJson;
        }
    }
}
