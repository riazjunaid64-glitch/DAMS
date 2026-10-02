import { useEffect, useState, type ReactNode } from "react";
import { BottomSheet } from "./BottomSheet.tsx";
import { Button } from "./Button.tsx";
import { cx } from "./cx.ts";
import {
  financeRangeError,
  financialYearHint,
  periodPresetForDates,
  periodRangeFor,
  periodSelectOptions,
  type FinancialYearStatus,
  type PeriodPreset,
  type PeriodRange,
} from "./dateRange.ts";
import { Dropdown } from "./Dropdown.tsx";
import { IconFilter, IconPlus } from "./icons.tsx";
import { SearchBar } from "./SearchBar.tsx";
import { DatePicker } from "./DatePicker.tsx";
import { activeFilterCount, rangeId } from "./filterCount.ts";
import type { Option } from "./types.ts";

export type { FinancialYearStatus, PeriodPreset, PeriodRange };

type FilterBase = {
  /**
   * Shows the filter only while this is true. It reads the live values on desktop and the sheet's
   * unapplied choices on a phone, so a filter can follow another ("Show: Date range" brings From
   * and To). A hidden filter is not counted and its range is not checked.
   */
  when?: (values: FilterValues) => boolean;
};

export type FilterDef = FilterBase & (
  | { type: "select"; key: string; label: string; options: readonly Option[]; icon?: ReactNode; allLabel?: string }
  | { type: "date"; key: string; label: string; max?: string; required?: boolean }
  | {
      type: "dateRange";
      fromKey: string;
      toKey: string;
      fromLabel?: string;
      toLabel?: string;
      /** Passed to both date pickers. A max of today blocks future dates. */
      max?: string;
      /** Passed to both date pickers. Required fields have no Clear button. */
      required?: boolean;
    }
  | {
      type: "period";
      key: string;
      label?: string;
      /** The date range this period fills. Counted once with that range, not as a second filter. */
      fromKey: string;
      toKey: string;
      /**
       * Ranges the page supplies (see `buildPeriodRange`). "all" clears both dates. Year presets
       * return an empty range until the financial year start is known.
       */
      rangeFor: (preset: Exclude<PeriodPreset, "custom">) => PeriodRange;
      /** Year options stay disabled, with a hint, until this is "ready". */
      financialYear?: FinancialYearStatus;
      /** Offer only these presets, plus Custom. Dates that match none of them read as Custom. */
      presets?: readonly Exclude<PeriodPreset, "custom">[];
      /** Replaces the word "Custom" ("Custom dates"). */
      customLabel?: string;
      /** Set to false to name the year options without their months, when the dates are on screen anyway. */
      monthsInLabel?: boolean;
    }
);

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
  /**
   * What a page opens on, for filters that start with a value (a financial year, today). The Reset
   * link and the phone badge count a filter only when it differs from this. Omitted: "" everywhere.
   */
  defaults?: FilterValues;
  /** The message under a From/To pair that cannot be applied, or null once it can. */
  onRangeError?: (message: string | null) => void;
  /** Phone only: the navy "+" add button next to the filter button. */
  onAdd?: () => void;
  addLabel?: string;
  className?: string;
};

const visibleIn = (filters: readonly FilterDef[], values: FilterValues) => filters.filter((filter) => !filter.when || filter.when(values));

const withAll = (filter: Extract<FilterDef, { type: "select" }>): Option[] => [
  { value: "", label: filter.allLabel ?? "All" },
  ...filter.options,
];

type RangeDraft = { from: string; to: string };

const controlClass = "min-w-0 max-w-[240px] flex-1 basis-0";

/**
 * Search + filters for a list. Desktop: one line — search, compact dropdowns, date fields and a
 * Reset link (only once a filter or the search has a value). Phone: search, a filter button (gold
 * dot while any filter is on) that opens a bottom sheet with the same filters, and an optional
 * "+" add button. One definition drives both.
 *
 * A `dateRange` is one filter: both dates or neither, applied only once the pair is valid. A
 * `period` select can own that range.
 */
export function FilterBar({ search, filters, values, onChange, onReset, defaults, onRangeError, onAdd, addLabel = "Add", className }: FilterBarProps) {
  const [sheetOpen, setSheetOpen] = useState(false);
  const [draft, setDraft] = useState<FilterValues>({});
  const [rangeDrafts, setRangeDrafts] = useState<Record<string, RangeDraft>>({});
  const [sheetError, setSheetError] = useState<string | null>(null);
  const rangeSignature = filters
    .filter((filter): filter is Extract<FilterDef, { type: "dateRange" | "period" }> => filter.type === "dateRange" || filter.type === "period")
    .map((filter) => `${filter.fromKey}=${values[filter.fromKey] ?? ""};${filter.toKey}=${values[filter.toKey] ?? ""}`)
    .join("|");
  const [seenSignature, setSeenSignature] = useState(rangeSignature);
  if (seenSignature !== rangeSignature) {
    setSeenSignature(rangeSignature);
    setRangeDrafts((current) => {
      let changed = false;
      const next = { ...current };
      for (const filter of filters) {
        if (filter.type !== "dateRange" && filter.type !== "period") continue;
        const id = rangeId(filter.fromKey, filter.toKey);
        const pending = next[id];
        if (!pending) continue;
        const from = values[filter.fromKey] ?? "";
        const to = values[filter.toKey] ?? "";
        if ((pending.from === from && pending.to === to) || financeRangeError(from, to) === null) {
          delete next[id];
          changed = true;
        }
      }
      return changed ? next : current;
    });
  }
  const activeCount = activeFilterCount(filters, values, defaults);
  const active = activeCount > 0;
  const showReset = active || Boolean(search?.value);

  const shownRange = (fromKey: string, toKey: string, source: FilterValues): RangeDraft => {
    const draftRange = rangeDrafts[rangeId(fromKey, toKey)];
    if (draftRange) return draftRange;
    return { from: source[fromKey] ?? "", to: source[toKey] ?? "" };
  };

  const desktopFilters = visibleIn(filters, values);
  const desktopRangeError = (() => {
    for (const filter of desktopFilters) {
      if (filter.type !== "dateRange") continue;
      const shown = shownRange(filter.fromKey, filter.toKey, values);
      const message = financeRangeError(shown.from, shown.to);
      if (message) return message;
    }
    return null;
  })();

  useEffect(() => {
    onRangeError?.(desktopRangeError);
  }, [desktopRangeError, onRangeError]);

  const yearHint = (() => {
    for (const filter of filters) {
      if (filter.type !== "period") continue;
      const hint = financialYearHint(filter.financialYear ?? "ready");
      if (hint && filter.financialYear && filter.financialYear !== "ready") return hint;
    }
    return null;
  })();

  const commitRange = (fromKey: string, toKey: string, next: RangeDraft) => {
    const id = rangeId(fromKey, toKey);
    const message = financeRangeError(next.from, next.to);
    setRangeDrafts((current) => ({ ...current, [id]: next }));
    if (message) return;
    const appliedFrom = values[fromKey] ?? "";
    const appliedTo = values[toKey] ?? "";
    if (next.from === appliedFrom && next.to === appliedTo) {
      setRangeDrafts((current) => {
        if (!current[id]) return current;
        const rest = { ...current };
        delete rest[id];
        return rest;
      });
      return;
    }
    onChange({ [fromKey]: next.from, [toKey]: next.to });
  };

  const applyPeriod = (filter: Extract<FilterDef, { type: "period" }>, preset: string) => {
    const range = periodRangeFor(preset as PeriodPreset, filter.rangeFor);
    if (!range) return;
    setRangeDrafts((current) => ({ ...current, [rangeId(filter.fromKey, filter.toKey)]: range }));
    if ((values[filter.fromKey] ?? "") === range.from && (values[filter.toKey] ?? "") === range.to) return;
    onChange({ [filter.fromKey]: range.from, [filter.toKey]: range.to });
  };

  const openSheet = () => {
    const next: FilterValues = {};
    for (const filter of filters) {
      if (filter.type === "dateRange" || filter.type === "period") {
        next[filter.fromKey] = values[filter.fromKey] ?? "";
        next[filter.toKey] = values[filter.toKey] ?? "";
      } else {
        next[filter.key] = values[filter.key] ?? "";
      }
    }
    setDraft(next);
    setSheetError(null);
    setSheetOpen(true);
  };

  const apply = () => {
    for (const filter of visibleIn(filters, draft)) {
      if (filter.type !== "dateRange") continue;
      const message = financeRangeError(draft[filter.fromKey] ?? "", draft[filter.toKey] ?? "");
      if (message) {
        setSheetError(message);
        return;
      }
    }
    const changes: FilterValues = {};
    const seen = new Set<string>();
    for (const filter of filters) {
      if (filter.type === "dateRange" || filter.type === "period") {
        for (const key of [filter.fromKey, filter.toKey]) {
          if (seen.has(key)) continue;
          seen.add(key);
          const value = draft[key] ?? "";
          if ((values[key] ?? "") !== value) changes[key] = value;
        }
      } else if ((values[filter.key] ?? "") !== (draft[filter.key] ?? "")) {
        changes[filter.key] = draft[filter.key] ?? "";
      }
    }
    if (Object.keys(changes).length) onChange(changes);
    setSheetOpen(false);
  };

  const sheetFilters = visibleIn(filters, draft);
  const sheetRangeError = (() => {
    if (!sheetOpen) return null;
    for (const filter of sheetFilters) {
      if (filter.type !== "dateRange") continue;
      const message = financeRangeError(draft[filter.fromKey] ?? "", draft[filter.toKey] ?? "");
      if (message) return message;
    }
    return null;
  })();

  const renderPeriod = (filter: Extract<FilterDef, { type: "period" }>, source: FilterValues, onPreset: (preset: string) => void, size: "filter" | "form") => {
    const status = filter.financialYear ?? "ready";
    const options = periodSelectOptions(filter.rangeFor, status, filter.presets, filter.customLabel, filter.monthsInLabel);
    const matched = periodPresetForDates(source[filter.fromKey] ?? "", source[filter.toKey] ?? "", filter.rangeFor);
    const preset = options.some((option) => option.value === matched) ? matched : "custom";
    return (
      <Dropdown
        key={filter.key}
        size={size}
        label={filter.label ?? "Period"}
        options={options}
        value={preset}
        onChange={onPreset}
        className={size === "filter" ? controlClass : undefined}
      />
    );
  };

  return (
    <div className={cx("font-ui", className)}>
      {/* Desktop. Filters shrink so five of them and Reset stay on one line at 1440. */}
      <div className="hidden min-w-0 flex-nowrap items-center gap-2 md:flex">
        {search && <SearchBar {...search} className="min-w-0 max-w-[340px] flex-1 basis-0" />}
        {desktopFilters.map((filter) => {
          if (filter.type === "select") {
            return (
              <Dropdown
                key={filter.key}
                size="filter"
                label={filter.label}
                icon={filter.icon}
                options={withAll(filter)}
                value={values[filter.key] ?? ""}
                onChange={(value) => onChange({ [filter.key]: value })}
                className={controlClass}
              />
            );
          }
          if (filter.type === "date") {
            return (
              <DatePicker
                key={filter.key}
                size="filter"
                label={filter.label}
                value={values[filter.key] ?? ""}
                max={filter.max}
                required={filter.required}
                onChange={(value) => onChange({ [filter.key]: value })}
                className={controlClass}
              />
            );
          }
          if (filter.type === "period") {
            const shown = shownRange(filter.fromKey, filter.toKey, values);
            return renderPeriod(
              filter,
              { ...values, [filter.fromKey]: shown.from, [filter.toKey]: shown.to },
              (preset) => applyPeriod(filter, preset),
              "filter",
            );
          }
          const shown = shownRange(filter.fromKey, filter.toKey, values);
          return (
            <span key={rangeId(filter.fromKey, filter.toKey)} className="contents">
              <DatePicker
                size="filter"
                label={filter.fromLabel ?? "From"}
                value={shown.from}
                max={filter.max}
                required={filter.required}
                onChange={(value) => commitRange(filter.fromKey, filter.toKey, { from: value, to: shown.to })}
                className={controlClass}
              />
              <DatePicker
                size="filter"
                label={filter.toLabel ?? "To"}
                value={shown.to}
                max={filter.max}
                required={filter.required}
                onChange={(value) => commitRange(filter.fromKey, filter.toKey, { from: shown.from, to: value })}
                className={controlClass}
              />
            </span>
          );
        })}
        {showReset && <Button variant="link" onClick={onReset} className="ml-1 shrink-0">Reset</Button>}
      </div>
      {(desktopRangeError || yearHint) && (
        <div className="mt-2 hidden flex-col gap-1 md:flex">
          {desktopRangeError && <p role="alert" className="m-0 text-small font-bold text-danger">{desktopRangeError}</p>}
          {yearHint && <p className="m-0 text-small text-ink-muted">{yearHint}</p>}
        </div>
      )}

      {/* Phone */}
      <div className="flex items-center gap-2 md:hidden">
        {search && <SearchBar {...search} size="lg" className="flex-1" />}
        {filters.length > 0 && (
          <button
            type="button"
            aria-label={active ? `Filters (${activeCount} applied)` : "Filters"}
            onClick={openSheet}
            className={cx(
              "relative flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-field border focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary",
              active ? "border-primary bg-primary text-white" : "border-line-input bg-card text-ink",
            )}
          >
            <IconFilter size={18} />
            {active && (
              <span aria-hidden="true" className="absolute -top-1.5 -right-1.5 flex size-5 items-center justify-center rounded-full bg-gold text-caption font-extrabold text-primary ring-2 ring-page">
                {activeCount}
              </span>
            )}
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
          {sheetFilters.map((filter) => {
            if (filter.type === "select") {
              return (
                <Dropdown
                  key={filter.key}
                  label={filter.label}
                  options={withAll(filter)}
                  value={draft[filter.key] ?? ""}
                  onChange={(value) => setDraft((current) => ({ ...current, [filter.key]: value }))}
                />
              );
            }
            if (filter.type === "date") {
              return (
                <DatePicker
                  key={filter.key}
                  label={filter.label}
                  value={draft[filter.key] ?? ""}
                  max={filter.max}
                  required={filter.required}
                  onChange={(value) => setDraft((current) => ({ ...current, [filter.key]: value }))}
                />
              );
            }
            if (filter.type === "period") {
              return (
                <div key={filter.key} className="flex flex-col gap-1">
                  {renderPeriod(filter, draft, (preset) => {
                    const range = periodRangeFor(preset as PeriodPreset, filter.rangeFor);
                    if (!range) return;
                    setDraft((current) => ({ ...current, [filter.fromKey]: range.from, [filter.toKey]: range.to }));
                    setSheetError(null);
                  }, "form")}
                  {filter.financialYear && filter.financialYear !== "ready" && (
                    <p className="m-0 text-small text-ink-muted">{financialYearHint(filter.financialYear)}</p>
                  )}
                </div>
              );
            }
            return (
              <div key={rangeId(filter.fromKey, filter.toKey)} className="flex flex-col gap-4">
                <DatePicker
                  label={filter.fromLabel ?? "From"}
                  value={draft[filter.fromKey] ?? ""}
                  max={filter.max}
                  required={filter.required}
                  onChange={(value) => { setDraft((current) => ({ ...current, [filter.fromKey]: value })); setSheetError(null); }}
                />
                <DatePicker
                  label={filter.toLabel ?? "To"}
                  value={draft[filter.toKey] ?? ""}
                  max={filter.max}
                  required={filter.required}
                  onChange={(value) => { setDraft((current) => ({ ...current, [filter.toKey]: value })); setSheetError(null); }}
                />
              </div>
            );
          })}
          {(sheetError || sheetRangeError) && (
            <p role="alert" className="m-0 text-small font-bold text-danger">{sheetError ?? sheetRangeError}</p>
          )}
        </div>
      </BottomSheet>
    </div>
  );
}
