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

export function financePeriodLabel(preset: FinancePeriodPreset, startMonth: number, now = new Date()) {
  if (preset !== "year" && preset !== "lastYear") {
    return preset === "today" ? "Today" : preset === "month" ? "This Month" : preset === "all" ? "All" : "Custom";
  }
  const range = buildPeriodRange(preset, startMonth, now);
  const from = new Date(`${range.from}T00:00:00`);
  const to = new Date(`${range.to}T00:00:00`);
  const month = (date: Date) => date.toLocaleString("en-GB", { month: "short" });
  const label = `${month(from)} ${from.getFullYear()} – ${month(to)} ${to.getFullYear()}`;
  return `${preset === "year" ? "This Year" : "Last Year"} (${label})`;
}

function normalizeMonth(month: number) {
  return month >= 1 && month <= 12 ? month : 7;
}

function fmtLocal(date: Date) {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
