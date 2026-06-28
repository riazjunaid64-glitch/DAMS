// Input validation + formatting helpers for Pakistani-specific fields.
//
// Standards used:
// - Email:  basic RFC-ish "local@domain.tld" shape.
// - CNIC:   NADRA 13-digit National ID, written as 00000-0000000-0
//           (5 digits region, 7 digits family/serial, 1 check/gender digit).
// - Mobile: Pakistani mobile numbers are 03XX-XXXXXXX — i.e. they start
//           with "03" and are 11 digits long. The international form is
//           +92 3XX XXXXXXX. We accept either and never allow more digits
//           than a valid Pakistani number.

/** Guidance placeholders shown inside the inputs. */
export const PLACEHOLDERS = {
  email: "name@example.com",
  cnic: "42101-1234567-1",
  mobile: "0300-1234567",
} as const;

/* --------------------------------- Email --------------------------------- */

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function isValidEmail(value: string): boolean {
  return EMAIL_RE.test(value.trim());
}

/* ---------------------------------- CNIC --------------------------------- */

/** Matches a 13-digit CNIC in the canonical 00000-0000000-0 form. */
const CNIC_RE = /^\d{5}-\d{7}-\d$/;

export function isValidCnic(value: string): boolean {
  return CNIC_RE.test(value.trim());
}

/**
 * Formats free typing into the CNIC mask 00000-0000000-0 as the user types.
 * Strips non-digits, caps at 13 digits, and inserts the two hyphens.
 */
export function formatCnic(value: string): string {
  const d = value.replace(/\D/g, "").slice(0, 13);
  if (d.length <= 5) return d;
  if (d.length <= 12) return `${d.slice(0, 5)}-${d.slice(5)}`;
  return `${d.slice(0, 5)}-${d.slice(5, 12)}-${d.slice(12)}`;
}

/* --------------------------------- Mobile -------------------------------- */

/** Local 11-digit (03XXXXXXXXX) or international (+923XXXXXXXXX) Pakistani mobile. */
const PK_MOBILE_RE = /^(?:\+92|0)3\d{9}$/;

export function isValidPkMobile(value: string): boolean {
  return PK_MOBILE_RE.test(value.replace(/[\s-]/g, ""));
}

/**
 * Formats typing into a Pakistani mobile number, capping the length so it can
 * never exceed a valid Pakistani number.
 * - +92 form: "+92 3XX XXXXXXX"  (max 10 digits after the country code)
 * - local form: "03XX-XXXXXXX"   (11 digits total)
 */
export function formatPkMobile(value: string): string {
  const trimmed = value.trimStart();
  if (trimmed.startsWith("+")) {
    // Keep "92" + up to 10 subscriber digits.
    let d = trimmed.replace(/\D/g, "");
    if (!d.startsWith("92")) d = `92${d}`;
    d = d.slice(0, 12);
    const sub = d.slice(2);
    if (sub.length <= 3) return `+92 ${sub}`.trimEnd();
    return `+92 ${sub.slice(0, 3)} ${sub.slice(3)}`;
  }
  // Local form: 11 digits, masked as 03XX-XXXXXXX.
  const d = value.replace(/\D/g, "").slice(0, 11);
  if (d.length <= 4) return d;
  return `${d.slice(0, 4)}-${d.slice(4)}`;
}
