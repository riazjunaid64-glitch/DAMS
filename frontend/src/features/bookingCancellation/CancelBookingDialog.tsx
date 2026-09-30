import { useCallback, useEffect, useId, useState, type FormEvent } from "react";
import { Button, IconAlert, Modal, Notice, NumberField, TextArea, useToast } from "../../components/ui";
import { useIdempotencyKeys } from "../../lib/idempotency.ts";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { formatPkr } from "../../utils/currency.ts";
import type { BookingDetail, FinanceAccountOption } from "../bookings/detailTypes.ts";
import { DialogTitle } from "../bookings/DialogTitle.tsx";
import { useProofUpload } from "../proof/useProofUpload.ts";
import { bookingCancellationApi } from "./api.ts";
import { RefundChoice } from "./RefundChoice.tsx";
import { RefundPayoutFields } from "./RefundPayoutFields.tsx";
import { cancelErrors, computeRetained, isStaleCancellationError, type CancelErrors, type CancelFields } from "./state.ts";
import type { CancelBookingRequest, RefundPaymentMethod } from "./types.ts";

type Props = {
  booking: Pick<BookingDetail, "id" | "bookingReference" | "unitNumber">;
  financeAccounts: FinanceAccountOption[];
  /** Set when the account list could not be loaded, so the account field can say why it is empty. */
  accountsError: string | null;
  onClose: () => void;
  /** Reloads the booking once it is cancelled, before the popup closes. */
  onCancelled: () => Promise<void> | void;
};

/** What the decision is made against: read fresh when the popup opens, and again when the server says it went stale. */
type Snapshot = { cashReceived: number; concurrencyToken: string };

const keeps = (caption: string, value: string) => (
  <div className="min-w-0">
    <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-2">{caption}</p>
    <p className="m-0 mt-2 text-section font-extrabold tabular-nums text-primary">{value}</p>
  </div>
);

/**
 * Cancel booking. The server cancels and settles the refund in one step; this popup only collects the
 * decision: the reason, whether the customer is refunded now, later or not at all, and how much.
 * Everything is measured against a fresh read of the booking, and the server re-checks it.
 */
export function CancelBookingDialog({ booking, financeAccounts, accountsError, onClose, onCancelled }: Props) {
  const toast = useToast();
  const keys = useIdempotencyKeys();
  const proof = useProofUpload();
  const formId = useId();
  // Read once: the date sent is the date the person saw locked in the field.
  const [refundDate] = useState(pakistanToday);

  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [fields, setFields] = useState<CancelFields>({
    reason: "",
    decision: "",
    refundAmount: "",
    method: "Cash",
    accountId: financeAccounts.length === 1 ? String(financeAccounts[0]!.id) : "",
    reference: "",
  });
  const [shown, setShown] = useState<CancelErrors>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);

  const loadSnapshot = useCallback(async () => {
    setLoadError(null);
    try {
      const fresh = await bookingCancellationApi.current(booking.id);
      setSnapshot({ cashReceived: fresh.payments.reduce((sum, p) => sum + p.amount, 0), concurrencyToken: fresh.concurrencyToken });
      setStale(false);
      setError(null);
    } catch (failure) {
      setSnapshot(null);
      setLoadError(failure instanceof Error ? failure.message : "The booking could not be loaded.");
    }
  }, [booking.id]);

  useEffect(() => { void loadSnapshot(); }, [loadSnapshot]);

  const set = (change: Partial<CancelFields>) => {
    setFields((current) => ({ ...current, ...change }));
    // A field's message goes as soon as it is edited, so it never lingers beside a fixed value.
    setShown((current) => {
      const next = { ...current };
      for (const key of Object.keys(change)) delete next[key as keyof CancelFields];
      // Cash needs no reference, so a missing-reference message must not outlive the method that asked for it.
      if (change.method !== undefined) delete next.reference;
      return next;
    });
  };

  const cash = snapshot?.cashReceived ?? 0;
  const paid = cash > 0;
  const refunding = fields.decision === "PayNow" || fields.decision === "PayLater";
  const refundValue = paid && refunding ? Number(fields.refundAmount) || 0 : 0;
  const retained = computeRetained(cash, refundValue);
  const payNow = paid && fields.decision === "PayNow";

  // "No refund" is the only choice that fixes the amount; moving off it clears the stale zero.
  const chooseDecision = (decision: CancelFields["decision"]) =>
    set({ decision, refundAmount: decision === "None" ? "" : fields.refundAmount });

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !snapshot) return;

    const problems = cancelErrors(fields, cash);
    setShown(problems);
    if (Object.keys(problems).length > 0) return;

    setSaving(true);
    setError(null);
    setStale(false);
    const intent: Omit<CancelBookingRequest, "idempotencyKey"> = {
      reason: fields.reason.trim(),
      expectedCustomerCashReceived: cash,
      refundAmount: refundValue,
      // A refund of nothing is "No refund", whatever was ticked — the server insists.
      refundDecision: refundValue === 0 ? "None" : (fields.decision as "PayNow" | "PayLater"),
      concurrencyToken: snapshot.concurrencyToken,
      refundFinanceAccountId: payNow ? Number(fields.accountId) : null,
      refundPaymentMethod: payNow ? (fields.method as RefundPaymentMethod) : null,
      refundPaymentReference: payNow ? fields.reference.trim() || null : null,
      refundPaidAt: payNow ? refundDate : null,
    };
    // The same intent keeps the same key, so pressing the button again after a dropped connection is
    // recognised as the retry it is rather than cancelling (or refunding) twice.
    const signature = `cancel:${booking.id}:${JSON.stringify(intent)}`;
    const body: CancelBookingRequest = { ...intent, idempotencyKey: keys.key(signature, "cancel") };
    try {
      const saved = await bookingCancellationApi.cancel(booking.id, body);
      keys.release(signature);
      const refundId = saved.cancellationSettlement?.refund?.id ?? null;
      // A failed proof never undoes the cancellation: the popup closes with a warning and the file is
      // attached later from the saved refund.
      toast.success(`${booking.bookingReference} cancelled.`);
      if (payNow && proof.hasFile && !(refundId !== null && await proof.upload("CancellationRefund", refundId))) {
        toast.error("The cancellation and refund were saved, but the proof did not upload. Attach it from the refund.");
      }
      try {
        await onCancelled();
      } finally {
        onClose();
      }
    } catch (failure) {
      const message = failure instanceof Error ? failure.message : "The booking could not be cancelled.";
      setError(message);
      setStale(isStaleCancellationError(message));
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="md"
      phoneLayout="fullscreen"
      title={<DialogTitle title="Cancel booking" subtitle={`${booking.bookingReference} · Unit ${booking.unitNumber}`} />}
      cancelLabel="Keep booking"
      primaryAction={{ label: "Cancel booking", variant: "danger", form: formId, loading: saving, disabled: !snapshot || stale }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {loadError && (
          <Notice tone="red" role="alert" title={loadError} action={<Button size="sm" variant="outline" onClick={() => void loadSnapshot()}>Retry</Button>} />
        )}
        {!snapshot && !loadError && <p className="m-0 text-body text-ink-2">Loading the booking…</p>}
        {error && (
          <Notice
            tone="red"
            role="alert"
            title={error}
            action={stale ? <Button size="sm" variant="outline" onClick={() => void loadSnapshot()}>Reload figures</Button> : undefined}
          />
        )}
        {accountsError && payNow && <Notice tone="orange" role="alert" title={accountsError} />}

        {snapshot && (
          <>
            <div className="flex items-center justify-between gap-3 rounded-field bg-page px-3.5 py-3 text-body font-extrabold text-ink">
              <span className="text-ink-2">Customer has paid</span>
              <span className="tabular-nums">{formatPkr(cash)}</span>
            </div>

            <TextArea
              label="Reason"
              required
              rows={3}
              maxLength={1000}
              disabled={saving}
              error={shown.reason}
              value={fields.reason}
              onChange={(event) => set({ reason: event.target.value })}
            />

            {paid && <RefundChoice value={fields.decision} onChange={chooseDecision} disabled={saving} error={shown.decision} />}

            {paid && fields.decision === "None" && keeps("Company keeps", formatPkr(cash))}

            {paid && refunding && (
              <div className="grid gap-4 md:grid-cols-2">
                <NumberField
                  label="Refund amount"
                  required
                  prefix="Rs"
                  disabled={saving}
                  helper={`Up to ${formatPkr(cash)}`}
                  error={shown.refundAmount}
                  value={fields.refundAmount}
                  onChange={(refundAmount) => set({ refundAmount })}
                />
                {keeps("Company keeps", formatPkr(retained))}
              </div>
            )}

            {payNow && (
              <RefundPayoutFields
                fields={fields}
                errors={shown}
                onChange={set}
                financeAccounts={financeAccounts}
                proof={proof.fieldProps}
                refundDate={refundDate}
                disabled={saving}
              />
            )}

            <Notice tone="red" icon={<IconAlert size={18} />} title={`Unit ${booking.unitNumber} becomes Available again. This cannot be undone.`} />
          </>
        )}
      </form>
    </Modal>
  );
}
