import type { FinanceChartData } from "../lib/financeChartData.ts";

interface FinanceChartsProps {
  data: FinanceChartData | null;
  loading: boolean;
  formatMoney: (n: number) => string;
}

export default function FinanceCharts({ data, loading, formatMoney }: FinanceChartsProps) {
  const series = data?.series ?? [];
  const distribution = data?.distribution ?? [];
  const max = Math.max(1, ...series.flatMap((s) => [s.revenue, s.expense]));
  const hasSeries = series.some((s) => s.revenue > 0 || s.expense > 0);
  const hasDist = distribution.some((d) => d.revenue > 0);

  return (
    <div className="fin-charts">
      {/* Revenue vs Expenses */}
      <section className="fin-chart-card">
        <header className="fin-chart-card__head">
          <h3 className="fin-chart-card__title">Revenue vs Expenses</h3>
          <div className="fin-legend">
            <span className="fin-legend__item"><span className="fin-legend__dot fin-legend__dot--rev" />Revenue</span>
            <span className="fin-legend__item"><span className="fin-legend__dot fin-legend__dot--exp" />Expense</span>
          </div>
        </header>

        {loading ? (
          <div className="fin-chart-empty">Loading…</div>
        ) : !hasSeries ? (
          <div className="fin-chart-empty">No revenue or expenses in this period.</div>
        ) : (
          <div className="fin-bars" role="img" aria-label="Revenue versus expenses by period">
            {series.map((b, i) => (
              <div
                className="fin-bar-group"
                key={`${b.label}-${i}`}
                title={`${b.label} · Revenue ${formatMoney(b.revenue)} · Expense ${formatMoney(b.expense)}`}
              >
                <div className="fin-bar-pair">
                  <div className="fin-bar fin-bar--rev" style={{ height: `${(b.revenue / max) * 100}%` }} />
                  <div className="fin-bar fin-bar--exp" style={{ height: `${(b.expense / max) * 100}%` }} />
                </div>
                <span className="fin-bar-label">{b.label}</span>
              </div>
            ))}
          </div>
        )}
      </section>

      {/* Project Distribution */}
      <section className="fin-chart-card">
        <header className="fin-chart-card__head">
          <h3 className="fin-chart-card__title">Project Distribution</h3>
          <span className="fin-chart-card__meta">Revenue share</span>
        </header>

        {loading ? (
          <div className="fin-chart-empty">Loading…</div>
        ) : !hasDist ? (
          <div className="fin-chart-empty">No revenue recorded for this period.</div>
        ) : (
          <ul className="fin-dist">
            {distribution.map((d, i) => (
              <li className="fin-dist__row" key={`${d.name}-${i}`} title={`${d.name} · ${formatMoney(d.revenue)}`}>
                <div className="fin-dist__top">
                  <span className="fin-dist__name">{d.name}</span>
                  <span className="fin-dist__pct">{d.percent.toFixed(0)}%</span>
                </div>
                <div className="fin-dist__track">
                  <div className="fin-dist__fill" style={{ width: `${Math.max(2, d.percent)}%` }} />
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
