import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import Button from "../../lib/Button.tsx";
import { ErrorBanner, inputClass } from "./CrmUi.tsx";
import { apiJson, jsonRequest } from "./leadApi.ts";
import { describeHeldEnquiry, heldEnquiriesHeading, type HeldEnquiryList } from "./heldEnquiries.ts";
import { formatDateTime } from "./types.ts";

/**
 * External enquiries whose details match more than one open lead. They were not added to any
 * lead; an administrator decides here which one each belongs to. Renders nothing while there is
 * nothing waiting.
 */
export default function HeldEnquiriesPanel({ onResolved }: { onResolved: () => void }) {
  const [list, setList] = useState<HeldEnquiryList>({ totalWaiting: 0, items: [] });
  const [loadError, setLoadError] = useState<string | null>(null);
  // Kept apart from loadError: the list reloads after every decision, and a successful reload
  // must not wipe the reason the decision was refused.
  const [resolveError, setResolveError] = useState<{ enquiryId: number; message: string } | null>(null);
  const [notes, setNotes] = useState<Record<number, string>>({});
  const [busyId, setBusyId] = useState<number | null>(null);

  const load = useCallback(async () => {
    try {
      setList(await apiJson<HeldEnquiryList>("/api/leads/held-enquiries"));
      setLoadError(null);
    } catch (caught) {
      setLoadError(caught instanceof Error ? caught.message : "Held enquiries could not be loaded.");
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const resolve = async (enquiryId: number, body: { leadId?: number; dismiss?: boolean }) => {
    setBusyId(enquiryId);
    setResolveError(null);
    try {
      await apiJson(`/api/leads/held-enquiries/${enquiryId}/resolve`,
        jsonRequest("POST", { ...body, notes: notes[enquiryId]?.trim() || null }));
      onResolved();
    } catch (caught) {
      setResolveError({ enquiryId, message: caught instanceof Error ? caught.message : "The enquiry could not be resolved." });
    } finally {
      await load();
      setBusyId(null);
    }
  };

  if (list.items.length === 0 && !loadError && !resolveError) return null;

  return (
    <section aria-label="Held enquiries" className="rounded-2xl border border-amber-500/25 bg-amber-500/[0.06] p-4 sm:p-5">
      <h2 className="text-base font-semibold text-[var(--text-heading)]">{heldEnquiriesHeading(list)}</h2>
      <p className="mt-1 text-sm text-[var(--text-secondary)]">
        Each of these matches more than one open lead, so it was not added to any of them. Choose the lead it belongs to.
      </p>
      {loadError && <div className="mt-3"><ErrorBanner message={loadError} onRetry={() => void load()} /></div>}
      {resolveError && !list.items.some((enquiry) => enquiry.id === resolveError.enquiryId) && (
        <div className="mt-3"><ErrorBanner message={resolveError.message} /></div>
      )}
      <ul className="mt-4 space-y-3">
        {list.items.map((enquiry) => {
          const view = describeHeldEnquiry(enquiry);
          const busy = busyId === enquiry.id;
          return (
            <li key={enquiry.id} className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] p-4 text-sm">
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <p className="font-semibold text-[var(--text-heading)]">{view.title}</p>
                <p className="text-xs text-[var(--text-secondary)]">{view.origin} · {formatDateTime(enquiry.receivedAt)}</p>
              </div>
              {view.contact.length > 0 && <p className="mt-1 text-[var(--text-secondary)]">{view.contact.join(" · ")}</p>}
              {enquiry.notes && <p className="mt-1 text-[var(--text-secondary)]">{enquiry.notes}</p>}
              {view.waitingNote && <p className="mt-2 font-medium text-amber-300">{view.waitingNote}</p>}
              {resolveError?.enquiryId === enquiry.id && <div className="mt-3"><ErrorBanner message={resolveError.message} /></div>}
              <ul className="mt-3 space-y-2">
                {view.choices.map((choice) => (
                  <li key={choice.leadId} className="flex flex-wrap items-center gap-2">
                    <div className="mr-auto">
                      <Link className="font-medium text-[var(--text-heading)] hover:text-[var(--accent)]" to={`/crm/leads/${choice.leadId}`}>{choice.title}</Link>
                      <p className="text-xs text-[var(--text-secondary)]">{choice.blockedReason ?? choice.detail}</p>
                    </div>
                    <Button size="sm" variant="outline" disabled={busy || !!choice.blockedReason}
                      onClick={() => void resolve(enquiry.id, { leadId: choice.leadId })}>{choice.addLabel}</Button>
                  </li>
                ))}
              </ul>
              <div className="mt-3 flex flex-wrap items-center gap-2">
                <input aria-label="Note about this decision (optional)" placeholder="Note about this decision (optional)" maxLength={1000}
                  className={`${inputClass} min-w-[12rem] flex-1`} value={notes[enquiry.id] ?? ""}
                  onChange={(e) => setNotes((current) => ({ ...current, [enquiry.id]: e.target.value }))} />
                {view.canDismiss && (
                  <Button size="sm" variant="ghost" disabled={busy}
                    onClick={() => { if (window.confirm("Dismiss this enquiry? It will not be added to any lead.")) void resolve(enquiry.id, { dismiss: true }); }}>
                    Dismiss
                  </Button>
                )}
              </div>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
