import type { ReactNode } from "react";
import { Card, IconAlert, Notice } from "../../components/ui";
import { formatDay } from "../../lib/dates.ts";
import { formatPhone } from "../bookings/format.ts";
import { blockedLine, isBlocked } from "./customerHeader.ts";
import type { CustomerDetail } from "./customerForm.ts";

const notAdded = <span className="font-semibold text-ink-faint">Not added</span>;
const orNotAdded = (value?: string | null): ReactNode => (value?.trim() ? value : notAdded);

/** Label on the left, value on the right, like the booking Summary. */
function Rows({ rows }: { rows: { label: string; value: ReactNode }[] }) {
  return (
    <dl className="m-0 flex flex-col">
      {rows.map((row) => (
        <div key={row.label} className="flex items-baseline justify-between gap-4 py-1.5">
          <dt className="shrink-0 whitespace-nowrap text-body font-bold text-ink-2">{row.label}</dt>
          <dd className="m-0 min-w-0 break-words text-right text-body font-bold text-ink">{row.value}</dd>
        </div>
      ))}
    </dl>
  );
}

/** The Overview tab: the red block Notice when blocked, then Personal details, Contact and Notes. */
export function CustomerOverview({ customer }: { customer: CustomerDetail }) {
  return (
    <div className="flex flex-col gap-4">
      {isBlocked(customer) && (
        <Notice tone="red" role="status" icon={<IconAlert size={18} />} title="New bookings are stopped" message={blockedLine(customer)} />
      )}
      <div className="grid items-start gap-4 md:grid-cols-2">
        <Card title="Personal details">
          <Rows rows={[
            { label: "S/O, W/O, D/O", value: orNotAdded(customer.fatherName) },
            { label: "Date of birth", value: customer.dateOfBirth ? formatDay(customer.dateOfBirth) : notAdded },
            { label: "Nationality", value: orNotAdded(customer.nationality) },
            { label: "Occupation", value: orNotAdded(customer.occupation) },
          ]} />
        </Card>
        <Card title="Contact">
          <Rows rows={[
            // A blank WhatsApp means the same number as the mobile.
            { label: "WhatsApp", value: customer.whatsapp?.trim() ? formatPhone(customer.whatsapp) : <span className="font-semibold text-ink-muted">Same as mobile</span> },
            { label: "Email", value: orNotAdded(customer.email) },
            { label: "Mailing address", value: orNotAdded(customer.address) },
          ]} />
        </Card>
      </div>
      {customer.notes?.trim() && (
        <Card title="Notes"><p className="m-0 whitespace-pre-line text-body text-ink">{customer.notes}</p></Card>
      )}
    </div>
  );
}
