import { useState, type ReactNode } from "react";
import { BottomSheet, Button, IconArrowDown, IconArrowUp, IconDownload, IconMore, IconPencil, StatusBadge } from "../../../components/ui";
import { loanLine, loanStatus, rupees } from "./rules.ts";
import type { Loan } from "./types.ts";

type Props = {
  loan: Loan;
  isPhone: boolean;
  /** Export is offered only once there is a movement to export. */
  canExport: boolean;
  exporting: boolean;
  onEdit: () => void;
  onRepay: () => void;
  onReceive: () => void;
  onExport: () => void;
};

function Figure({ label, value }: { label: string; value: number }) {
  return (
    <div className="min-w-0 rounded-field bg-page px-3 py-2.5">
      <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">{label}</p>
      <p className="m-0 mt-0.5 truncate text-section font-extrabold tabular-nums text-ink">{rupees(value)}</p>
    </div>
  );
}

function SheetItem({ icon, label, disabled, onSelect }: { icon: ReactNode; label: string; disabled?: boolean; onSelect: () => void }) {
  return (
    <li>
      <button
        type="button"
        disabled={disabled}
        onClick={onSelect}
        className="flex h-12 w-full cursor-pointer items-center gap-3 border-0 bg-transparent px-0 text-left text-body font-extrabold text-ink focus-visible:outline-2 focus-visible:outline-primary disabled:cursor-not-allowed disabled:text-ink-faint"
      >
        <span className="flex text-ink-2">{icon}</span>
        {label}
      </button>
    </li>
  );
}

/**
 * The open loan: what is owed, borrowed, repaid and paid in interest, and the buttons that move it.
 * A closed loan takes no new movements until it is reopened from Edit. On a phone the less common
 * actions sit in a sheet behind ⋯.
 */
export function LoanSummary({ loan, isPhone, canExport, exporting, onEdit, onRepay, onReceive, onExport }: Props) {
  const [sheetOpen, setSheetOpen] = useState(false);
  const fromSheet = (action: () => void) => () => {
    setSheetOpen(false);
    action();
  };

  return (
    <section className="rounded-card border border-line bg-card p-[18px] font-ui md:p-5">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2.5">
            <h2 className="m-0 text-[20px] leading-tight font-extrabold text-ink md:text-[22px]">{loan.name}</h2>
            <StatusBadge status={loanStatus(loan)} />
          </div>
          <p className="m-0 mt-1 text-small text-ink-muted">{loanLine(loan, "Loan account")}</p>
        </div>
        {!isPhone && <Button iconOnly variant="outline" icon={<IconPencil size={16} />} aria-label="Edit loan" onClick={onEdit} />}
      </div>

      <div className="mt-4 grid grid-cols-2 gap-2.5 md:grid-cols-4">
        <Figure label="Outstanding principal" value={loan.currentBalance} />
        <Figure label="Total borrowed" value={loan.drawnPrincipal} />
        <Figure label="Principal repaid" value={loan.repaidPrincipal} />
        <Figure label="Interest paid" value={loan.interestPaid} />
      </div>
      {loan.openingBalance !== 0 && (
        <p className="m-0 mt-3 text-small text-ink-muted">
          Outstanding principal includes {rupees(loan.openingBalance)} brought forward as the loan account's opening balance, which is not part of Total borrowed.
        </p>
      )}

      {isPhone ? (
        <div className="mt-4 grid grid-cols-[minmax(0,1fr)_auto] gap-2.5">
          <Button size="lg" fullWidth disabled={!loan.isActive} onClick={onRepay}>Record repayment</Button>
          <Button size="lg" iconOnly variant="outline" icon={<IconMore size={18} />} aria-label={`More for ${loan.name}`} onClick={() => setSheetOpen(true)} />
        </div>
      ) : (
        <div className="mt-4 flex flex-wrap items-center gap-2.5">
          <Button icon={<IconArrowDown size={16} />} disabled={!loan.isActive} onClick={onRepay}>Record repayment</Button>
          <Button variant="outline" disabled={!loan.isActive} onClick={onReceive}>Receive loan funds</Button>
        </div>
      )}
      {!loan.isActive && <p className="m-0 mt-2.5 text-small text-ink-muted">This loan is closed. Reopen it from Edit loan to record new movements.</p>}

      {isPhone && (
        <BottomSheet open={sheetOpen} onClose={() => setSheetOpen(false)} title="More actions" footer={null}>
          <ul className="m-0 flex list-none flex-col p-0">
            <SheetItem icon={<IconArrowUp size={18} />} label="Receive loan funds" disabled={!loan.isActive} onSelect={fromSheet(onReceive)} />
            <SheetItem icon={<IconPencil size={18} />} label="Edit loan" onSelect={fromSheet(onEdit)} />
            <SheetItem icon={<IconDownload size={18} />} label="Export activity" disabled={!canExport || exporting} onSelect={fromSheet(onExport)} />
          </ul>
        </BottomSheet>
      )}
    </section>
  );
}
