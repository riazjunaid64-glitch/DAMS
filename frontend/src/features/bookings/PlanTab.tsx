import { useState } from "react";
import {
  Button, DataTable, EmptyState, IconCalendar, Notice, Pagination, StatCard, StatusBadge, cx, useIsPhone,
  type DataTableColumn,
} from "../../components/ui";
import { formatDay } from "../../lib/dates.ts";
import { formatPkr } from "../../utils/currency.ts";
import type { BookingDetail, InstallmentSchedule, ScheduleItem } from "./detailTypes.ts";
import { frequencyPhrase, installmentLabel, orderInstallments, phoneInstallments, planStats } from "./planForm.ts";
import { installmentStatus } from "./statusNames.ts";
import { Figure } from "./ui.tsx";

/** Rows per page on desktop, above which the shared pagination appears. */
const PAGE_SIZE = 20;

type Props = {
  booking: BookingDetail;
  schedule: InstallmentSchedule | null;
  /** Why the schedule could not be read, if it could not. */
  scheduleError: string | null;
  /** False while a failed reload leaves the previous figures on screen: nothing may act on them. */
  fresh: boolean;
  onCreate: () => void;
  onChange: () => void;
  onRecord: (item: ScheduleItem) => void;
  onRetry: () => void;
};

/** The Installment plan tab: no plan yet, a plan with nothing paid, a plan with payments, or one that no longer covers the balance. */
export function PlanTab({ booking, schedule, scheduleError, fresh, onCreate, onChange, onRecord, onRetry }: Props) {
  if (schedule?.hasSchedule && schedule.items.length > 0) {
    // Keyed by when the plan was built: a changed plan is a different list, so the page and the
    // phone's "Show all" start over instead of pointing past the end of a shorter schedule.
    return <Plan key={schedule.generatedAt ?? ""} booking={booking} schedule={schedule} fresh={fresh} onChange={onChange} onRecord={onRecord} />;
  }
  if (scheduleError) {
    return (
      <Notice tone="red" role="alert" title={scheduleError} action={<Button size="sm" variant="outline" onClick={onRetry}>Retry</Button>} />
    );
  }
  if (schedule === null) return <p className="py-10 text-center text-sm text-ink-muted">Loading...</p>;

  if (schedule.canGenerate && fresh) {
    const toSchedule = schedule.installmentPool + schedule.possessionAmount;
    return (
      <EmptyState
        icon={<IconCalendar size={26} />}
        title="No installment plan yet"
        message={`${formatPkr(toSchedule)} to schedule`}
        action={<Button onClick={onCreate}>Create plan</Button>}
      />
    );
  }
  return (
    <EmptyState
      icon={<IconCalendar size={26} />}
      title="No installment plan yet"
      message={booking.status === "Cancelled"
        ? "This booking is cancelled, so there is no plan to set up."
        : "The booking amount must be fully received before the installment plan unlocks."}
    />
  );
}

type PlanProps = Pick<Props, "booking" | "fresh" | "onChange" | "onRecord"> & { schedule: InstallmentSchedule };

function Plan({ booking, schedule, fresh, onChange, onRecord }: PlanProps) {
  const isPhone = useIsPhone();
  const [page, setPage] = useState(1);
  const [showAll, setShowAll] = useState(false);

  const stats = planStats(schedule);
  const unscheduled = fresh ? schedule.unscheduledBalance ?? 0 : 0;
  const canChange = fresh && schedule.canGenerate;
  // Collection continues after possession, when the unpaid balance is a receivable. Withheld while
  // money is outside the plan, because the server refuses those receipts until the plan is changed.
  const canRecord = fresh && (booking.status === "PaymentPlanActive" || booking.status === "PossessionGiven") && unscheduled <= 0;
  const payable = (item: ScheduleItem) => canRecord && item.status !== "Paid" && item.remainingBalance > 0;

  const ordered = orderInstallments(schedule.items);
  const paged = ordered.length > PAGE_SIZE;
  const currentPage = Math.min(page, Math.max(1, Math.ceil(ordered.length / PAGE_SIZE)));
  const rows = isPhone
    ? (showAll ? ordered : phoneInstallments(ordered))
    : (paged ? ordered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE) : ordered);

  const noPaymentYet = stats.paid <= 0 && stats.overdueCount === 0;
  const heading = schedule.numberOfInstallments
    ? `${frequencyPhrase(schedule.frequency, schedule.numberOfInstallments)}${schedule.installmentStartDate ? ` from ${formatDay(schedule.installmentStartDate)}` : ""}`
    : "Schedule";

  const columns: DataTableColumn<ScheduleItem>[] = [
    { key: "seq", header: "#", render: (item) => (item.type === "Possession" ? "—" : item.sequenceNumber), className: "text-ink-2" },
    {
      key: "type",
      header: "Type",
      render: (item) => item.type === "Possession"
        ? <span className="font-bold text-gold-text">Possession</span>
        : "Installment",
    },
    {
      key: "due",
      header: "Due",
      render: (item) => <span className={cx("whitespace-nowrap", item.isOverdue && item.status !== "Paid" && "font-bold text-danger")}>{formatDay(item.dueDate)}</span>,
    },
    { key: "amount", header: "Amount", render: (item) => <span className="whitespace-nowrap font-bold tabular-nums">{formatPkr(item.amount)}</span> },
    { key: "paid", header: "Paid", render: (item) => <span className="whitespace-nowrap tabular-nums text-ink-2">{formatPkr(item.amountPaid)}</span> },
    { key: "remaining", header: "Remaining", render: (item) => <span className="whitespace-nowrap tabular-nums text-ink-2">{formatPkr(item.remainingBalance)}</span> },
    { key: "status", header: "Status", render: (item) => <StatusBadge status={installmentStatus(item.status)} /> },
    {
      key: "action",
      header: <span className="sr-only">Action</span>,
      align: "right",
      render: (item) => payable(item)
        ? <Button size="sm" variant="outline" onClick={() => onRecord(item)}>Record payment</Button>
        : null,
    },
  ];

  const card = (item: ScheduleItem) => (
    <div className="rounded-card border border-line bg-card p-4 font-ui">
      <div className="flex items-center justify-between gap-2">
        <span className={cx("text-section font-extrabold", item.type === "Possession" ? "text-gold-text" : "text-primary")}>{installmentLabel(item)}</span>
        <StatusBadge status={installmentStatus(item.status)} />
      </div>
      <dl className="m-0 mt-2.5 grid grid-cols-2 gap-x-4 gap-y-2">
        <div>
          <dt className="text-label text-ink-muted">Due</dt>
          <dd className={cx("m-0 mt-0.5 text-sm font-semibold", item.isOverdue && item.status !== "Paid" ? "text-danger" : "text-ink")}>{formatDay(item.dueDate)}</dd>
        </div>
        <div>
          <dt className="text-label text-ink-muted">Amount</dt>
          <dd className="m-0 mt-0.5 text-sm font-semibold tabular-nums text-ink">{formatPkr(item.amount)}</dd>
        </div>
        {item.amountPaid > 0 && item.status !== "Paid" && (
          <div className="col-span-2">
            <dt className="text-label text-ink-muted">Remaining</dt>
            <dd className="m-0 mt-0.5 text-sm font-semibold tabular-nums text-ink">{formatPkr(item.remainingBalance)}</dd>
          </div>
        )}
      </dl>
      {payable(item) && <Button className="mt-3" variant="outline" fullWidth onClick={() => onRecord(item)}>Record payment</Button>}
    </div>
  );

  return (
    <div className="flex flex-col gap-4">
      {unscheduled > 0 && (
        <Notice
          tone="orange"
          role="status"
          title={`${formatPkr(unscheduled)} is not in the plan. Change the plan before recording more payments.`}
          action={canChange ? <Button size="sm" variant="outline" onClick={onChange}>Change plan</Button> : undefined}
        />
      )}

      {noPaymentYet ? (
        <div className="grid grid-cols-2 gap-2.5 md:grid-cols-3 md:gap-4">
          <StatCard label="Plan total" value={<Figure amount={formatPkr(stats.total)} />} />
          <StatCard tone="green" label="Paid" value={<Figure amount={formatPkr(stats.paid)} detail={`${stats.paidCount} of ${stats.count} paid`} />} />
          <StatCard
            className="max-md:col-span-2"
            label="Next due"
            value={stats.next ? <Figure amount={formatPkr(stats.next.remainingBalance)} detail={formatDay(stats.next.dueDate)} /> : "—"}
          />
        </div>
      ) : (
        <div className="grid grid-cols-2 gap-2.5 md:grid-cols-4 md:gap-4">
          <StatCard label="Plan total" value={<Figure amount={formatPkr(stats.total)} />} />
          <StatCard tone="green" label="Paid" value={<Figure amount={formatPkr(stats.paid)} detail={`${stats.paidCount} of ${stats.count} paid`} />} />
          <StatCard label="Remaining" value={<Figure amount={formatPkr(stats.remaining)} />} />
          <StatCard
            tone={stats.overdueCount > 0 ? "red" : "grey"}
            label="Overdue"
            value={<Figure amount={formatPkr(stats.overdue)} detail={`${stats.overdueCount} ${stats.overdueCount === 1 ? "installment" : "installments"}`} />}
          />
        </div>
      )}

      <section className="flex flex-col gap-3 font-ui">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="min-w-0">
            <h3 className="m-0 text-label font-extrabold uppercase tracking-[0.4px] text-ink-2">Schedule</h3>
            <p className="m-0 mt-0.5 text-body font-bold text-ink">{heading}</p>
          </div>
          {canChange && !isPhone && <Button size="sm" variant="outline" onClick={onChange}>Change plan</Button>}
        </div>
        {canChange && isPhone && <Button variant="outline" fullWidth onClick={onChange}>Change plan</Button>}

        {isPhone ? (
          <ul className="m-0 flex list-none flex-col gap-2.5 p-0">{rows.map((item) => <li key={item.id}>{card(item)}</li>)}</ul>
        ) : (
          <DataTable caption="Installment schedule" columns={columns} rows={rows} rowKey={(item) => item.id} minWidth={860} />
        )}
        {isPhone && !showAll && rows.length < ordered.length && (
          <Button variant="outline" size="lg" fullWidth onClick={() => setShowAll(true)}>Show all {ordered.length}</Button>
        )}
        {!isPhone && paged && (
          <Pagination page={currentPage} onPageChange={setPage} totalCount={ordered.length} pageSize={PAGE_SIZE} itemLabel="installments" />
        )}
      </section>
    </div>
  );
}
