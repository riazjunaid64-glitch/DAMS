/*
 * Pure date and time helpers behind DatePicker and TimePicker. Dates are "YYYY-MM-DD" and times
 * "HH:mm" (24 hour) everywhere in state and on the wire; only the screen shows "Sep 29, 2026" and
 * "11:30 AM". Calendar days are plain calendar arithmetic in UTC, so the browser's time zone and
 * daylight saving never shift a day.
 */

export const MONTHS_SHORT = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"] as const;
export const MONTHS_LONG = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"] as const;
/** The week starts on Monday. */
export const WEEKDAYS = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"] as const;

export type YearMonth = { year: number; month: number };

const pad = (n: number, width = 2) => String(n).padStart(width, "0");

export function toIso(year: number, month: number, day: number): string {
  return `${pad(year, 4)}-${pad(month + 1)}-${pad(day)}`;
}

/** Year, zero-based month and day of a "YYYY-MM-DD" value, or null when it is not a real date. */
export function parseIso(value: string | undefined | null): { year: number; month: number; day: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value ?? "");
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]) - 1;
  const day = Number(match[3]);
  const check = new Date(Date.UTC(year, month, day));
  return check.getUTCFullYear() === year && check.getUTCMonth() === month && check.getUTCDate() === day ? { year, month, day } : null;
}

/** "Sep 29, 2026" for a date value; empty when the value is empty or invalid. */
export function formatPickerDate(value: string | undefined | null): string {
  const parts = parseIso(value);
  return parts ? `${MONTHS_SHORT[parts.month]} ${parts.day}, ${parts.year}` : "";
}

export function addDays(value: string, days: number): string {
  const parts = parseIso(value);
  if (!parts) return value;
  const moved = new Date(Date.UTC(parts.year, parts.month, parts.day + days));
  return toIso(moved.getUTCFullYear(), moved.getUTCMonth(), moved.getUTCDate());
}

/** The same day-of-month `months` later, held to the last day when the target month is shorter. */
export function addMonths(value: string, months: number): string {
  const parts = parseIso(value);
  if (!parts) return value;
  const target = new Date(Date.UTC(parts.year, parts.month + months, 1));
  const last = new Date(Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0)).getUTCDate();
  return toIso(target.getUTCFullYear(), target.getUTCMonth(), Math.min(parts.day, last));
}

export type CalendarCell = { iso: string; day: number; inMonth: boolean };

/** Six Monday-first weeks (42 days) that cover the month, with the neighbouring months' days around it. */
export function monthGrid({ year, month }: YearMonth): CalendarCell[] {
  const first = new Date(Date.UTC(year, month, 1));
  const lead = (first.getUTCDay() + 6) % 7;
  return Array.from({ length: 42 }, (_, index) => {
    const day = new Date(Date.UTC(year, month, 1 - lead + index));
    return {
      iso: toIso(day.getUTCFullYear(), day.getUTCMonth(), day.getUTCDate()),
      day: day.getUTCDate(),
      inMonth: day.getUTCMonth() === month,
    };
  });
}

/** True when `value` falls outside the optional `min` / `max` (both inclusive). */
export function isOutOfRange(value: string, min?: string, max?: string): boolean {
  return (!!min && value < min) || (!!max && value > max);
}

/** Moves a view by whole months, carrying into the year. */
export function shiftMonth({ year, month }: YearMonth, months: number): YearMonth {
  const total = year * 12 + month + months;
  return { year: Math.floor(total / 12), month: ((total % 12) + 12) % 12 };
}

/** True when no day of the month can be picked. */
export function monthOutOfRange({ year, month }: YearMonth, min?: string, max?: string): boolean {
  const last = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
  return isOutOfRange(toIso(year, month, last), min, undefined) || isOutOfRange(toIso(year, month, 1), undefined, max);
}

/** "11:30 AM" for a "HH:mm" value; empty when the value is empty or invalid. */
export function formatPickerTime(value: string | undefined | null): string {
  const match = /^(\d{2}):(\d{2})/.exec(value ?? "");
  if (!match) return "";
  const hour = Number(match[1]);
  const minute = Number(match[2]);
  if (hour > 23 || minute > 59) return "";
  return `${hour % 12 === 0 ? 12 : hour % 12}:${pad(minute)} ${hour < 12 ? "AM" : "PM"}`;
}

/**
 * Reads what a person types into the time box ("11:15 AM", "9pm", "2:05 p.m.", "14:30") as
 * "HH:mm". Null when it is not a time. A bare number is an hour.
 */
export function parseTypedTime(text: string): string | null {
  const match = /^(\d{1,2})(?::?(\d{2}))?\s*([ap])?\.?m?\.?$/i.exec(text.trim());
  if (!match) return null;
  let hour = Number(match[1]);
  const minute = match[2] === undefined ? 0 : Number(match[2]);
  const meridiem = match[3]?.toLowerCase();
  if (minute > 59) return null;
  if (meridiem) {
    if (hour < 1 || hour > 12) return null;
    hour = (hour % 12) + (meridiem === "p" ? 12 : 0);
  } else if (hour > 23) {
    return null;
  }
  return `${pad(hour)}:${pad(minute)}`;
}

/** Every 30 minutes of the day, "00:00" to "23:30". */
export const HALF_HOURS: readonly string[] = Array.from({ length: 48 }, (_, index) => `${pad(Math.floor(index / 2))}:${index % 2 ? "30" : "00"}`);

export type ClockParts = { hour12: number; minute: number; pm: boolean };

export function toClockParts(value: string | undefined | null): ClockParts | null {
  const match = /^(\d{2}):(\d{2})/.exec(value ?? "");
  if (!match) return null;
  const hour = Number(match[1]);
  const minute = Number(match[2]);
  if (hour > 23 || minute > 59) return null;
  return { hour12: hour % 12 === 0 ? 12 : hour % 12, minute, pm: hour >= 12 };
}

export function fromClockParts({ hour12, minute, pm }: ClockParts): string {
  return `${pad((hour12 % 12) + (pm ? 12 : 0))}:${pad(minute)}`;
}
