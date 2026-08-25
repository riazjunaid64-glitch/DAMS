/**
 * Short money for dashboard cards.
 *
 * A card is read at a glance, and `Rs 90,040,000` cannot be: the reader has to count digits to
 * learn the magnitude, which is the one thing the card exists to convey. So a card states the
 * largest whole unit and what is left over — `Rs 9 Cr 40,000` — and the suffix is coloured, so
 * scale registers before the digits are read at all.
 *
 * At most two units are ever stated, and a remainder large enough to carry a unit is given one:
 * `Rs 999 Cr 98 L`, never `Rs 999 Cr 9,787,889`. A raw tail that long is the original problem
 * again, printed after a suffix.
 *
 * Two rules hold this together, and both are structural rather than a matter of care:
 *
 * 1. One system, everywhere. Crore and Billion are NOT the same size, and a dashboard that says
 *    Cr on one card and B on the next invites the reader to compare them as if they were. The
 *    scale is chosen once, in AMOUNT_SYSTEM, and every card on every screen follows it.
 * 2. Short is for cards only. Statements, reports and ledgers print the exact figure to the
 *    paisa — they are the record, and a rounded record is a wrong one. See exactAmount.
 */

export type AmountUnit = "L" | "Cr" | "M" | "B";

/** The legend colours: scale is meant to register from the suffix alone. */
export const UNIT_CLASS: Record<AmountUnit, string> = {
  L: "text-sky-400",
  Cr: "text-amber-400",
  M: "text-violet-400",
  B: "text-pink-400",
};

export type AmountSystem = "lakh-crore" | "million-billion";

const SCALES: Record<AmountSystem, { unit: AmountUnit; size: number }[]> = {
  // Largest first: the first unit a figure clears is the one it is stated in.
  "lakh-crore": [{ unit: "Cr", size: 10_000_000 }, { unit: "L", size: 100_000 }],
  "million-billion": [{ unit: "B", size: 1_000_000_000 }, { unit: "M", size: 1_000_000 }],
};

/** Lakh/Crore is how amounts are spoken here, so it is what the cards say. */
export const AMOUNT_SYSTEM: AmountSystem = "lakh-crore";

const group = (value: number, fractionDigits = 0) =>
  value.toLocaleString("en-PK", { maximumFractionDigits: fractionDigits });

/** The exact figure, to the paisa when there is one. What every report and ledger prints. */
export function exactAmount(value: number) {
  return `${value < 0 ? "−" : ""}Rs ${group(Math.abs(value), 2)}`;
}

/** One spoken piece of an amount: `999 Cr`, `98 L`, or a bare `40,000` below the smallest unit. */
export type AmountPart = { value: string; unit: AmountUnit | null };

export type ShortAmountParts = { sign: string; parts: AmountPart[] };

export function shortAmountParts(value: number, system: AmountSystem = AMOUNT_SYSTEM): ShortAmountParts {
  const sign = value < 0 ? "−" : "";
  const absolute = Math.abs(value);
  // Rounded before the unit is chosen, not after: Rs 9,999,999.70 is a crore to any reader, and
  // deciding on the raw value would state it as 99 L instead.
  const rounded = Math.round(absolute);
  const scale = SCALES[system];
  const topIndex = scale.findIndex((step) => rounded >= step.size);
  // Below the smallest unit the figure is already short, and it keeps its paisa.
  if (topIndex < 0) return { sign, parts: [{ value: group(absolute, 2), unit: null }] };

  const top = scale[topIndex];
  let whole = Math.floor(rounded / top.size);
  const remainder = rounded - whole * top.size;
  const stated = (): ShortAmountParts => ({ sign, parts: [{ value: group(whole), unit: top.unit }] });
  if (remainder === 0) return stated();

  const next = scale[topIndex + 1];
  // The remainder gets a unit of its own rather than being dumped out in full. Left raw, a large
  // one undoes the whole point of the card: `Rs 999 Cr 9,787,889` puts the reader straight back to
  // counting digits, which is exactly what the short form exists to spare them.
  if (next && remainder >= next.size) {
    let sub = Math.round(remainder / next.size);
    // A hundred lakh is a crore, not a hundred-lakh: when rounding fills the unit above, carry it
    // rather than printing a figure no reader would ever say out loud.
    if (sub * next.size >= top.size) {
      whole += 1;
      sub = 0;
    }
    if (sub === 0) return stated();
    return { sign, parts: [{ value: group(whole), unit: top.unit }, { value: group(sub), unit: next.unit }] };
  }
  // Too small for a unit of its own, so it is stated exactly — five digits at most, and this is
  // what the recommended `Rs 9 Cr 40,000` is made of.
  return { sign, parts: [{ value: group(whole), unit: top.unit }, { value: group(remainder), unit: null }] };
}
