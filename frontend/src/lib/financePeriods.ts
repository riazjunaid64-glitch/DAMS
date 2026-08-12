export type FinancePeriodPreset = "today" | "month" | "year" | "all" | "custom";

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
    case "all":
    case "custom":
      return { from: "", to: "" };
    default:
      return { from: "", to: "" };
  }
}

function normalizeMonth(month: number) {
  return month >= 1 && month <= 12 ? month : 7;
}

function fmtLocal(date: Date) {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
