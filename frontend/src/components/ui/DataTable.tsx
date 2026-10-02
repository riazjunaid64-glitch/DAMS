import type { ReactNode } from "react";
import { cx } from "./cx.ts";

export type DataTableColumn<T> = {
  key: string;
  header: ReactNode;
  /** Custom cell. */
  render: (row: T) => ReactNode;
  align?: "left" | "right";
  /** Extra classes for this column's cells (width, wrapping). */
  className?: string;
};

/** A normal record, a group heading, a subtotal or the bold total. */
export type DataTableRowKind = "row" | "group" | "subtotal" | "total";

export type DataTableProps<T> = {
  columns: DataTableColumn<T>[];
  rows: readonly T[];
  rowKey: (row: T) => string | number;
  /**
   * Group header, subtotal or total. When omitted, a `kind` field on the row is used, and
   * anything else is a normal row.
   */
  rowKind?: (row: T) => DataTableRowKind;
  /** Text of a group header. Defaults to the first column. */
  groupLabel?: (row: T) => ReactNode;
  /** Makes each normal row open something; rows also open with Enter. Group headers are not clickable. */
  onRowClick?: (row: T) => void;
  /** Accessible name for the row action ("Open Ahmed Khan"). */
  rowLabel?: (row: T) => string;
  /** On phone, render each normal row as this card instead of the table. */
  phoneCard?: (row: T) => ReactNode;
  /** Shown instead of the rows when there are none and nothing is loading. */
  empty?: ReactNode;
  /**
   * Grey placeholder rows while the first load has nothing to show. On a refresh (rows are
   * already here) the old rows stay and a thin progress line shows.
   */
  loading?: boolean;
  /** How many placeholder rows to draw. */
  loadingCount?: number;
  /** Minimum table width before it scrolls sideways inside its card. */
  minWidth?: number;
  caption?: string;
  className?: string;
};

function kindOf<T>(row: T, rowKind?: (row: T) => DataTableRowKind): DataTableRowKind {
  if (rowKind) return rowKind(row);
  if (row && typeof row === "object" && "kind" in row) {
    const kind = (row as { kind?: unknown }).kind;
    if (kind === "group" || kind === "subtotal" || kind === "total") return kind;
  }
  return "row";
}

const placeholder = "block h-4 animate-pulse rounded bg-track";

/** Desktop list table with a custom cell per column; pair it with Pagination. */
export function DataTable<T>({
  columns,
  rows,
  rowKey,
  rowKind,
  groupLabel,
  onRowClick,
  rowLabel,
  phoneCard,
  empty,
  loading = false,
  loadingCount = 8,
  minWidth = 720,
  caption,
  className,
}: DataTableProps<T>) {
  const refreshing = loading && rows.length > 0;
  const showPlaceholders = loading && rows.length === 0;
  if (!loading && rows.length === 0 && empty) return <>{empty}</>;

  const labelFor = (row: T) => groupLabel ? groupLabel(row) : columns[0]?.render(row);

  const progress = refreshing ? (
    <div className="absolute inset-x-0 top-0 z-10 h-0.5 overflow-hidden bg-track" role="progressbar" aria-label="Loading">
      <div className="h-full w-1/3 animate-pulse bg-primary" />
    </div>
  ) : null;

  const bodyRows = showPlaceholders
    ? Array.from({ length: loadingCount }, (_, index) => (
        <tr key={`loading-${index}`} className="border-t border-line-soft">
          {columns.map((column) => (
            <td key={column.key} className="px-4 py-3.5">
              <span aria-hidden="true" className={cx(placeholder, column.align === "right" && "ml-auto w-16")} />
            </td>
          ))}
        </tr>
      ))
    : rows.map((row) => {
        const kind = kindOf(row, rowKind);
        if (kind === "group") {
          return (
            <tr key={rowKey(row)} className="border-t border-line-soft bg-page">
              <td colSpan={columns.length} className="px-4 py-2.5 text-label font-extrabold uppercase tracking-[0.4px] text-ink-2">
                {labelFor(row)}
              </td>
            </tr>
          );
        }
        const summary = kind === "subtotal" || kind === "total";
        const clickable = Boolean(onRowClick) && !summary;
        return (
          <tr
            key={rowKey(row)}
            tabIndex={clickable ? 0 : undefined}
            aria-label={clickable && rowLabel ? rowLabel(row) : undefined}
            onClick={clickable ? () => onRowClick!(row) : undefined}
            onKeyDown={clickable ? (event) => { if (event.key === "Enter" && event.target === event.currentTarget) onRowClick!(row); } : undefined}
            className={cx(
              "border-t border-line-soft align-middle",
              kind === "total" && "bg-table-head font-extrabold",
              kind === "subtotal" && "font-extrabold",
              clickable && "cursor-pointer transition-colors hover:bg-page focus-visible:bg-page focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary",
            )}
          >
            {columns.map((column) => (
              <td key={column.key} className={cx("px-4 py-3.5 text-sm text-ink", summary && "font-extrabold", column.align === "right" && "text-right", column.className)}>
                {column.render(row)}
              </td>
            ))}
          </tr>
        );
      });

  return (
    <div className={cx("relative", className)} aria-busy={loading || undefined}>
      {progress}
      <div className={cx("overflow-hidden rounded-card border border-line bg-card font-ui", phoneCard && "max-md:hidden")}>
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-left" style={{ minWidth }}>
            {caption && <caption className="sr-only">{caption}</caption>}
            <thead className="bg-table-head">
              <tr>
                {columns.map((column) => (
                  <th
                    key={column.key}
                    scope="col"
                    className={cx("whitespace-nowrap px-4 py-3.5 text-label font-bold uppercase tracking-[0.4px] text-ink-2", column.align === "right" && "text-right")}
                  >
                    {column.header}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>{bodyRows}</tbody>
          </table>
        </div>
      </div>
      {phoneCard && (
        <ul className="relative m-0 flex list-none flex-col gap-2.5 p-0 md:hidden">
          {showPlaceholders
            ? Array.from({ length: loadingCount }, (_, index) => (
                <li key={`loading-${index}`} aria-hidden="true" className="h-16 animate-pulse rounded-card bg-track" />
              ))
            : rows.map((row) => {
                const kind = kindOf(row, rowKind);
                if (kind === "group") {
                  return (
                    <li key={rowKey(row)}>
                      <p className="m-0 px-1 pt-1 text-label font-extrabold uppercase tracking-[0.4px] text-ink-2">{labelFor(row)}</p>
                    </li>
                  );
                }
                if (kind === "subtotal" || kind === "total") {
                  return (
                    <li key={rowKey(row)}>
                      <div className={cx("rounded-card border border-line bg-card px-4 py-3", kind === "total" && "bg-table-head")}>
                        {columns.map((column) => (
                          <div key={column.key} className="flex items-baseline justify-between gap-3 py-0.5">
                            {column.header ? <span className="text-small text-ink-muted">{column.header}</span> : <span />}
                            <span className="text-sm font-extrabold text-ink">{column.render(row)}</span>
                          </div>
                        ))}
                      </div>
                    </li>
                  );
                }
                return <li key={rowKey(row)}>{phoneCard(row)}</li>;
              })}
        </ul>
      )}
    </div>
  );
}
