import { useState } from "react";
import { BottomSheet, Button, IconArrowDown, IconArrowUp, IconMore, IconPlus, SheetAction, cx } from "../../../components/ui";
import { balanceClass, canReturnCash, giveAction, holderStatus, openSince, rupees, type MovementIntent } from "./rules.ts";
import type { Holder } from "./types.ts";

type Props = {
  holder: Holder;
  isPhone: boolean;
  onExpense: () => void;
  onMovement: (intent: MovementIntent) => void;
};

/**
 * The open person: how long the float has been open, what they hold or are owed, and the buttons that
 * move it. An inactive float takes nothing new, because the server refuses it. On a phone the two
 * money moves sit in a sheet behind ⋯.
 */
export function PersonPanel({ holder, isPhone, onExpense, onMovement }: Props) {
  const [sheetOpen, setSheetOpen] = useState(false);
  const give = giveAction(holder);
  const fromSheet = (intent: MovementIntent) => () => {
    setSheetOpen(false);
    onMovement(intent);
  };

  const balance = (
    <div className={cx(!isPhone && "text-right")}>
      <p className="m-0 text-small font-bold text-ink-2">{holderStatus(holder, "panel")}</p>
      <p className={cx("m-0 text-[26px] leading-tight font-extrabold tabular-nums", balanceClass(holder.currentBalance))}>{rupees(holder.currentBalance)}</p>
    </div>
  );

  return (
    <section className="rounded-card border border-line bg-card p-[18px] font-ui md:p-5">
      <div className={cx("flex gap-3", isPhone ? "flex-col" : "items-start justify-between")}>
        <div className="min-w-0">
          <h2 className="m-0 text-[20px] leading-tight font-extrabold text-ink md:text-[22px]">{holder.personName}</h2>
          <p className="m-0 mt-1 text-small text-ink-muted">{openSince(holder)}</p>
        </div>
        {balance}
      </div>

      {isPhone ? (
        <div className="mt-4 grid grid-cols-[minmax(0,1fr)_auto] gap-2.5">
          <Button size="lg" fullWidth disabled={!holder.isActive} onClick={onExpense}>Record an expense</Button>
          <Button size="lg" iconOnly variant="outline" icon={<IconMore size={18} />} aria-label={`More for ${holder.personName}`} onClick={() => setSheetOpen(true)} />
        </div>
      ) : (
        <div className="mt-4 flex flex-wrap items-center gap-2.5">
          <Button icon={<IconPlus size={16} />} disabled={!holder.isActive} onClick={onExpense}>Record an expense</Button>
          <Button variant="outline" disabled={!holder.isActive} onClick={() => onMovement(give.intent)}>{give.label}</Button>
          <Button variant="outline" disabled={!canReturnCash(holder)} onClick={() => onMovement("return")}>Record cash returned</Button>
        </div>
      )}

      {isPhone && (
        <BottomSheet open={sheetOpen} onClose={() => setSheetOpen(false)} title="More actions" footer={null}>
          <ul className="m-0 flex list-none flex-col p-0">
            <SheetAction icon={<IconArrowUp size={18} />} label={give.label} disabled={!holder.isActive} onSelect={fromSheet(give.intent)} />
            <SheetAction icon={<IconArrowDown size={18} />} label="Record cash returned" disabled={!canReturnCash(holder)} onSelect={fromSheet("return")} />
          </ul>
        </BottomSheet>
      )}
    </section>
  );
}
