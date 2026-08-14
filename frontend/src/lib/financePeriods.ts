export type FinancePeriodPreset = "today" | "month" | "year" | "lastYear" | "all" | "custom";

export function financialYearWindow(date: Date, startMonth: number) {
  const month = normalizeMonth(startMonth);
  const year = date.getMonth() + 1 >= month ? date.getFullYear() : date.getFullYear() - 1;
  const from = new Date(year, month - 1, 1, 0, 0, 0, 0);
  return {
    from,
    toExclusive: new Date(from.getFullYear() + 1, from.getMonth(), 1, 0, 0, 0, 0),
  };
}

export function buildPeriodRange(preset: FinancePeriodPreset, startMonth: number, now = new Date()) {
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
      const { from, toExclusive } = financialYearWindow(now, startMonth);
      const to = new Date(toExclusive.getFullYear(), toExclusive.getMonth(), 0);
      return { from: fmtLocal(from), to: fmtLocal(to) };
    }
    case "lastYear": {
      const { from, toExclusive } = financialYearWindow(now, startMonth);
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
 * Every label is derived from buildPeriodRange, so what a chip says and what it filters on cannot
 * drift apart.
 */
export function financePeriodLabel(preset: FinancePeriodPreset, startMonth: number, now = new Date()) {
  const name = PRESET_NAMES[preset] ?? PRESET_NAMES.custom;
  if (preset !== "month" && preset !== "year" && preset !== "lastYear") return name;

  const range = buildPeriodRange(preset, startMonth, now);
  const from = parseLocal(range.from);
  // A month sits inside one named month, so naming it twice would only add noise.
  if (preset === "month") return `${name} (${monthAndYear(from)})`;
  return `${name} (${monthAndYear(from)} – ${monthAndYear(parseLocal(range.to))})`;
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
