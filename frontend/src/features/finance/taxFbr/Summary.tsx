import { Button, Notice, StatCard, type StatCardState } from "../../../components/ui";
import { formatRs, type WhtPayableSummary } from "../whtTypes.ts";
import { paymentsText, suppliersText } from "./rules.ts";

type CardsProps = {
  /** All-time figures straight from the server; the screen never adds them up itself. */
  summary: WhtPayableSummary | null;
  state: StatCardState;
  error: string | null;
  onRetry: () => void;
};

const sign = "hidden items-center justify-center text-[22px] font-extrabold text-ink-faint md:flex";

/**
 * Brought forward + Withheld from suppliers − Deposited to FBR = Still owed to FBR. A row with the
 * signs between the cards on desktop, a 2 × 2 grid on a phone. The figures do not follow From / To.
 */
export function SummaryCards({ summary, state, error, onRetry }: CardsProps) {
  const value = (amount: number | undefined) => formatRs(amount ?? 0);
  return (
    <section aria-label="Tax owed to FBR" className="flex flex-col gap-3 font-ui">
      <div className="grid grid-cols-2 gap-2.5 md:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)_auto_minmax(0,1fr)_auto_minmax(0,1fr)] md:items-stretch md:gap-3">
        <StatCard label="Brought forward" state={state} value={value(summary?.openingPayable)} note="Tax owed before go-live" />
        <span aria-hidden="true" className={sign}>+</span>
        <StatCard label="Withheld from suppliers" state={state} value={value(summary?.totalWithheldAllTime)} note="Kept back when paying suppliers" />
        <span aria-hidden="true" className={sign}>−</span>
        <StatCard label="Deposited to FBR" state={state} value={value(summary?.totalDepositedAllTime)} note="Paid with challans" />
        <span aria-hidden="true" className={sign}>=</span>
        <StatCard label="Still owed to FBR" tone="gold" highlight state={state} value={value(summary?.outstandingPayable)} note="Sitting in your accounts, not yours to spend" />
      </div>
      {error && <Notice tone="red" role="alert" title={error} action={<Button variant="outline" onClick={onRetry}>Try again</Button>} />}
    </section>
  );
}

type LineProps = {
  summary: Pick<WhtPayableSummary, "withheldInPeriod" | "paymentCount" | "depositedInPeriod">;
  /** The rows of the By supplier list; null while that list has not answered, and then the clause is left out. */
  supplierCount: number | null;
};

const amount = (value: number) => <b className="font-extrabold text-ink">{formatRs(value)}</b>;

/** "In the selected period: withheld Rs 412,300 from 18 payments to 6 suppliers · deposited Rs 412,000". */
export function PeriodLine({ summary, supplierCount }: LineProps) {
  return (
    <p className="m-0 font-ui text-small text-ink-muted">
      In the selected period: withheld {amount(summary.withheldInPeriod)} from {paymentsText(summary.paymentCount)}
      {supplierCount !== null && <> to {suppliersText(supplierCount)}</>} · deposited {amount(summary.depositedInPeriod)}
    </p>
  );
}
