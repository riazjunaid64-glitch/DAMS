import type { ReactNode } from "react";
import { cx, IconCheck, Notice, StatusBadge } from "../../../components/ui";
import { formatDate, formatMoney } from "../home/format.ts";
import { sentenceCase } from "./format.ts";
import { ReportCard } from "./ReportCard.tsx";
import type { BalanceSheet, BalanceSheetGroup, BalanceSheetLine } from "./types.ts";

const linePad = "px-[18px] md:px-5";

function Line({ line }: { line: BalanceSheetLine }) {
  return (
    <div className={cx("flex items-baseline justify-between gap-3 py-1.5 text-sm text-ink", linePad)}>
      <span className="min-w-0">
        {line.ledgerCode && <span className="mr-2 tabular-nums text-ink-faint">{line.ledgerCode}</span>}
        {line.name}
      </span>
      <span className="shrink-0 whitespace-nowrap font-semibold tabular-nums">{formatMoney(line.amount)}</span>
    </div>
  );
}

function Subtotal({ label, amount }: { label: string; amount: number }) {
  return (
    <div className="mx-[18px] mt-1 flex items-baseline justify-between gap-3 border-t border-line-soft py-2 text-sm font-extrabold text-ink md:mx-5">
      <span>{label}</span>
      <span className="shrink-0 whitespace-nowrap tabular-nums">{formatMoney(amount)}</span>
    </div>
  );
}

function GroupBlock({ group }: { group: BalanceSheetGroup }) {
  const name = sentenceCase(group.name);
  return (
    <section className="pt-3">
      <h4 className={cx("m-0 pb-1 text-label font-extrabold uppercase tracking-[0.4px] text-ink-2", linePad)}>{name}</h4>
      {group.lines.map((line) => <Line key={line.accountId} line={line} />)}
      <Subtotal label={`Total ${name.toLowerCase()}`} amount={group.total} />
    </section>
  );
}

function Bar({ label, amount, className }: { label: string; amount: number; className?: string }) {
  return (
    <div className={cx("flex items-baseline justify-between gap-3 bg-gold-soft px-[18px] py-3.5 text-body font-extrabold text-ink md:px-5", className)}>
      <span className="min-w-0">{label}</span>
      <span className="shrink-0 whitespace-nowrap tabular-nums">{formatMoney(amount)}</span>
    </div>
  );
}

function Column({ title, children, className }: { title: string; children: ReactNode; className?: string }) {
  return (
    <div className={cx("pb-3", className)}>
      <h3 className={cx("m-0 pt-4 text-body font-extrabold text-ink", linePad)}>{title}</h3>
      {children}
    </div>
  );
}

function Placeholder() {
  return (
    <div aria-hidden="true" className="grid gap-6 p-5 md:grid-cols-2">
      {[0, 1].map((column) => (
        <div key={column} className="flex flex-col gap-3">
          {[0, 1, 2, 3, 4, 5].map((row) => <span key={row} className="h-4 animate-pulse rounded bg-track" />)}
        </div>
      ))}
    </div>
  );
}

/**
 * Assets on the left, liabilities and capital on the right, a shaded total bar under each. The
 * bars sit in their own grid row, so they stay level however long either column is.
 */
export function BalanceSheetReport({ report, loading }: { report: BalanceSheet | null; loading: boolean }) {
  if (!report) {
    return <ReportCard title="Balance sheet">{loading ? <Placeholder /> : null}</ReportCard>;
  }
  const review = report.unbalancedAccounts.length ? ` Review: ${report.unbalancedAccounts.join(", ")}.` : "";
  const window = report.retainedProfitStart ? `between ${formatDate(report.retainedProfitStart)} and this date` : "up to this date";
  return (
    <ReportCard
      title="Balance sheet"
      subtitle={`As at ${formatDate(report.asAt)}`}
      busy={loading}
      badge={(
        <StatusBadge status="" tone={report.isBalanced ? "green" : "red"}>
          {report.isBalanced ? <><IconCheck size={13} className="mr-1" />Balanced</> : "Out of balance"}
        </StatusBadge>
      )}
    >
      {!report.isBalanced && (
        <div className="px-[18px] pt-4 md:px-5">
          <Notice tone="red" role="alert" title={`Statement is out of balance by ${formatMoney(report.imbalance)}.`} message={review.trim() || undefined} />
        </div>
      )}
      <div className="grid md:grid-cols-2">
        <Column title="Assets" className="md:col-start-1 md:row-start-1">
          {report.assetGroups.map((group) => <GroupBlock key={group.name} group={group} />)}
        </Column>
        <Bar label="Total assets" amount={report.totalAssets} className="md:col-start-1 md:row-start-2" />
        <Column title="Liabilities & capital" className="md:col-start-2 md:row-start-1 md:border-l md:border-line-soft">
          {report.liabilityGroups.map((group) => <GroupBlock key={group.name} group={group} />)}
          <section className="pt-3">
            <h4 className={cx("m-0 pb-1 text-label font-extrabold uppercase tracking-[0.4px] text-ink-2", linePad)}>Capital</h4>
            {report.capitalLines.map((line) => <Line key={line.accountId} line={line} />)}
            <Line line={{ accountId: -1, ledgerCode: null, name: "Retained profit (per the ledger)", amount: report.retainedProfit }} />
            <Subtotal label="Total capital" amount={report.totalCapital} />
          </section>
        </Column>
        <Bar label="Total liabilities & capital" amount={report.totalLiabilitiesAndCapital} className="md:col-start-2 md:row-start-2 md:border-l md:border-line-soft" />
      </div>
      {report.unpostedFixedAssetCharge > 0 && (
        <div className="p-4 md:px-5">
          <Notice
            tone="gold"
            title={`${formatMoney(report.unpostedFixedAssetCharge)} of fixed assets bought ${window} is charged in Profit & loss, but not in retained profit here.`}
          />
        </div>
      )}
    </ReportCard>
  );
}
