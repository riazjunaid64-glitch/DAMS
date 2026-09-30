import { Button, cx, useIsPhone } from "../../components/ui";
import { formatPkr } from "../../utils/currency.ts";
import { SavedProof } from "../proof/SavedProof.tsx";
import { movementDetail, movementTitle } from "./forms.ts";
import type { MoneyMovement } from "./types.ts";

type Props = {
  heading: string;
  rows: MoneyMovement[];
  /** Where the proof goes: a commission payment, or a rebate given to the customer. */
  ownerType: "CommissionPayout" | "RebateDisbursement";
  /** A cancelled booking is read-only: proofs still open, nothing is attached or reversed. */
  readOnly: boolean;
  /** Reversing and attaching are withdrawn while the figures are not current. */
  locked: boolean;
  onReverse: (row: MoneyMovement) => void;
  /** Reloads the workspace once a proof file is attached. */
  onProofChanged: () => void | Promise<void>;
};

/** The payments of one commission, or the amounts given to the customer for the rebate. */
export function MovementRows({ heading, rows, ownerType, readOnly, locked, onReverse, onProofChanged }: Props) {
  const isPhone = useIsPhone();
  if (rows.length === 0) return null;
  return (
    <div className="mt-3.5 rounded-field bg-page px-3.5 py-3 font-ui">
      <p className="m-0 mb-1.5 text-caption font-bold uppercase tracking-[0.4px] text-ink-2">{heading}</p>
      <ul className="m-0 flex list-none flex-col p-0">
        {rows.map((row) => {
          const reversed = row.amount > 0 && row.reversedAmount >= row.amount;
          const evidence = row.evidence[0];
          const proof = reversed ? null : evidence && { id: evidence.id, fileName: evidence.originalFileName, fileSize: evidence.fileSize };
          const proofControl = reversed
            ? null
            : readOnly && !proof
              ? null
              : <SavedProof ownerType={ownerType} ownerId={row.id} proof={proof} onChanged={onProofChanged} />;
          const reverse = !readOnly && !reversed && (
            <Button variant="link" size="sm" disabled={locked} onClick={() => onReverse(row)} className="font-extrabold text-danger">Reverse</Button>
          );
          return (
            <li key={row.id} className="border-t border-line-soft py-2.5 first:border-t-0 first:pt-0 last:pb-0">
              <div className={cx("flex gap-x-4 gap-y-1", isPhone ? "flex-col" : "items-start justify-between")}>
                <div className="min-w-0">
                  <p className="m-0 text-body font-extrabold text-ink">{movementTitle(row)}</p>
                  <p className="m-0 mt-0.5 text-small text-ink-muted">{movementDetail(row)}</p>
                </div>
                <div className={cx("flex shrink-0 items-center gap-4", isPhone && "justify-between")}>
                  <span className="whitespace-nowrap text-body font-extrabold tabular-nums text-ink">
                    {reversed ? <span className="text-ink-muted line-through">{formatPkr(row.amount)}</span> : formatPkr(row.amount - row.reversedAmount)}
                  </span>
                  {reversed ? <span className="text-small font-extrabold text-ink-muted">Reversed</span> : (
                    <span className="inline-flex items-center gap-4">{proofControl}{reverse}</span>
                  )}
                </div>
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
