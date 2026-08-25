import { Fragment } from "react";
import { UNIT_CLASS, exactAmount, shortAmountParts } from "./financeAmounts.ts";

/**
 * A card amount: short, with each unit coloured, and the exact figure on hover.
 *
 * Colour, size and weight are all inherited, so this drops into an existing card without changing
 * anything about how that card looks apart from the number itself. Only cards use it — a report or
 * a ledger prints exactAmount, because a rounded record is a wrong one. See financeAmounts.
 */
export default function ShortAmount({ value, className }: { value: number; className?: string }) {
  const { sign, parts } = shortAmountParts(value);
  return (
    <span className={className} title={exactAmount(value)}>
      {sign}Rs{" "}
      {parts.map((part, index) => (
        <Fragment key={part.unit ?? index}>
          {index > 0 && " "}
          {part.value}
          {part.unit && <> <span className={UNIT_CLASS[part.unit]}>{part.unit}</span></>}
        </Fragment>
      ))}
    </span>
  );
}
