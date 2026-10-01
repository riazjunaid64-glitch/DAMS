/**
 * Keeps only what a number field may hold: digits and, when decimals are allowed, one point
 * followed by at most `decimals` digits. The result is the raw value handed to the caller —
 * never rounded, so an amount survives exactly as typed.
 *
 * A leading minus is kept only when `allowNegative` is set, and only one of them: an overdraft
 * opening balance can be typed, and every other field still refuses the sign.
 */
export function cleanNumber(input: string, decimals: number, allowNegative = false): string {
  const negative = allowNegative && input.trimStart().startsWith("-");
  const [whole = "", ...rest] = input.replace(/[^\d.]/g, "").split(".");
  const intPart = whole.replace(/^0+(?=\d)/, "");
  const body = decimals <= 0 || rest.length === 0 ? intPart : `${intPart}.${rest.join("").slice(0, decimals)}`;
  if (!negative) return body;
  return body ? `-${body}` : "-";
}

/** "1000000.5" → "1,000,000.5". Leaves the fraction as typed. A leading minus stays in front. */
export function groupThousands(raw: string): string {
  if (!raw || raw === "-") return raw;
  const negative = raw.startsWith("-");
  const value = negative ? raw.slice(1) : raw;
  if (!value) return negative ? "-" : "";
  const [intPart = "", fraction] = value.split(".");
  const grouped = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  const body = fraction === undefined ? grouped : `${grouped}.${fraction}`;
  return negative ? `-${body}` : body;
}

/**
 * Where the caret belongs in `formatted` so that `digitsBefore` digits, points and a leading
 * minus sit before it. Commas the grouping added are skipped.
 */
export function caretAfter(formatted: string, digitsBefore: number): number {
  if (digitsBefore <= 0) return 0;
  let seen = 0;
  for (let i = 0; i < formatted.length; i += 1) {
    if (/[\d.-]/.test(formatted[i]!)) seen += 1;
    if (seen === digitsBefore) return i + 1;
  }
  return formatted.length;
}
