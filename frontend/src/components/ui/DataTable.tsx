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

export type DataTableProps<T> = {
  columns: DataTableColumn<T>[];
  rows: readonly T[];
  rowKey: (row: T) => string | number;
  /** Makes each row open something; rows also open with Enter. */
  onRowClick?: (row: T) => void;
  /** Accessible name for the row action ("Open Ahmed Khan"). */
  rowLabel?: (row: T) => string;
  /** On phone, render each row as this card instead of the table. */
  phoneCard?: (row: T) => ReactNode;
  /** Shown instead of the rows when there are none. */
  empty?: ReactNode;
  /** Minimum table width before it scrolls sideways inside its card. */
  minWidth?: number;
  caption?: string;
  className?: string;
};

/** Desktop list table with a custom cell per column; pair it with Pagination. */
export function DataTable<T>({ columns, rows, rowKey, onRowClick, rowLabel, phoneCard, empty, minWidth = 720, caption, className }: DataTableProps<T>) {
  if (rows.length === 0 && empty) return <>{empty}</>;
  return (
    <>
      <div className={cx("overflow-hidden rounded-card border border-line bg-card font-ui", phoneCard && "max-md:hidden", className)}>
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
            <tbody>
              {rows.map((row) => (
                <tr
                  key={rowKey(row)}
                  tabIndex={onRowClick ? 0 : undefined}
                  aria-label={onRowClick && rowLabel ? rowLabel(row) : undefined}
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                  onKeyDown={onRowClick ? (event) => { if (event.key === "Enter" && event.target === event.currentTarget) onRowClick(row); } : undefined}
                  className={cx(
                    "border-t border-line-soft align-middle",
                    onRowClick && "cursor-pointer transition-colors hover:bg-page focus-visible:bg-page focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary",
                  )}
                >
                  {columns.map((column) => (
                    <td key={column.key} className={cx("px-4 py-3.5 text-sm text-ink", column.align === "right" && "text-right", column.className)}>
                      {column.render(row)}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      {phoneCard && (
        <ul className="m-0 flex list-none flex-col gap-2.5 p-0 md:hidden">
          {rows.map((row) => <li key={rowKey(row)}>{phoneCard(row)}</li>)}
        </ul>
      )}
    </>
  );
}
