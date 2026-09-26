using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkResolvedHoldEventsToLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // KAN-29: a Meta enquiry that was held and later resolved left its webhook event with
            // no lead. Link those events to the lead the enquiry was resolved into, as resolving
            // now does. Every event key ends with ":" + the Meta lead id
            // (MetaWebhookIntakeService.EventKeySuffix). Only unlinked, processed events are
            // touched, so re-running this changes nothing.
            // ExternalIntegrationEventStatus.Processed = 2, LeadIntakeHoldStatus.Resolved = 1.
            migrationBuilder.Sql(@"
UPDATE e
SET e.LeadId = h.ResolvedLeadId
FROM ExternalIntegrationEvents e
JOIN LeadIntakeHolds h
  ON h.Provider = e.Provider
 AND RIGHT(e.EventKey, LEN(h.ExternalLeadId) + 1) = N':' + h.ExternalLeadId
WHERE e.Provider = N'meta'
  AND e.LeadId IS NULL
  AND e.Status = 2
  AND h.Status = 1
  AND h.ResolvedLeadId IS NOT NULL
  AND h.ExternalLeadId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data only. Unlinking would also undo links written since by resolving enquiries,
            // which cannot be told apart from these, so the links are left in place.
        }
    }
}
