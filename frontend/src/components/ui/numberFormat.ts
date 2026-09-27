/**
 * Keeps only what a number field may hold: digits and, when decimals are allowed, one point
 * followed by at most `decimals` digits. The result is the raw value handed to the caller —
 * never rounded, so an amount survives exactly as typed.
 */
export function cleanNumber(input: string, decimals: number): string {
  const [whole = "", ...rest] = input.replace(/[^\d.]/g, "").split(".");
  const intPart = whole.replace(/^0+(?=\d)/, "");
  if (decimals <= 0 || rest.length === 0) return intPart;
  return `${intPart}.${rest.join("").slice(0, decimals)}`;
}

/** "1000000.5" → "1,000,000.5". Leaves the fraction as typed. */
export function groupThousands(raw: string): string {
  if (!raw) return "";
  const [intPart = "", fraction] = raw.split(".");
  const grouped = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  return fraction === undefined ? grouped : `${grouped}.${fraction}`;
}

/** Where the caret belongs in `formatted` so that `digitsBefore` digits / points sit before it. */
export function caretAfter(formatted: string, digitsBefore: number): number {
  if (digitsBefore <= 0) return 0;
  let seen = 0;
  for (let i = 0; i < formatted.length; i += 1) {
    if (/[\d.]/.test(formatted[i]!)) seen += 1;
    if (seen === digitsBefore) return i + 1;
  }
  return formatted.length;
}
