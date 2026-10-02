/**
 * Why a From/To range is not usable yet, or null when it is.
 *
 * One end alone used to be accepted by the finance cards and quietly re-read as "all time" by the
 * chart, and a backwards range was quietly re-read as the financial year — so the screen answered
 * two different questions at once and said nothing about it. A date outside what the database can
 * store reached the server and came back as a 500. All three are refused here, and again in the
 * controller. Filter bars and the dashboard share this one check so they cannot disagree.
 */
/** SQL Server's `datetime` floor, and the server's own lower bound. */
const MIN_FILTER_DATE = "1753-01-01";
/** One day short of the maximum representable date, because every query compares against To + 1 day. */
const MAX_FILTER_DATE = "9999-12-30";

export function financeRangeError(from: string, to: string): string | null {
  if (!from && !to) return null;
  if (!from || !to) return "Enter both a From and a To date, or clear them both.";
  for (const [value, label] of [[from, "From date"], [to, "To date"]] as const) {
    if (value < MIN_FILTER_DATE) return `${label} cannot be before 01 Jan 1753.`;
    if (value > MAX_FILTER_DATE) return `${label} cannot be after 30 Dec 9999.`;
  }
  if (from > to) return "From date cannot be after To date.";
  return null;
}

/** Presets a Period filter can show. The page supplies the dates each one covers. */
export type PeriodPreset = "today" | "month" | "lastMonth" | "year" | "lastYear" | "custom" | "all";

export type PeriodRange = { from: string; to: string };

/** Whether the configured financial year can fill the year presets yet. */
export type FinancialYearStatus = "loading" | "error" | "ready";

const YEAR_PRESETS = ["year", "lastYear"] as const;

export function financialYearHint(status: FinancialYearStatus): string | null {
  if (status === "loading") return "Loading the configured financial year…";
  if (status === "error") return "The financial year setting could not be loaded, so this range cannot be applied.";
  return null;
}

function monthAndYear(iso: string): string {
  const date = new Date(`${iso}T00:00:00`);
  if (Number.isNaN(date.getTime())) return iso;
  return `${date.toLocaleString("en-GB", { month: "short" })} ${date.getFullYear()}`;
}

function yearLabel(name: string, range: PeriodRange, ready: boolean): string {
  if (!ready || !range.from || !range.to) return name;
  return `${name} (${monthAndYear(range.from)} – ${monthAndYear(range.to)})`;
}

export type PeriodOption = { value: PeriodPreset; label: string; disabled?: boolean };

/**
 * The Period choices. Year options stay disabled until the page's ranges are usable, and their
 * labels state the months those ranges cover ("This financial year (Jul 2026 – Jun 2027)").
 */
export function periodSelectOptions(
  rangeFor: (preset: Exclude<PeriodPreset, "custom">) => PeriodRange,
  status: FinancialYearStatus,
  /** Offer only these presets (plus Custom). Omitted: every preset. */
  presets?: readonly Exclude<PeriodPreset, "custom">[],
  /** Replaces the word "Custom". */
  customLabel = "Custom",
  /** Year options also state their months ("This financial year (Jul 2026 – Jun 2027)"). */
  withMonths = true,
): PeriodOption[] {
  const yearReady = status === "ready";
  const year = rangeFor("year");
  const lastYear = rangeFor("lastYear");
  const all: PeriodOption[] = [
    { value: "all", label: "All time" },
    { value: "today", label: "Today" },
    { value: "month", label: "This month" },
    { value: "lastMonth", label: "Last month" },
    { value: "year", label: yearLabel("This financial year", year, yearReady && withMonths), disabled: !yearReady || !year.from || !year.to },
    { value: "lastYear", label: yearLabel("Last financial year", lastYear, yearReady && withMonths), disabled: !yearReady || !lastYear.from || !lastYear.to },
    { value: "custom", label: customLabel },
  ];
  return presets ? all.filter((option) => option.value === "custom" || presets.includes(option.value as Exclude<PeriodPreset, "custom">)) : all;
}

/**
 * Which preset the current dates are. Both empty is All time; a pair that matches a supplied
 * range is that preset; anything else is Custom. An empty year range (the start month is not
 * known yet) never matches, so it cannot be mistaken for All time.
 */
export function periodPresetForDates(
  from: string,
  to: string,
  rangeFor: (preset: Exclude<PeriodPreset, "custom">) => PeriodRange,
): PeriodPreset {
  if (!from && !to) return "all";
  for (const preset of ["today", "month", "lastMonth", ...YEAR_PRESETS] as const) {
    const range = rangeFor(preset);
    if (range.from && range.to && range.from === from && range.to === to) return preset;
  }
  return "custom";
}

/** Dates a preset writes. Custom changes nothing. A year preset with no range writes nothing. */
export function periodRangeFor(
  preset: PeriodPreset,
  rangeFor: (preset: Exclude<PeriodPreset, "custom">) => PeriodRange,
): PeriodRange | null {
  if (preset === "custom") return null;
  const range = rangeFor(preset);
  if ((preset === "year" || preset === "lastYear") && (!range.from || !range.to)) return null;
  return { from: range.from, to: range.to };
}
