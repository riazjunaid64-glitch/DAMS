import { monthStartIso, todayIso, type DateRange, type RangePreset } from "./dateRange";
import { DatePicker } from "../ui";

function presetRange(preset: RangePreset, current: DateRange): DateRange {
  if (preset === "today") {
    const t = todayIso();
    return { preset, from: t, to: t };
  }
  if (preset === "month") {
    return { preset, from: monthStartIso(), to: todayIso() };
  }
  // custom: keep whatever dates were last in effect
  return { preset, from: current.from, to: current.to };
}

const PRESETS: { id: RangePreset; label: string }[] = [
  { id: "today", label: "Today" },
  { id: "month", label: "This Month" },
  { id: "custom", label: "Custom" },
];

interface Props {
  value: DateRange;
  onChange: (next: DateRange) => void;
}

export default function DateRangeFilter({ value, onChange }: Props) {
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="inline-flex rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-1">
        {PRESETS.map(p => (
          <button
            key={p.id}
            type="button"
            onClick={() => onChange(presetRange(p.id, value))}
            className={`rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors ${
              value.preset === p.id
                ? "bg-[var(--accent)] text-[#1c1810] shadow-sm"
                : "text-[var(--text-muted)] hover:text-[var(--text-primary)]"
            }`}
          >
            {p.label}
          </button>
        ))}
      </div>

      {value.preset === "custom" && (
        <div className="flex flex-wrap items-end gap-3">
          <DatePicker label="From" required value={value.from} max={value.to || undefined} onChange={from => onChange({ ...value, from })} className="w-44" />
          <DatePicker label="To" required value={value.to} min={value.from || undefined} onChange={to => onChange({ ...value, to })} className="w-44" />
        </div>
      )}
    </div>
  );
}
