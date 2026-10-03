import { ListCard, StatusBadge, cx } from "../../../components/ui";
import { balanceClass, holderActivity, holderStatus, rupees } from "./rules.ts";
import type { Holder } from "./types.ts";

/**
 * One card per person, in the server's order. The whole card opens the person: beside the list on
 * a desktop, as its own page on a phone.
 */
export function PeopleList({ holders, selectedId, onOpen }: { holders: readonly Holder[]; selectedId: number | null; onOpen: (holder: Holder) => void }) {
  return (
    <ul className="m-0 flex list-none flex-col gap-2.5 p-0" aria-label="People">
      {holders.map((holder) => (
        <li key={holder.financeAccountId}>
          <ListCard
            selected={holder.financeAccountId === selectedId}
            onClick={() => onOpen(holder)}
            title={
              <span className="flex items-center justify-between gap-2 text-ink">
                <span className="min-w-0 truncate">{holder.personName}</span>
                <span className="flex shrink-0 items-center gap-2">
                  {!holder.isActive && <StatusBadge status="Inactive" />}
                  <span className={cx("tabular-nums", balanceClass(holder.currentBalance))}>{rupees(holder.currentBalance)}</span>
                </span>
              </span>
            }
            detail={
              <span className="flex flex-wrap items-baseline justify-between gap-x-3">
                <span className={balanceClass(holder.currentBalance)}>{holderStatus(holder)}</span>
                <span className="font-normal text-ink-muted">{holderActivity(holder)}</span>
              </span>
            }
          />
        </li>
      ))}
    </ul>
  );
}
