import { useEffect, useState } from "react";
import Button from "../../lib/Button.tsx";
import { documentJson } from "./documentApi.ts";
import { statusLabel } from "./documentState.ts";
import type { DocumentAudit, DocumentAuditPage } from "./types.ts";

const PAGE_SIZE = 50;

export default function CustomerDocumentHistory({ customerId, history, hasMore }: {
  customerId: number;
  history: DocumentAudit[];
  hasMore: boolean;
}) {
  // The checklist inlines only the newest preview; older rows are pulled on demand via the keyset
  // endpoint (beforeId = the oldest row already shown), which stays stable as new activity is appended.
  const [older, setOlder] = useState<DocumentAudit[]>([]);
  const [more, setMore] = useState(hasMore);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Reset the appended pages whenever the inlined preview changes (e.g. after a refresh).
  useEffect(() => { setOlder([]); setMore(hasMore); setError(null); }, [history, hasMore]);

  const rows = [...history, ...older];

  const loadMore = async () => {
    const beforeId = rows[rows.length - 1]?.id;
    if (beforeId === undefined) return;
    setLoading(true); setError(null);
    try {
      const page = await documentJson<DocumentAuditPage>(
        `/api/customer-documents/customers/${customerId}/history?beforeId=${beforeId}&take=${PAGE_SIZE}`,
      );
      setOlder((current) => [...current, ...page.items]);
      setMore(page.hasMore);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Older history could not be loaded.");
    } finally {
      setLoading(false);
    }
  };

  if (rows.length === 0) {
    return <div className="rounded-2xl border border-dashed border-[var(--border)] p-12 text-center text-sm text-[var(--text-muted)]">No document activity has been recorded.</div>;
  }
  return (
    <div className="space-y-4">
      <ol className="space-y-3" aria-label="Customer document history">
        {rows.map((item) => (
          <li key={item.id} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
            <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-start">
              <div>
                <p className="font-medium text-[var(--text-heading)]">{statusLabel(item.action)}</p>
                <p className="mt-0.5 text-sm text-[var(--text-secondary)]">{item.documentName ?? "Document configuration"}</p>
              </div>
              <time className="text-xs text-[var(--text-muted)]" dateTime={item.occurredAt}>{new Date(item.occurredAt).toLocaleString()}</time>
            </div>
            {(item.previousStatus || item.newStatus) && (
              <p className="mt-2 text-xs text-[var(--text-muted)]">
                {item.previousStatus ? statusLabel(item.previousStatus) : "Not set"} → {item.newStatus ? statusLabel(item.newStatus) : "Not set"}
              </p>
            )}
            {item.notes && <p className="mt-2 text-sm text-[var(--text-secondary)]">{item.notes}</p>}
            <p className="mt-2 text-xs text-[var(--text-muted)]">By {item.performedByName ?? "System"}</p>
          </li>
        ))}
      </ol>
      {error && <p role="alert" className="text-sm text-rose-300">{error}</p>}
      {more && <div className="text-center"><Button size="sm" variant="outline" disabled={loading} onClick={() => void loadMore()}>{loading ? "Loading…" : "Load older activity"}</Button></div>}
    </div>
  );
}
