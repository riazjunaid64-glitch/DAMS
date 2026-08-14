export type FinancePeriodPreset = "today" | "month" | "year" | "lastYear" | "all" | "custom";

/**
 * Today's date in Pakistan, as a `yyyy-mm-dd` value for a date input.
 *
 * Every finance date is judged against PKT on the server — a transfer or purchase dated after
 * PakistanTime.Today is rejected outright. Two other readings both get this wrong. `toISOString()`
 * is UTC, so between midnight and 05:00 in Pakistan it offers yesterday, and the entry is filed
 * into the wrong day, or the wrong month, with nothing to flag it. The browser's own local date is
 * wrong the other way for anyone east of PKT: it offers a day the server refuses as being in the
 * future. Shifting the instant by PKT's fixed +05:00 and reading the UTC date gives the Pakistani
 * calendar date on any machine, wherever it is set.
 */
export const pakistanToday = (): string =>
  new Date(Date.now() + 5 * 60 * 60 * 1000).toISOString().slice(0, 10);

export function financialYearWindow(date: Date, startMonth: number) {
  const month = normalizeMonth(startMonth);
  const year = date.getMonth() + 1 >= month ? date.getFullYear() : date.getFullYear() - 1;
  const from = new Date(year, month - 1, 1, 0, 0, 0, 0);
  return {
    from,
    toExclusive: new Date(from.getFullYear() + 1, from.getMonth(), 1, 0, 0, 0, 0),
  };
}

/**
 * The dates a preset covers. `startMonth` may be null while the configured financial year start is
 * still being read; the two presets that depend on it then resolve to no range at all rather than
 * to a July one, so a caller cannot accidentally filter on a financial year the client never set.
 */
export function buildPeriodRange(
  preset: FinancePeriodPreset,
  startMonth: number | null,
  now = parseLocal(pakistanToday()),
) {
  if ((preset === "year" || preset === "lastYear") && startMonth === null) {
    return { from: "", to: "" };
  }
  switch (preset) {
    case "today": {
      const value = fmtLocal(now);
      return { from: value, to: value };
    }
    case "month": {
      const from = new Date(now.getFullYear(), now.getMonth(), 1);
      const to = new Date(now.getFullYear(), now.getMonth() + 1, 0);
      return { from: fmtLocal(from), to: fmtLocal(to) };
    }
    case "year": {
      const { from, toExclusive } = financialYearWindow(now, startMonth ?? 7);
      const to = new Date(toExclusive.getFullYear(), toExclusive.getMonth(), 0);
      return { from: fmtLocal(from), to: fmtLocal(to) };
    }
    case "lastYear": {
      const { from, toExclusive } = financialYearWindow(now, startMonth ?? 7);
      const priorFrom = new Date(from.getFullYear() - 1, from.getMonth(), 1);
      const priorTo = new Date(toExclusive.getFullYear() - 1, toExclusive.getMonth(), 0);
      return { from: fmtLocal(priorFrom), to: fmtLocal(priorTo) };
    }
    case "all":
    case "custom":
      return { from: "", to: "" };
    default:
      return { from: "", to: "" };
  }
}

const PRESET_NAMES: Record<FinancePeriodPreset, string> = {
  today: "Today",
  month: "This Month",
  year: "This Year",
  lastYear: "Last Year",
  all: "All",
  custom: "Custom",
};

/**
 * A preset's name together with the dates it actually covers, so a filter never leaves the reader
 * guessing which months a figure is for.
 *
 * The year presets are the reason this exists: they follow the configured financial year rather
 * than the calendar, so with a July start "This Year" is neither 2026 nor obvious from the name.
 * Today, All and Custom are named only — Today already states its own range, and the other two
 * have no fixed range to state.
 *
 * Pass `null` for `startMonth` when the configured start month has not been read back yet. The
 * year presets are then named without a range, because a stated range is a promise: an admin who
 * reads "Jul 2026 – Jun 2027" will trust the figure beside it, and on a client whose year starts
 * in January that promise would be false. Saying less is recoverable; saying something wrong about
 * which months a number covers is not.
 *
 * Every label is derived from buildPeriodRange, so what a chip says and what it filters on cannot
 * drift apart.
 */
export function financePeriodLabel(
  preset: FinancePeriodPreset,
  startMonth: number | null,
  now = parseLocal(pakistanToday()),
) {
  const name = PRESET_NAMES[preset] ?? PRESET_NAMES.custom;

  // A calendar month is the same month wherever the financial year starts, so this one can always
  // be stated. The argument is accepted and ignored.
  if (preset === "month") {
    return `${name} (${monthAndYear(parseLocal(buildPeriodRange(preset, 1, now).from))})`;
  }
  if (preset !== "year" && preset !== "lastYear") return name;
  if (startMonth === null) return name;

  const range = buildPeriodRange(preset, startMonth, now);
  return `${name} (${monthAndYear(parseLocal(range.from))} – ${monthAndYear(parseLocal(range.to))})`;
}

function monthAndYear(date: Date) {
  return `${date.toLocaleString("en-GB", { month: "short" })} ${date.getFullYear()}`;
}

function parseLocal(value: string) {
  return new Date(`${value}T00:00:00`);
}

function normalizeMonth(month: number) {
  return month >= 1 && month <= 12 ? month : 7;
}

function fmtLocal(date: Date) {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
