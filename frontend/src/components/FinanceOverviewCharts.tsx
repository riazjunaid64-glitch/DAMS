interface FinanceOverviewChartsProps {
  totalRevenue: number;
  totalExpenses: number;
  netProfit: number;
  automaticRevenue: number;
  manualRevenue: number;
  outstandingAmount: number;
  overdueAmount: number;
  formatMoney: (n: number) => string;
  loading?: boolean;
}

const C = {
  revenue: "#059669",
  expense: "#e11d48",
  payments: "#390217",
  manual: "#c99a5b",
  outstanding: "#d97706",
  overdue: "#ea580c",
};

function pct(part: number, whole: number) {
  if (whole <= 0) return 0;
  return Math.max(0, Math.min(100, (part / whole) * 100));
}

export default function FinanceOverviewCharts({
  totalRevenue,
  totalExpenses,
  netProfit,
  automaticRevenue,
  manualRevenue,
  outstandingAmount,
  overdueAmount,
  formatMoney,
  loading,
}: FinanceOverviewChartsProps) {
  const flowMax = Math.max(totalRevenue, totalExpenses, 1);
  const revenueTotal = automaticRevenue + manualRevenue;
  const receivableTotal = outstandingAmount + overdueAmount;

  return (
    <div className="finance-charts">
      {/* Panel 1 — Revenue vs Expenses */}
      <section className="chart-card">
        <header className="chart-card__head">
          <h3 className="chart-card__title">Revenue vs Expenses</h3>
          <span className="chart-card__meta">This period</span>
        </header>

        <div className="chart-bars">
          <div className="chart-bar-row">
            <div className="chart-bar-row__top">
              <span className="chart-bar-row__label">
                <span className="chart-dot" style={{ background: C.revenue }} />
                Revenue
              </span>
              <span className="chart-bar-row__value">{loading ? "…" : formatMoney(totalRevenue)}</span>
            </div>
            <div className="chart-track">
              <div className="chart-fill" style={{ width: `${pct(totalRevenue, flowMax)}%`, background: C.revenue }} />
            </div>
          </div>

          <div className="chart-bar-row">
            <div className="chart-bar-row__top">
              <span className="chart-bar-row__label">
                <span className="chart-dot" style={{ background: C.expense }} />
                Expenses
              </span>
              <span className="chart-bar-row__value">{loading ? "…" : formatMoney(totalExpenses)}</span>
            </div>
            <div className="chart-track">
              <div className="chart-fill" style={{ width: `${pct(totalExpenses, flowMax)}%`, background: C.expense }} />
            </div>
          </div>
        </div>

        <footer className="chart-card__foot">
          <span className="chart-card__foot-label">Net profit</span>
          <span
            className="chart-card__foot-value"
            style={{ color: netProfit >= 0 ? C.revenue : C.expense }}
          >
            {loading ? "…" : formatMoney(netProfit)}
          </span>
        </footer>
      </section>

      {/* Panel 2 — Revenue sources */}
      <section className="chart-card">
        <header className="chart-card__head">
          <h3 className="chart-card__title">Revenue Sources</h3>
          <span className="chart-card__meta">{loading ? "…" : formatMoney(revenueTotal)}</span>
        </header>

        {revenueTotal > 0 ? (
          <>
            <div className="chart-stack">
              {manualRevenue > 0 && (
                <div
                  className="chart-stack__seg"
                  style={{ width: `${pct(manualRevenue, revenueTotal)}%`, background: C.manual }}
                  title={`Manual · ${formatMoney(manualRevenue)}`}
                />
              )}
              {automaticRevenue > 0 && (
                <div
                  className="chart-stack__seg"
                  style={{ width: `${pct(automaticRevenue, revenueTotal)}%`, background: C.payments }}
                  title={`Payments · ${formatMoney(automaticRevenue)}`}
                />
              )}
            </div>
            <ul className="chart-legend">
              <li>
                <span className="chart-dot" style={{ background: C.manual }} />
                <span className="chart-legend__label">Manual entries</span>
                <span className="chart-legend__value">
                  {formatMoney(manualRevenue)} · {pct(manualRevenue, revenueTotal).toFixed(0)}%
                </span>
              </li>
              <li>
                <span className="chart-dot" style={{ background: C.payments }} />
                <span className="chart-legend__label">Booking payments</span>
                <span className="chart-legend__value">
                  {formatMoney(automaticRevenue)} · {pct(automaticRevenue, revenueTotal).toFixed(0)}%
                </span>
              </li>
            </ul>
          </>
        ) : (
          <p className="chart-empty">No revenue recorded for this period.</p>
        )}
      </section>

      {/* Panel 3 — Receivables */}
      <section className="chart-card">
        <header className="chart-card__head">
          <h3 className="chart-card__title">Receivables</h3>
          <span className="chart-card__meta">{loading ? "…" : formatMoney(receivableTotal)}</span>
        </header>

        {receivableTotal > 0 ? (
          <div className="chart-bars">
            <div className="chart-bar-row">
              <div className="chart-bar-row__top">
                <span className="chart-bar-row__label">
                  <span className="chart-dot" style={{ background: C.outstanding }} />
                  Outstanding
                </span>
                <span className="chart-bar-row__value">{formatMoney(outstandingAmount)}</span>
              </div>
              <div className="chart-track">
                <div className="chart-fill" style={{ width: `${pct(outstandingAmount, receivableTotal)}%`, background: C.outstanding }} />
              </div>
            </div>
            <div className="chart-bar-row">
              <div className="chart-bar-row__top">
                <span className="chart-bar-row__label">
                  <span className="chart-dot" style={{ background: C.overdue }} />
                  Overdue
                </span>
                <span className="chart-bar-row__value">{formatMoney(overdueAmount)}</span>
              </div>
              <div className="chart-track">
                <div className="chart-fill" style={{ width: `${pct(overdueAmount, receivableTotal)}%`, background: C.overdue }} />
              </div>
            </div>
          </div>
        ) : (
          <p className="chart-empty">Nothing outstanding — all payments are up to date.</p>
        )}
      </section>
    </div>
  );
}
