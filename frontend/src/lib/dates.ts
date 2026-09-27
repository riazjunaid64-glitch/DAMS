/**
 * Server timestamps as staff read them. DAMS is used in Pakistan, so every value is shown on the
 * Karachi clock whatever time zone the browser happens to be set to.
 */

const TIME_ZONE = "Asia/Karachi";

const format = (options: Intl.DateTimeFormatOptions) =>
  new Intl.DateTimeFormat("en-US", { timeZone: TIME_ZONE, ...options });

const timeFormat = format({ hour: "numeric", minute: "2-digit" });
const monthDayFormat = format({ month: "short", day: "numeric" });
const dayFormat = format({ month: "short", day: "numeric", year: "numeric" });
const calendarFormat = format({ year: "numeric", month: "numeric", day: "numeric" });

const MS_PER_DAY = 86_400_000;

/**
 * SQL Server stores these without a timezone, so they reach the browser as
 * `2026-08-29T14:23:11` with no `Z`. Parsing that as local time would shift a UTC instant by
 * the browser's offset — five hours in Pakistan — which is enough to show a live invitation
 * as expired. A value that already carries an offset is left exactly as it is.
 */
export function parseServerDateTime(value?: string | null): Date | null {
  if (!value) return null;
  const trimmed = value.trim();
  const hasZone = /(z|[+-]\d{2}:?\d{2})$/i.test(trimmed);
  const parsed = new Date(hasZone ? trimmed : `${trimmed}Z`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/** Whether a server time has already passed. A missing or unreadable time never has. */
export const isPastServerTime = (value?: string | null, now: Date = new Date()) => {
  const date = parseServerDateTime(value);
  return date !== null && date < now;
};

/** Intl may put a narrow no-break space before AM/PM; a plain space keeps UI and tests stable. */
const clean = (text: string) => text.replace(/\u202f/g, " ");

/** The Karachi calendar day of an instant, as a whole day count, plus its year. */
function karachiDay(date: Date) {
  const parts = Object.fromEntries(
    calendarFormat.formatToParts(date).map((part) => [part.type, Number(part.value)]),
  );
  return { year: parts.year, day: Date.UTC(parts.year, parts.month - 1, parts.day) / MS_PER_DAY };
}

const RELATIVE_DAYS: Record<number, string> = { [-1]: "Yesterday", 0: "Today", 1: "Tomorrow" };

/** "Today, 11:45 AM" · "Tomorrow, 4:00 PM" · "Sep 25, 3:02 PM" · "Sep 25, 2025, 3:02 PM". */
export function formatWhen(value?: string | null, now: Date = new Date()): string {
  const date = parseServerDateTime(value);
  if (!date) return "—";
  const relative = RELATIVE_DAYS[karachiDay(date).day - karachiDay(now).day];
  return `${relative ?? formatMonthDay(value, now)}, ${clean(timeFormat.format(date))}`;
}

/** "Sep 27, 2026". */
export function formatDay(value?: string | null): string {
  const date = parseServerDateTime(value);
  return date ? dayFormat.format(date) : "—";
}

/** "Sep 27" within the current Karachi year, "Sep 27, 2025" outside it. */
export function formatMonthDay(value?: string | null, now: Date = new Date()): string {
  const date = parseServerDateTime(value);
  if (!date) return "—";
  return karachiDay(date).year === karachiDay(now).year ? monthDayFormat.format(date) : dayFormat.format(date);
}

/** "10:20 AM". */
export function formatTime(value?: string | null): string {
  const date = parseServerDateTime(value);
  return date ? clean(timeFormat.format(date)) : "—";
}
