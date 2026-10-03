import { StatusBadge, cx } from "../../../components/ui";
import { loanLine, loanStatus, rupees } from "./rules.ts";
import type { Loan } from "./types.ts";

/** One loan in the list: name and status, lender and account, and what is still owed. The whole card opens it. */
export function LoanCard({ loan, selected, onOpen }: { loan: Loan; selected: boolean; onOpen: () => void }) {
  return (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onOpen}
      className={cx(
        "block w-full min-w-0 cursor-pointer rounded-card border bg-card p-4 text-left font-ui text-ink transition-colors",
        "hover:border-ink-faint focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary",
        selected ? "border-primary ring-1 ring-primary" : "border-line",
      )}
    >
      <span className="flex items-start justify-between gap-2">
        <span className="min-w-0 text-body font-extrabold text-ink">{loan.name}</span>
        <StatusBadge status={loanStatus(loan)} />
      </span>
      <span className="mt-1 block truncate text-small text-ink-muted">{loanLine(loan)}</span>
      <span className="mt-3 block text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">Outstanding principal</span>
      <span className="block text-section font-extrabold tabular-nums text-ink">{rupees(loan.currentBalance)}</span>
    </button>
  );
}
