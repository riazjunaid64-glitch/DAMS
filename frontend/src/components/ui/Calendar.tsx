import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { cx } from "./cx.ts";
import { IconChevronDown, IconChevronLeft, IconChevronRight } from "./icons.tsx";
import {
  MONTHS_LONG,
  MONTHS_SHORT,
  WEEKDAYS,
  addDays,
  addMonths,
  isOutOfRange,
  monthGrid,
  monthOutOfRange,
  parseIso,
  shiftMonth,
  toIso,
  type YearMonth,
} from "./pickerFormat.ts";

const YEARS_PER_PAGE = 12;

type CalendarProps = {
  /** The picked day ("YYYY-MM-DD"), or "" when none. */
  value: string;
  min?: string;
  max?: string;
  /** Today in Pakistan, as "YYYY-MM-DD". */
  today: string;
  /** Shows Clear (optional fields only). */
  clearable: boolean;
  onPick: (value: string) => void;
  onClear: () => void;
  /** Moves keyboard focus onto a day when the calendar opens. */
  autoFocus?: boolean;
};

const navButtonClass =
  "flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-lg border border-line-input bg-card text-ink-2 outline-none hover:bg-page focus-visible:ring-3 focus-visible:ring-primary-ring md:size-8";

const cellClass =
  "flex h-11 w-full cursor-pointer items-center justify-center rounded-lg text-small font-bold outline-none focus-visible:ring-3 focus-visible:ring-primary-ring md:h-9";

/**
 * The month grid inside DatePicker's popover and phone sheet. Week starts on Monday; tapping the
 * month title switches to a year and month grid (12 years a page). Arrow keys move between days,
 * PageUp / PageDown between months, Enter or Space picks.
 */
export function Calendar({ value, min, max, today, clearable, onPick, onClear, autoFocus = false }: CalendarProps) {
  const start = parseIso(value) ? value : today;
  const [view, setView] = useState<YearMonth>(() => {
    const parts = parseIso(start)!;
    return { year: parts.year, month: parts.month };
  });
  const [focusIso, setFocusIso] = useState(start);
  const [mode, setMode] = useState<"days" | "years">("days");
  const [pageStart, setPageStart] = useState(view.year - 5);
  const [draftYear, setDraftYear] = useState(view.year);
  const gridRef = useRef<HTMLDivElement>(null);
  const moveFocus = useRef(autoFocus);

  useEffect(() => {
    if (!moveFocus.current || mode !== "days") return;
    moveFocus.current = false;
    gridRef.current?.querySelector<HTMLButtonElement>(`[data-iso="${focusIso}"]`)?.focus({ preventScroll: true });
  }, [focusIso, mode, view]);

  const focusTo = (iso: string) => {
    const parts = parseIso(iso);
    if (!parts) return;
    moveFocus.current = true;
    setFocusIso(iso);
    setView({ year: parts.year, month: parts.month });
  };

  const onGridKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const step: Record<string, string> = {
      ArrowLeft: addDays(focusIso, -1),
      ArrowRight: addDays(focusIso, 1),
      ArrowUp: addDays(focusIso, -7),
      ArrowDown: addDays(focusIso, 7),
      PageUp: addMonths(focusIso, -1),
      PageDown: addMonths(focusIso, 1),
    };
    const next = step[event.key];
    if (!next) return;
    event.preventDefault();
    focusTo(next);
  };

  const showMonth = (months: number) => {
    setView(shiftMonth(view, months));
    setFocusIso(addMonths(focusIso, months));
  };

  const cells = monthGrid(view);
  const tabStop = cells.some((cell) => cell.inMonth && cell.iso === focusIso)
    ? focusIso
    : cells.find((cell) => cell.inMonth && !isOutOfRange(cell.iso, min, max))?.iso ?? cells.find((cell) => cell.inMonth)!.iso;
  const todayBlocked = isOutOfRange(today, min, max);

  const openYears = () => {
    setDraftYear(view.year);
    setPageStart(view.year - 5);
    setMode("years");
  };

  const pickMonth = (month: number) => {
    setView({ year: draftYear, month });
    setFocusIso(toIso(draftYear, month, 1));
    setMode("days");
  };

  const pageEnd = pageStart + YEARS_PER_PAGE - 1;

  return (
    <div className="w-full select-none font-ui text-ink md:w-[264px]">
      <div className="flex items-center justify-between gap-2">
        <button
          type="button"
          aria-label={mode === "days" ? "Previous month" : "Previous years"}
          onClick={() => (mode === "days" ? showMonth(-1) : setPageStart((from) => from - YEARS_PER_PAGE))}
          className={navButtonClass}
        >
          <IconChevronLeft size={16} />
        </button>
        {mode === "days" ? (
          <button
            type="button"
            aria-label={`${MONTHS_LONG[view.month]} ${view.year}, choose a year`}
            onClick={openYears}
            className="flex min-h-11 cursor-pointer items-center gap-1 rounded-lg px-2 text-body font-extrabold text-ink outline-none hover:bg-page focus-visible:ring-3 focus-visible:ring-primary-ring md:min-h-8"
          >
            {MONTHS_LONG[view.month]} {view.year}
            <IconChevronDown size={14} className="text-ink-2" />
          </button>
        ) : (
          <span className="text-body font-extrabold text-ink">{pageStart} – {pageEnd}</span>
        )}
        <button
          type="button"
          aria-label={mode === "days" ? "Next month" : "Next years"}
          onClick={() => (mode === "days" ? showMonth(1) : setPageStart((from) => from + YEARS_PER_PAGE))}
          className={navButtonClass}
        >
          <IconChevronRight size={16} />
        </button>
      </div>

      {mode === "days" ? (
        <>
          <div ref={gridRef} role="grid" aria-label={`${MONTHS_LONG[view.month]} ${view.year}`} onKeyDown={onGridKeyDown} className="mt-3">
            <div role="row" className="grid grid-cols-7">
              {WEEKDAYS.map((weekday) => (
                <span key={weekday} role="columnheader" className="pb-1 text-center text-caption font-bold text-ink-faint">{weekday}</span>
              ))}
            </div>
            {Array.from({ length: 6 }, (_, week) => (
              <div key={week} role="row" className="grid grid-cols-7">
                {cells.slice(week * 7, week * 7 + 7).map((cell) => {
                  const picked = cell.iso === value;
                  const blocked = isOutOfRange(cell.iso, min, max);
                  return (
                    <div key={cell.iso} role="gridcell" aria-selected={picked} className="p-px">
                      <button
                        type="button"
                        data-iso={cell.iso}
                        tabIndex={cell.iso === tabStop ? 0 : -1}
                        disabled={blocked}
                        aria-label={`${MONTHS_LONG[parseIso(cell.iso)!.month]} ${cell.day}, ${parseIso(cell.iso)!.year}`}
                        aria-current={cell.iso === today ? "date" : undefined}
                        onClick={() => onPick(cell.iso)}
                        className={cx(
                          cellClass,
                          picked
                            ? "bg-primary text-white"
                            : cx(
                                cell.inMonth && !blocked ? "text-ink" : "text-ink-faint",
                                !blocked && "hover:bg-selected",
                                cell.iso === today && "ring-1 ring-inset ring-gold",
                              ),
                          blocked && "cursor-not-allowed",
                        )}
                      >
                        {cell.day}
                      </button>
                    </div>
                  );
                })}
              </div>
            ))}
          </div>
          <div className="mt-2 flex items-center justify-between border-t border-line-soft pt-2">
            {clearable ? (
              <button type="button" onClick={onClear} className="min-h-11 cursor-pointer rounded-lg px-2 text-small font-bold text-ink-muted outline-none hover:bg-page focus-visible:ring-3 focus-visible:ring-primary-ring md:min-h-8">
                Clear
              </button>
            ) : <span />}
            <button
              type="button"
              disabled={todayBlocked}
              onClick={() => onPick(today)}
              className="min-h-11 cursor-pointer rounded-lg px-2 text-small font-bold text-gold-text outline-none hover:bg-page focus-visible:ring-3 focus-visible:ring-primary-ring disabled:cursor-not-allowed disabled:text-ink-faint disabled:hover:bg-transparent md:min-h-8"
            >
              Today
            </button>
          </div>
        </>
      ) : (
        <div className="mt-3">
          <p className="mb-1.5 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">Year</p>
          <div className="grid grid-cols-4 gap-1.5">
            {Array.from({ length: YEARS_PER_PAGE }, (_, index) => pageStart + index).map((year) => {
              const blocked = (!!min && year < parseIso(min)!.year) || (!!max && year > parseIso(max)!.year);
              return (
                <button
                  key={year}
                  type="button"
                  disabled={blocked}
                  aria-pressed={year === draftYear}
                  onClick={() => setDraftYear(year)}
                  className={cx(
                    "h-11 cursor-pointer rounded-lg text-small font-bold outline-none focus-visible:ring-3 focus-visible:ring-primary-ring disabled:cursor-not-allowed disabled:text-ink-faint md:h-9",
                    year === draftYear ? "bg-primary text-white" : "bg-selected text-ink hover:bg-line-soft disabled:hover:bg-selected",
                  )}
                >
                  {year}
                </button>
              );
            })}
          </div>
          <p className="mb-1.5 mt-3 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">Month</p>
          <div className="grid grid-cols-4 gap-1.5">
            {MONTHS_SHORT.map((name, month) => {
              const blocked = monthOutOfRange({ year: draftYear, month }, min, max);
              return (
                <button
                  key={name}
                  type="button"
                  disabled={blocked}
                  aria-pressed={draftYear === view.year && month === view.month}
                  onClick={() => pickMonth(month)}
                  className={cx(
                    "h-11 cursor-pointer rounded-lg text-small font-bold outline-none focus-visible:ring-3 focus-visible:ring-primary-ring disabled:cursor-not-allowed disabled:text-ink-faint md:h-9",
                    draftYear === view.year && month === view.month ? "bg-primary text-white" : "bg-selected text-ink hover:bg-line-soft disabled:hover:bg-selected",
                  )}
                >
                  {name}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
