import { useId, useState, type ReactNode } from "react";
import { BottomSheet } from "./BottomSheet.tsx";
import { Button } from "./Button.tsx";
import { cx } from "./cx.ts";
import { Dropdown } from "./Dropdown.tsx";
import { IconFilter, IconPlus } from "./icons.tsx";
import { SearchBar } from "./SearchBar.tsx";
import { DateField } from "./TextField.tsx";
import type { Option } from "./types.ts";

export type FilterDef =
  | { type: "select"; key: string; label: string; options: readonly Option[]; icon?: ReactNode; allLabel?: string }
  | { type: "date"; key: string; label: string };

export type FilterValues = Record<string, string>;

export type FilterBarProps = {
  search?: { value: string; onSearch: (value: string) => void; placeholder?: string };
  filters: readonly FilterDef[];
  /** Current value of each filter; "" means All / not set. */
  values: FilterValues;
  /** Changed filters. Desktop sends one at a time; the phone sheet sends everything on Apply. */
  onChange: (changes: FilterValues) => void;
  /** Clears search and filters. */
  onReset: () => void;
  /** Phone only: the navy "+" add button next to the filter button. */
  onAdd?: () => void;
  addLabel?: string;
  className?: string;
};

const withAll = (filter: Extract<FilterDef, { type: "select" }>): Option[] => [
  { value: "", label: filter.allLabel ?? "All" },
  ...filter.options,
];

/**
 * Search + filters for a list. Desktop: one line — search, compact dropdowns, date fields and a
 * Reset link. Phone: search, a filter button (gold dot while any filter is on) that opens a bottom
 * sheet with the same filters, and an optional "+" add button. One definition drives both.
 */
export function FilterBar({ search, filters, values, onChange, onReset, onAdd, addLabel = "Add", className }: FilterBarProps) {
  const [sheetOpen, setSheetOpen] = useState(false);
  const [draft, setDraft] = useState<FilterValues>({});
  const active = filters.some((filter) => values[filter.key]);

  const openSheet = () => {
    setDraft(Object.fromEntries(filters.map((filter) => [filter.key, values[filter.key] ?? ""])));
    setSheetOpen(true);
  };
  const apply = () => {
    const changes = Object.fromEntries(Object.entries(draft).filter(([key, value]) => (values[key] ?? "") !== value));
    if (Object.keys(changes).length) onChange(changes);
    setSheetOpen(false);
  };

  return (
    <div className={cx("font-ui", className)}>
      {/* Desktop */}
      <div className="hidden flex-wrap items-center gap-2.5 md:flex">
        {search && <SearchBar {...search} className="w-full max-w-[280px] flex-1" />}
        {filters.map((filter) =>
          filter.type === "select" ? (
            <Dropdown
              key={filter.key}
              size="filter"
              label={filter.label}
              icon={filter.icon}
              options={withAll(filter)}
              value={values[filter.key] ?? ""}
              onChange={(value) => onChange({ [filter.key]: value })}
              className="w-auto max-w-[240px]"
            />
          ) : (
            <FilterDate key={filter.key} label={filter.label} value={values[filter.key] ?? ""} onChange={(value) => onChange({ [filter.key]: value })} />
          ),
        )}
        <Button variant="link" onClick={onReset} className="ml-1">Reset</Button>
      </div>

      {/* Phone */}
      <div className="flex items-center gap-2 md:hidden">
        {search && <SearchBar {...search} size="lg" className="flex-1" />}
        {filters.length > 0 && (
          <button
            type="button"
            aria-label={active ? "Filters (some applied)" : "Filters"}
            onClick={openSheet}
            className="relative flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-field border border-line-input bg-card text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
          >
            <IconFilter size={18} />
            {active && <span aria-hidden="true" className="absolute top-2 right-2 size-2 rounded-full bg-gold ring-2 ring-card" />}
          </button>
        )}
        {onAdd && <Button iconOnly icon={<IconPlus size={18} />} aria-label={addLabel} onClick={onAdd} />}
      </div>

      <BottomSheet
        open={sheetOpen}
        onClose={() => setSheetOpen(false)}
        title="Filters"
        onApply={apply}
        onReset={() => { onReset(); setSheetOpen(false); }}
      >
        <div className="flex flex-col gap-4">
          {filters.map((filter) =>
            filter.type === "select" ? (
              <Dropdown
                key={filter.key}
                label={filter.label}
                options={withAll(filter)}
                value={draft[filter.key] ?? ""}
                onChange={(value) => setDraft((current) => ({ ...current, [filter.key]: value }))}
              />
            ) : (
              <DateField
                key={filter.key}
                label={filter.label}
                value={draft[filter.key] ?? ""}
                onChange={(event) => setDraft((current) => ({ ...current, [filter.key]: event.target.value }))}
              />
            ),
          )}
        </div>
      </BottomSheet>
    </div>
  );
}

/** Compact date box for the desktop filter bar: grey label, bold date. */
function FilterDate({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  const id = useId();
  return (
    <div className="flex h-10 items-center gap-2 rounded-field border border-line-input bg-card px-3 text-small focus-within:border-primary focus-within:ring-3 focus-within:ring-primary-ring">
      <label htmlFor={id} className="font-bold text-ink-muted">{label}</label>
      <input
        id={id}
        type="date"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="border-0 bg-transparent p-0 font-extrabold text-ink outline-none"
      />
    </div>
  );
}
