/** "0333 4412987" for a Pakistani mobile however it was stored ("03334412987", "0333-4412987", "+92 333 4412987"). Anything else is shown as it is. */
export function formatPhone(phone: string | null | undefined): string {
  const raw = (phone ?? "").trim();
  if (!raw) return "";
  let digits = raw.replace(/\D/g, "");
  if (digits.startsWith("0092")) digits = `0${digits.slice(4)}`;
  else if (digits.startsWith("92") && digits.length === 12) digits = `0${digits.slice(2)}`;
  return /^03\d{9}$/.test(digits) ? `${digits.slice(0, 4)} ${digits.slice(4)}` : raw;
}
