import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button, Card, ConfirmDialog, IconInbox, Modal, Notice, StatusBadge, useToast } from "../../components/ui";
import { formatWhen } from "../../lib/dates.ts";
import { describeHeldEnquiry, heldEnquiriesTitle, type HeldEnquiry, type HeldEnquiryList } from "./heldEnquiries.ts";
import { apiJson, jsonRequest } from "./leadApi.ts";

/**
 * External enquiries whose details match more than one lead: a banner while any are waiting and
 * the popup where an Admin or Sales manager chooses the lead each belongs to. Renders nothing
 * while nothing is waiting. The endpoint is theirs only, so render this only for crm.manage.
 */
export function HeldEnquiries({ onResolved }: { onResolved: () => void }) {
  const toast = useToast();
  const [list, setList] = useState<HeldEnquiryList | null>(null);
  const [open, setOpen] = useState(false);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [dismissing, setDismissing] = useState<HeldEnquiry | null>(null);

  const load = useCallback(async () => {
    try {
      const next = await apiJson<HeldEnquiryList>("/api/leads/held-enquiries");
      setList(next);
      if (next.items.length === 0) setOpen(false);
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "Enquiries to review could not be loaded.");
    }
  }, [toast]);

  useEffect(() => { void load(); }, [load]);

  const resolve = async (enquiry: HeldEnquiry, body: { leadId: number } | { dismiss: true }, done: string) => {
    setBusyId(enquiry.id);
    try {
      await apiJson(`/api/leads/held-enquiries/${enquiry.id}/resolve`, jsonRequest("POST", { ...body, notes: null }));
      toast.success(done);
      setDismissing(null);
      setList((current) => current && { totalWaiting: current.totalWaiting - 1, items: current.items.filter((item) => item.id !== enquiry.id) });
      onResolved();
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "The enquiry could not be resolved.");
    } finally {
      setBusyId(null);
    }
    // Either way, what is waiting now comes from the server: someone else may have decided too.
    await load();
  };

  if (!list || list.totalWaiting === 0) return null;

  return (
    <>
      <Notice
        tone="gold"
        icon={<IconInbox size={18} />}
        title={heldEnquiriesTitle(list.totalWaiting)}
        message="Each one matches more than one lead"
        action={<Button onClick={() => setOpen(true)}>Review</Button>}
      />
      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title={<span className="flex items-center gap-2">Enquiries to review <StatusBadge status="count" tone="gold">{list.totalWaiting.toLocaleString("en-PK")}</StatusBadge></span>}
        size="lg"
        phoneLayout="fullscreen"
        cancelLabel="Close"
      >
        <ul className="m-0 flex list-none flex-col gap-3 p-0">
          {list.items.map((enquiry) => {
            const view = describeHeldEnquiry(enquiry);
            const busy = busyId === enquiry.id;
            return (
              <li key={enquiry.id}>
                <Card className="text-ink">
                  <div className="flex flex-col gap-0.5 md:flex-row md:items-baseline md:justify-between md:gap-3">
                    <p className="m-0 text-body font-extrabold text-ink">{view.title}</p>
                    <p className="m-0 text-small text-ink-muted">{view.origin} · {formatWhen(enquiry.receivedAt)}</p>
                  </div>
                  {view.contact.length > 0 && <p className="m-0 mt-1 text-sm text-ink-2">{view.contact.join(" · ")}</p>}
                  <p className="m-0 mt-3 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">Matches these leads</p>
                  <ul className="m-0 list-none p-0">
                    {view.matches.map((match) => (
                      <li key={match.leadId} className="flex flex-col gap-2 border-b border-line-soft py-3 md:flex-row md:items-center md:justify-between">
                        <div className="min-w-0">
                          <p className="m-0 flex flex-wrap items-center gap-x-2 gap-y-1">
                            <Link to={`/crm/leads/${match.leadId}`} className="text-sm font-extrabold text-ink no-underline hover:underline">{match.leadName}</Link>
                            <span className="text-small text-ink-muted">{match.leadReference}</span>
                            <StatusBadge status={match.status} />
                          </p>
                          <p className="m-0 mt-1 flex flex-wrap items-center gap-2 text-small text-ink-2">
                            {match.ownerName}
                            <StatusBadge status="match" tone="gold">{match.matched}</StatusBadge>
                          </p>
                        </div>
                        {match.canAdd ? (
                          <Button
                            variant="outline"
                            size="sm"
                            className="max-md:w-full"
                            disabled={busyId !== null}
                            loading={busy}
                            onClick={() => void resolve(enquiry, { leadId: match.leadId }, `Enquiry added to ${match.leadReference}`)}
                          >
                            Add to this lead
                          </Button>
                        ) : (
                          <span className="text-small font-bold text-ink-faint">Closed lead</span>
                        )}
                      </li>
                    ))}
                  </ul>
                  {view.canDismiss && (
                    <div className="mt-3 flex justify-end">
                      <Button variant="danger" size="sm" disabled={busyId !== null} onClick={() => setDismissing(enquiry)}>Dismiss enquiry</Button>
                    </div>
                  )}
                </Card>
              </li>
            );
          })}
        </ul>
      </Modal>
      <ConfirmDialog
        open={dismissing !== null}
        onClose={() => setDismissing(null)}
        onConfirm={() => dismissing && void resolve(dismissing, { dismiss: true }, "Enquiry dismissed")}
        title="Dismiss this enquiry?"
        message="It will not be added to any lead."
        confirmLabel="Dismiss enquiry"
        danger
        loading={busyId !== null}
      />
    </>
  );
}
