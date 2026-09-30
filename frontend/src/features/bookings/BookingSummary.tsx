import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Button, Card, IconAlert, IconFile, InfoCard, Notice, cx, useIsPhone } from "../../components/ui";
import { formatDay, formatMonthDay } from "../../lib/dates.ts";
import { formatPkr } from "../../utils/currency.ts";
import type { BookingDetail } from "./detailTypes.ts";
import { formatPhone } from "./format.ts";
import { termsNotSet } from "./statusNames.ts";

type Row = {
  label: ReactNode;
  value: ReactNode;
  /** A hairline above the row: the line that separates a total from what it adds up. */
  divider?: boolean;
  bold?: boolean;
};

/** Label left, value right, like a statement. Totals carry a line above and are bold. */
function Rows({ rows }: { rows: Row[] }) {
  return (
    <dl className="m-0 flex flex-col">
      {rows.map((row, index) => (
        <div
          key={index}
          className={cx("flex items-baseline justify-between gap-4 py-1.5", row.divider && "mt-2 border-t border-line-soft pt-3.5")}
        >
          <dt className={cx("shrink-0 whitespace-nowrap text-body text-ink-2", row.bold ? "font-extrabold" : "font-bold")}>{row.label}</dt>
          <dd className={cx("m-0 min-w-0 break-words text-right text-body tabular-nums text-ink", row.bold ? "font-extrabold" : "font-bold")}>{row.value}</dd>
        </div>
      ))}
    </dl>
  );
}

const green = (text: ReactNode) => <span className="text-success">{text}</span>;

function CustomerAndUnit({ booking, leadLink, wide = false }: { booking: BookingDetail; leadLink: boolean; wide?: boolean }) {
  const lead = booking.convertedFromLead;
  const person: Row[] = [
    { label: "Customer", value: booking.customerName },
    { label: "CNIC", value: booking.customerCnic || "—" },
    { label: "Phone", value: formatPhone(booking.customerPhone) || "—" },
    ...(lead ? [{
      label: "From lead",
      // A link only for someone who can open Lead CRM; everyone else just reads the reference.
      value: leadLink ? <Link to={`/crm/leads/${lead.leadId}`} className="text-gold-text underline-offset-4 hover:underline">{lead.leadReference}</Link> : lead.leadReference,
    }] : []),
  ];
  const unit: Row[] = [
    { label: "Unit", value: `Unit ${booking.unitNumber}${booking.unitType ? ` · ${booking.unitType}` : ""}` },
    { label: "Floor", value: booking.floorName || "—" },
    { label: "Size", value: booking.unitSize > 0 ? `${booking.unitSize.toLocaleString("en-PK")} sq ft` : "—" },
    { label: "Project", value: booking.projectName },
  ];
  return (
    <Card title="Customer and unit">
      {wide ? (
        <div className="grid gap-x-10 gap-y-0 md:grid-cols-2"><Rows rows={person} /><Rows rows={unit} /></div>
      ) : (
        <Rows rows={[...person, ...unit]} />
      )}
      <Link to={`/customers/${booking.customerId}`} className="mt-3 inline-flex min-h-11 items-center text-sm font-bold text-gold-text underline-offset-4 hover:underline md:min-h-0">
        Open customer
      </Link>
    </Card>
  );
}

const discountRow = (booking: BookingDetail): Row => ({
  label: booking.discountAmount > 0 ? `Discount (${booking.discountPercent}%)` : "Discount",
  value: booking.discountAmount > 0 ? <span className="text-danger">− {formatPkr(booking.discountAmount)}</span> : "None",
});

const netSalePrice = (booking: BookingDetail) => booking.agreedSalePrice - booking.discountAmount;

type SummaryProps = {
  booking: BookingDetail;
  /** True for someone who can open Lead CRM; the "From lead" reference is a link only then. */
  leadLink: boolean;
  onSetTerms: () => void;
  onEditTerms: () => void;
  /** Cancelled bookings: the cancellation settlement, which KAN-75 owns, shown above the figures. */
  settlement: ReactNode;
};

/** The Summary tab: which cards it holds depends on the booking's status. */
export function BookingSummary({ booking, leadLink, onSetTerms, onEditTerms, settlement }: SummaryProps) {
  if (termsNotSet(booking)) return <TermsNotSetSummary booking={booking} leadLink={leadLink} onSetTerms={onSetTerms} />;
  if (booking.status === "AwaitingBookingAmount") return <AwaitingSummary booking={booking} leadLink={leadLink} onEditTerms={onEditTerms} />;
  if (booking.status === "Cancelled") {
    return (
      <div className="flex flex-col gap-4">
        {settlement}
        <div className="grid items-start gap-4 lg:grid-cols-2">
          <PriceAndPayments booking={booking} />
          <CustomerAndUnit booking={booking} leadLink={leadLink} />
        </div>
      </div>
    );
  }
  return <PlanSummary booking={booking} leadLink={leadLink} />;
}

function TermsNotSetSummary({ booking, leadLink, onSetTerms }: { booking: BookingDetail; leadLink: boolean; onSetTerms: () => void }) {
  const notSet = <span className="text-ink-faint">Not set</span>;
  return (
    <div className="flex flex-col gap-4">
      <Card>
        <div className="flex flex-col gap-4 md:flex-row md:items-center">
          <div className="flex items-center gap-3.5 md:flex-1">
            <span className="flex size-12 shrink-0 items-center justify-center rounded-field bg-warning-soft text-warning"><IconFile size={22} /></span>
            <p className="m-0 text-section font-extrabold text-ink">Set the terms to start</p>
          </div>
          <dl className="m-0 grid grid-cols-[auto_1fr_1fr] gap-x-6 gap-y-3 md:flex md:items-center md:gap-x-8">
            {[
              ["List price", <span key="l" className="text-ink">{formatPkr(booking.listPrice)}</span>],
              ["Agreed price", notSet],
              ["Booking amount", notSet],
            ].map(([label, value]) => (
              <div key={label as string}>
                <dt className="text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">{label}</dt>
                <dd className="m-0 mt-0.5 whitespace-nowrap text-body font-extrabold md:text-[17px]">{value}</dd>
              </div>
            ))}
          </dl>
          <Button onClick={onSetTerms} className="max-md:w-full">Set terms</Button>
        </div>
      </Card>
      <CustomerAndUnit booking={booking} leadLink={leadLink} wide />
    </div>
  );
}

function AwaitingSummary({ booking, leadLink, onEditTerms }: { booking: BookingDetail; leadLink: boolean; onEditTerms: () => void }) {
  const percent = booking.bookingAmountRequired > 0
    ? Math.min(100, Math.round((booking.bookingAmountReceived / booking.bookingAmountRequired) * 100))
    : 0;
  return (
    <div className="grid items-start gap-4 lg:grid-cols-2">
      <Card title="Booking amount">
        <p className="m-0 flex flex-wrap items-baseline gap-x-2">
          <span className="text-[26px] font-extrabold leading-tight tabular-nums text-primary">{formatPkr(booking.bookingAmountReceived)}</span>
          <span className="text-body font-bold text-ink-2">received of {formatPkr(booking.bookingAmountRequired)}</span>
        </p>
        <div
          role="progressbar"
          aria-label="Booking amount received"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={percent}
          className="mt-3 h-2 overflow-hidden rounded-full bg-track"
        >
          <div className="h-full rounded-full bg-gold" style={{ width: `${percent}%` }} />
        </div>
        <div className="mt-3">
          <Rows rows={[
            { label: "Still due", value: formatPkr(booking.bookingAmountRemaining) },
            { label: "Due by", value: booking.bookingAmountDueDate ? formatDay(booking.bookingAmountDueDate) : <span className="text-ink-faint">Not set</span> },
          ]} />
        </div>
        <Button variant="outline" onClick={onEditTerms} className="mt-4 max-md:w-full">Edit terms</Button>
      </Card>
      <div className="flex flex-col gap-4">
        <Card title="Price">
          <Rows rows={[
            { label: "Agreed sale price", value: formatPkr(booking.agreedSalePrice) },
            discountRow(booking),
            { label: "Net sale price", value: formatPkr(netSalePrice(booking)), divider: true },
          ]} />
        </Card>
        <CustomerAndUnit booking={booking} leadLink={leadLink} />
      </div>
    </div>
  );
}

function PriceAndPayments({ booking }: { booking: BookingDetail }) {
  const received = booking.bookingAmountRequired > 0 && booking.bookingAmountReceived >= booking.bookingAmountRequired;
  return (
    <Card title="Price and payments">
      <Rows rows={[
        { label: "Agreed sale price", value: formatPkr(booking.agreedSalePrice) },
        discountRow(booking),
        { label: "Net sale price", value: formatPkr(netSalePrice(booking)), divider: true },
        {
          label: "Booking amount",
          value: <>{formatPkr(booking.bookingAmountRequired)}{received && <> · {green("received")}</>}</>,
        },
        { label: "Installment plan", value: booking.installmentsTotal > 0 ? `${booking.installmentsPaid} of ${booking.installmentsTotal} paid` : "No plan yet" },
        { label: "Collected", value: green(formatPkr(booking.collected)) },
        { label: "Rebate credits", value: formatPkr(booking.rebateCredits) },
        { label: "Outstanding", value: formatPkr(booking.outstanding), divider: true, bold: true },
      ]} />
    </Card>
  );
}

function PlanSummary({ booking, leadLink }: { booking: BookingDetail; leadLink: boolean }) {
  const isPhone = useIsPhone();
  const net = netSalePrice(booking);
  const collectedShare = net > 0 ? Math.round((booking.collected / net) * 100) : 0;
  const next = booking.nextInstallment;
  const unpaid = booking.installmentsTotal - booking.installmentsPaid;
  const day = (value: string) => (isPhone ? formatMonthDay(value) : formatDay(value));

  return (
    <div className="flex flex-col gap-4">
      {booking.status === "PossessionGiven" && booking.outstanding > 0 && (
        <Notice
          tone="orange"
          role="status"
          icon={<IconAlert size={18} />}
          title={`${formatPkr(booking.outstanding)} still due${unpaid > 0 ? ` · ${unpaid} installment${unpaid === 1 ? "" : "s"}` : ""}`}
        />
      )}

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4 lg:gap-4">
        <InfoCard highlight label="Net sale price" value={formatPkr(net)} />
        <InfoCard
          label="Collected"
          value={formatPkr(booking.collected)}
          detail={<span className="font-bold text-success">{isPhone ? `${collectedShare}%` : `${collectedShare}% of the price`}</span>}
        />
        <InfoCard label="Outstanding" value={formatPkr(booking.outstanding)} />
        <InfoCard
          label={isPhone ? "Next due" : "Next installment"}
          value={next ? formatPkr(next.amount) : booking.hasInstallmentSchedule ? "All paid" : "No plan yet"}
          detail={next && (
            <span className={cx("font-bold", next.isOverdue ? "text-danger" : "text-ink-muted")}>
              {next.isOverdue ? `Overdue · ${day(next.dueDate)}` : `Due ${day(next.dueDate)}`}
            </span>
          )}
        />
      </div>

      <div className="grid items-start gap-4 lg:grid-cols-2">
        <PriceAndPayments booking={booking} />
        <CustomerAndUnit booking={booking} leadLink={leadLink} />
      </div>
    </div>
  );
}
