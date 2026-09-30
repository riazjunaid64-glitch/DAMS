import type { ReactNode } from "react";
import { Button, Card, cx } from "../../components/ui";
import { formatDay } from "../../lib/dates.ts";
import { floorName } from "../../lib/floors.ts";
import { formatPkr } from "../../utils/currency.ts";
import type { FinanceAccountOption } from "../bookings/detailTypes.ts";
import { formatPhone } from "../bookings/format.ts";
import { PAYMENT_METHODS } from "../bookings/paymentForm.ts";
import { PAYMENT_FOR, SOURCES, draftFigures, receivedAmount, type BookingDraft, type StepIndex } from "./draft.ts";
import { formatSqFt } from "../../components/project/unitList.ts";

type Props = {
  draft: BookingDraft;
  financeAccounts: FinanceAccountOption[];
  /** Jumps back to the step a block came from. */
  onEdit: (step: StepIndex) => void;
};

type Row = [label: string, value: ReactNode];

function Block({ title, step, rows, onEdit, className }: { title: string; step: StepIndex; rows: Row[]; onEdit: (step: StepIndex) => void; className?: string }) {
  return (
    <Card className={cx("!p-4", className)}>
      <header className="mb-2 flex items-center justify-between">
        <h3 className="m-0 text-body font-extrabold text-ink">{title}</h3>
        <Button variant="link" aria-label={`Edit ${title}`} onClick={() => onEdit(step)}>Edit</Button>
      </header>
      <dl className="m-0 flex flex-col gap-1.5">
        {rows.map(([label, value]) => (
          <div key={label} className="flex items-baseline justify-between gap-4">
            <dt className="shrink-0 text-small font-bold text-ink-2">{label}</dt>
            <dd className="m-0 break-words text-right text-small font-extrabold text-ink">{value}</dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}

const dash = (value: string | null | undefined) => (value && value.trim() ? value : "—");

/** Step 5 · Review: four blocks, each with an Edit link back to its step. Nothing is saved until Create booking. */
export function ReviewStep({ draft, financeAccounts, onEdit }: Props) {
  const figures = draftFigures(draft);
  const received = receivedAmount(draft);
  const unit = draft.unit!;
  const customer = draft.customerMode === "existing" ? draft.pickedCustomer : null;
  const account = financeAccounts.find((a) => String(a.id) === draft.accountId);

  const unitRows: Row[] = [
    ["Project", draft.projectName],
    ["Unit", `Unit ${unit.unitNumber} · ${unit.unitType}`],
    ["Floor · Size", `${floorName(unit.floorName, unit.floorNumber)} · ${formatSqFt(unit.size)}`],
    ["Tower", dash(draft.tower)],
    ["Serial no.", draft.serialNo.trim() || "Auto"],
    ["Corner", draft.isCorner ? "Yes" : "No"],
  ];
  const customerRows: Row[] = customer
    ? [["Name", customer.fullName], ["Mobile", dash(formatPhone(customer.phone))], ["CNIC", dash(customer.cnic)]]
    : [
      ["Name", draft.fullName.trim()],
      ["S/O", dash(draft.guardianName)],
      ["Mobile", dash(formatPhone(draft.mobile))],
      ["CNIC", dash(draft.cnic)],
      ["Date of birth", draft.dob ? formatDay(`${draft.dob}T12:00:00`) : "—"],
      ["Address", dash(draft.address)],
    ];
  const kinRows: Row[] = [
    ["Name", [draft.kinName.trim(), draft.kinRelation].filter(Boolean).join(" · ") || "—"],
    ["Mobile", dash(formatPhone(draft.kinMobile))],
  ];
  const priceRows: Row[] = [
    ["Agreed price", formatPkr(figures.agreed)],
    ["Discount", figures.discountPercent > 0 ? `${figures.discountPercent}% · ${formatPkr(figures.discount)}` : "None"],
    ["Booking amount", `${formatPkr(figures.bookingAmount)}${figures.agreed > 0 ? ` · ${Math.round((figures.bookingAmount / figures.agreed) * 1000) / 10}%` : ""}`],
    ["Source", SOURCES.find((s) => s.value === draft.source)?.label ?? draft.source],
    ["Received today", received > 0 ? formatPkr(received) : "Nothing"],
    ...(received > 0 ? ([
      ["Account", account ? `${account.name} — ${account.accountHolderName}` : "—"],
      ["Method", PAYMENT_METHODS.find((m) => m.value === draft.method)?.label ?? draft.method],
      ["Payment for", PAYMENT_FOR.find((o) => o.value === draft.paymentFor)?.label ?? draft.paymentFor],
      ["Reference no.", dash(draft.reference)],
      ["Date", formatDay(`${draft.paidOn}T12:00:00`)],
    ] as Row[]) : []),
  ];

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <Block title="Unit" step={0} rows={unitRows} onEdit={onEdit} />
      <Block title="Customer" step={1} rows={customerRows} onEdit={onEdit} />
      <Block title="Next of kin" step={2} rows={kinRows} onEdit={onEdit} />
      <Block title="Price & payment" step={3} rows={priceRows} onEdit={onEdit} />
    </div>
  );
}
