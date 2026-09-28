import { Card, KeyValueGrid, StatusBadge, type KeyValueItem } from "../../components/ui";
import { paymentPreferenceLabel, purchaseIntentLabel } from "./labels.ts";
import { latestSubmission, platformLabel, unmappedAnswers } from "./leadPage.ts";
import type { ExternalSubmission, Lead } from "./types.ts";

/** The lead's details: contact, requirement, where it came from and notes. */
export function LeadOverview({ lead, submissions }: { lead: Lead; submissions: ExternalSubmission[] }) {
  const submission = latestSubmission(submissions);
  const payment = paymentPreferenceLabel(lead.paymentPreference);
  const requirement: KeyValueItem[] = [
    { label: "Apartment type", value: lead.propertyType },
    { label: "5-year installment plan?", value: payment && <StatusBadge status={payment.label} tone={payment.tone}>{payment.label}</StatusBadge> },
    { label: "Buying for", value: purchaseIntentLabel(lead.purchaseIntent) },
    // Answers with no DAMS field yet, so nothing the customer said is hidden.
    ...unmappedAnswers(submission).map((answer) => ({ label: answer.label, value: answer.value })),
  ];
  const source: KeyValueItem[] = submission
    ? [
        { label: "Source", value: lead.sourceName },
        { label: "Platform", value: platformLabel(submission) },
        { label: "Campaign", value: submission.campaignName },
        { label: "Ad", value: submission.adName },
        { label: "Form", value: submission.externalFormName },
      ]
    : [{ label: "Source", value: lead.sourceName }];

  return (
    <div className="grid gap-2.5 md:grid-cols-2 md:gap-4">
      <Card title="Contact">
        <KeyValueGrid
          items={[
            { label: "Phone", value: lead.phone && <a href={`tel:${lead.phone}`} className="text-ink no-underline hover:underline">{lead.phone}</a> },
            { label: "WhatsApp", value: lead.whatsappNumber },
            { label: "City", value: lead.city },
            { label: "Email", value: lead.email && <a href={`mailto:${lead.email}`} className="text-ink no-underline hover:underline">{lead.email}</a> },
          ]}
        />
      </Card>
      <Card title={submission ? "Requirement (from ad form)" : "Requirement"}>
        <KeyValueGrid items={requirement} />
      </Card>
      <Card title="Source">
        <KeyValueGrid items={source} />
      </Card>
      <Card title="Notes">
        <p className="m-0 whitespace-pre-wrap break-words text-sm font-semibold text-ink">{lead.notes || "—"}</p>
      </Card>
    </div>
  );
}
