import { useEffect, useRef } from "react";
import type { ReactNode } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";

export interface Column<T> {
  key: string;
  header: string;
  /** A CSS grid track, e.g. "120px" or "minmax(160px,1fr)". */
  width: string;
  align?: "left" | "right";
  render: (row: T) => ReactNode;
}

interface Props<T> {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T, index: number) => string;
  /** Initial load (no rows yet). */
  loading: boolean;
  /** Appending the next page. */
  loadingMore: boolean;
  hasMore: boolean;
  onLoadMore: () => void;
  emptyText: string;
  /** Min table width before horizontal scroll kicks in. */
  minWidth?: number;
  /** Scroll viewport height in px. */
  height?: number;
  /** Changing this resets the scroll back to the top (e.g. when the view/filters change). */
  resetKey?: string;
}

const ROW_HEIGHT = 52;

export default function VirtualInfiniteTable<T>({
  columns,
  rows,
  rowKey,
  loading,
  loadingMore,
  hasMore,
  onLoadMore,
  emptyText,
  minWidth = 720,
  height = 560,
  resetKey,
}: Props<T>) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const template = columns.map((c) => c.width).join(" ");

  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 10,
  });

  // Jump back to the top when the dataset changes (new view / filters) so the user
  // doesn't land partway down a freshly-loaded table.
  useEffect(() => {
    if (scrollRef.current) scrollRef.current.scrollTop = 0;
    virtualizer.scrollToOffset(0);
  }, [resetKey, virtualizer]);

  const virtualItems = virtualizer.getVirtualItems();
  const lastIndex = virtualItems.length ? virtualItems[virtualItems.length - 1].index : -1;

  // Fetch the next chunk once the user scrolls within ~12 rows of the end.
  useEffect(() => {
    if (loading || loadingMore || !hasMore) return;
    if (lastIndex >= rows.length - 12) onLoadMore();
  }, [lastIndex, rows.length, hasMore, loading, loadingMore, onLoadMore]);

  return (
    <div className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
      <div ref={scrollRef} className="overflow-auto" style={{ maxHeight: height }}>
        <div style={{ minWidth }}>
          {/* Sticky header */}
          <div
            className="sticky top-0 z-10 grid border-b border-[var(--border)] bg-[var(--surface-glass)] backdrop-blur"
            style={{ gridTemplateColumns: template }}
          >
            {columns.map((c) => (
              <div
                key={c.key}
                className={`px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)] ${
                  c.align === "right" ? "text-right" : "text-left"
                }`}
              >
                {c.header}
              </div>
            ))}
          </div>

          {/* Body */}
          {loading ? (
            <div className="px-5 py-12 text-center text-sm text-[var(--text-muted)]">Loading…</div>
          ) : rows.length === 0 ? (
            <div className="px-5 py-12 text-center text-sm text-[var(--text-muted)]">{emptyText}</div>
          ) : (
            <div style={{ height: virtualizer.getTotalSize(), position: "relative", width: "100%" }}>
              {virtualItems.map((vi) => {
                const row = rows[vi.index];
                return (
                  <div
                    key={rowKey(row, vi.index)}
                    className="absolute left-0 top-0 grid w-full items-center border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)]"
                    style={{
                      height: ROW_HEIGHT,
                      transform: `translateY(${vi.start}px)`,
                      gridTemplateColumns: template,
                    }}
                  >
                    {columns.map((c) => (
                      <div
                        key={c.key}
                        className={`truncate px-5 text-sm ${c.align === "right" ? "text-right" : "text-left"}`}
                      >
                        {c.render(row)}
                      </div>
                    ))}
                  </div>
                );
              })}
            </div>
          )}

          {loadingMore && (
            <div className="border-t border-[var(--border)] px-5 py-3 text-center text-xs text-[var(--text-muted)]">
              Loading more…
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
