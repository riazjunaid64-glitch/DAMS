import { statusLabel } from "./documentState.ts";
import type { DocumentAudit } from "./types.ts";

export default function CustomerDocumentHistory({ history }: { history: DocumentAudit[] }) {
  if (history.length === 0) {
    return <div className="rounded-2xl border border-dashed border-[var(--border)] p-12 text-center text-sm text-[var(--text-muted)]">No document activity has been recorded.</div>;
  }
  return (
    <ol className="space-y-3" aria-label="Customer document history">
      {history.map((item) => (
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
  );
}
