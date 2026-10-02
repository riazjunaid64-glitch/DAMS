import { cx } from "../../../components/ui";
import { formatDate, formatMoney } from "../home/format.ts";
import { ReportCard } from "./ReportCard.tsx";
import type { ProfitLoss } from "./types.ts";

const row = "flex items-center justify-between gap-4 border-t border-line-soft px-[18px] py-4 md:px-5";

function Placeholder() {
  return (
    <div aria-hidden="true">
      {[0, 1, 2].map((index) => (
        <div key={index} className={row}>
          <span className="h-4 w-32 animate-pulse rounded bg-track" />
          <span className="h-4 w-24 animate-pulse rounded bg-track" />
        </div>
      ))}
    </div>
  );
}

/**
 * The three figures the sheet exists to answer. They are stated as the server sent them, never
 * computed here. The income and expense lines and the prior year still arrive and are still in the
 * Excel export; they are not shown.
 */
export function ProfitLossReport({ report, loading }: { report: ProfitLoss | null; loading: boolean }) {
  if (!report) {
    return (
      <ReportCard title="Profit & loss" busy={false}>
        {loading ? <Placeholder /> : null}
      </ReportCard>
    );
  }
  const lines = [
    { label: "Total revenue", value: report.totalIncome, tone: "text-success" },
    { label: "Total expenses", value: report.totalExpenses, tone: "text-danger" },
  ];
  const profitTone = report.netProfit >= 0 ? "text-success" : "text-danger";
  return (
    <ReportCard
      title={`Profit & loss · ${report.periodLabel}`}
      subtitle={(
        <>
          <span>{formatDate(report.periodStart)} – {formatDate(report.periodEnd)}</span>
          {report.projectName && <span className="block">Project: {report.projectName}</span>}
        </>
      )}
      busy={loading}
    >
      <dl className="m-0">
        {lines.map((line) => (
          <div key={line.label} className={row}>
            <dt className="text-body text-ink">{line.label}</dt>
            <dd className={cx("m-0 text-body font-extrabold tabular-nums", line.tone)}>{formatMoney(line.value)}</dd>
          </div>
        ))}
        <div className={cx(row, "bg-page")}>
          <dt className="text-body font-extrabold text-ink">Net profit</dt>
          <dd className={cx("m-0 text-section font-extrabold tabular-nums", profitTone)}>{formatMoney(report.netProfit)}</dd>
        </div>
      </dl>
    </ReportCard>
  );
}
