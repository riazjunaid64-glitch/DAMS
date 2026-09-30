import {
  Button, DataTable, EmptyState, IconFile, Notice, StatCard, statusLabel, useIsPhone, type DataTableColumn,
} from "../../components/ui";
import { formatDay } from "../../lib/dates.ts";
import { formatPkr } from "../../utils/currency.ts";
import { SavedProof } from "../proof/SavedProof.tsx";
import type { BookingDetail, BookingPayment } from "./detailTypes.ts";
import { paymentFor } from "./paymentRows.ts";
import { Figure } from "./ui.tsx";

type Props = {
  booking: BookingDetail;
  /** NULL until the list has been read: "not known" is never the same as "no payments". */
  payments: BookingPayment[] | null;
  paymentsError: string | null;
  onOpenReceipt: (payment: BookingPayment) => void;
  /** Reloads the list once a proof has been attached from a row. */
  onChanged: () => void;
  onRetry: () => void;
};

/** The Payments tab: what has been collected, and every receipt with its proof. */
export function PaymentsTab({ booking, payments, paymentsError, onOpenReceipt, onChanged, onRetry }: Props) {
  const isPhone = useIsPhone();
  const count = payments?.length ?? booking.payments.length;
  const cancelled = booking.status === "Cancelled";

  const stats = (
    <div className="grid grid-cols-2 gap-2.5 md:grid-cols-3 md:gap-4">
      <StatCard
        tone="green"
        label="Collected"
        value={<Figure amount={formatPkr(booking.collected)} detail={`${count} ${count === 1 ? "payment" : "payments"}`} />}
      />
      <StatCard label="Rebate credits" value={<Figure amount={formatPkr(booking.rebateCredits)} />} />
      {/* A cancelled sale is no longer owed, so an outstanding figure would mislead; the settlement
          on the Summary tab says what remains between the parties. */}
      <StatCard
        className="max-md:col-span-2"
        label="Outstanding"
        value={<Figure amount={cancelled ? "Cancelled" : formatPkr(booking.outstanding)} />}
      />
    </div>
  );

  const proof = (payment: BookingPayment) => (
    <SavedProof ownerType="CustomerPayment" ownerId={payment.id} proof={payment.proof} onChanged={onChanged} />
  );
  const receipt = (payment: BookingPayment, fullWidth = false) => (
    <Button size="sm" variant="outline" fullWidth={fullWidth} icon={<IconFile size={16} />} onClick={() => onOpenReceipt(payment)}>Receipt</Button>
  );

  const columns: DataTableColumn<BookingPayment>[] = [
    { key: "receipt", header: "Receipt", render: (payment) => <span className="whitespace-nowrap font-mono text-xs font-bold text-primary">{payment.receiptNumber ?? "—"}</span> },
    { key: "date", header: "Date", render: (payment) => <span className="whitespace-nowrap">{formatDay(payment.paidAt)}</span> },
    { key: "for", header: "For", render: paymentFor },
    { key: "method", header: "Method", render: (payment) => statusLabel(payment.paymentMethod) },
    { key: "reference", header: "Reference", render: (payment) => payment.paymentReference || "—", className: "max-w-[200px] truncate text-ink-2" },
    { key: "amount", header: "Amount", align: "right", render: (payment) => <span className="whitespace-nowrap font-bold tabular-nums">{formatPkr(payment.amount)}</span> },
    {
      key: "actions",
      header: <span className="sr-only">Proof and receipt</span>,
      align: "right",
      render: (payment) => (
        <span className="inline-flex items-center justify-end gap-3">{proof(payment)}{receipt(payment)}</span>
      ),
    },
  ];

  const card = (payment: BookingPayment) => (
    <div className="rounded-card border border-line bg-card p-4 font-ui">
      <div className="flex items-baseline justify-between gap-3">
        <span className="font-mono text-xs font-bold text-primary">{payment.receiptNumber ?? "—"}</span>
        <span className="text-section font-extrabold tabular-nums text-ink">{formatPkr(payment.amount)}</span>
      </div>
      <dl className="m-0 mt-2.5 grid grid-cols-2 gap-x-4 gap-y-2">
        {[
          ["Date", formatDay(payment.paidAt)],
          ["For", paymentFor(payment)],
          ["Method", statusLabel(payment.paymentMethod)],
          ...(payment.paymentReference ? [["Reference", payment.paymentReference]] : []),
        ].map(([label, value]) => (
          <div key={label} className="min-w-0">
            <dt className="text-label text-ink-muted">{label}</dt>
            <dd className="m-0 mt-0.5 break-words text-sm font-semibold text-ink">{value}</dd>
          </div>
        ))}
        <div className="col-span-2">
          <dt className="text-label text-ink-muted">Proof</dt>
          <dd className="m-0 mt-0.5">{proof(payment)}</dd>
        </div>
      </dl>
      <div className="mt-3">{receipt(payment, true)}</div>
    </div>
  );

  let history;
  if (payments !== null && payments.length > 0) {
    // One rendering or the other, never both: a hidden copy would still hold a second file input
    // and a second Receipt button for every row.
    history = isPhone
      ? <ul className="m-0 flex list-none flex-col gap-2.5 p-0">{payments.map((payment) => <li key={payment.id}>{card(payment)}</li>)}</ul>
      : <DataTable caption="Payment history" columns={columns} rows={payments} rowKey={(payment) => payment.id} minWidth={860} />;
  } else if (paymentsError) {
    // "No payments" is a statement about the customer; a failed read is a statement about the
    // network. Showing the first when the second happened is how money gets collected twice.
    history = <Notice tone="red" role="alert" title={paymentsError} action={<Button size="sm" variant="outline" onClick={onRetry}>Retry</Button>} />;
  } else if (payments === null) {
    history = <p className="py-10 text-center text-sm text-ink-muted">Loading...</p>;
  } else {
    history = <EmptyState title="No payments yet" message="Receipts appear here as soon as the first payment is recorded." />;
  }

  return (
    <div className="flex flex-col gap-4">
      {stats}
      <section className="flex flex-col gap-3 font-ui">
        <h3 className="m-0 text-label font-extrabold uppercase tracking-[0.4px] text-ink-2">Payment history</h3>
        {history}
      </section>
    </div>
  );
}
