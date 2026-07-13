export type RangePreset = "today" | "month" | "custom";

export interface DateRange {
  preset: RangePreset;
  from: string;
  to: string;
}

function iso(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

export function todayIso(): string {
  return iso(new Date());
}

export function monthStartIso(): string {
  const d = new Date();
  return iso(new Date(d.getFullYear(), d.getMonth(), 1));
}

export function defaultRange(): DateRange {
  return { preset: "month", from: monthStartIso(), to: todayIso() };
}
