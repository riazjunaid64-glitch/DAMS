import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { api } from "../../api/api.ts";
import { Button, IconPlus, Notice, cx, useIsPhone } from "../../components/ui";
import { formatDay } from "../../lib/dates.ts";
import { formatPkr } from "../../utils/currency.ts";
import { mutationSnapshotIsCurrent } from "../bookings/refreshCoordination.ts";
import { commissionStatus, rebateStatus } from "../bookings/statusNames.ts";
import { apiError, commissionRebateApi } from "./api.ts";
import { ApplyRebateDialog } from "./ApplyRebateDialog.tsx";
import { CommissionDialog } from "./CommissionDialog.tsx";
import { MoneyBlock } from "./MoneyBlock.tsx";
import { MovementRows } from "./MovementRows.tsx";
import { PayCommissionDialog } from "./PayCommissionDialog.tsx";
import { ReasonDialog } from "./ReasonDialog.tsx";
import { RebateDialog } from "./RebateDialog.tsx";
import { commissionSummary, rebateWayShort, remainingAfterReversal } from "./forms.ts";
import type { RunMutation } from "./runMutation.ts";
import { commissionActions, rebateActions } from "./state.ts";
import type { BookingWorkspace, Commission, FinanceAccountOption, InstallmentOption, MoneyMovement, Partner, Rebate } from "./types.ts";

// A cancelled or reversed record is closed history: it no longer holds the booking's one live
// rebate, nor its partner's one live commission.
const closedStatuses = ["Cancelled", "Reversed"];

type Dialog =
  | { kind: "commission"; existing: Commission | null }
  | { kind: "pay"; commission: Commission }
  | { kind: "reverseCommission"; commission: Commission; row: MoneyMovement }
  | { kind: "rebate"; existing: Rebate | null }
  | { kind: "apply"; rebate: Rebate }
  | { kind: "reverseRebate"; rebate: Rebate; row: MoneyMovement }
  | null;

type Props = {
  bookingId: number;
  /** Tells the booking page that a change reached what the customer owes, so it reads its figures again. */
  onChanged?: () => void;
  /**
   * The page bumps this when something other than this panel moved the booking while the panel sat
   * mounted behind another tab. It deliberately does not bump it for this panel's own changes: those
   * already answered with the whole workspace, so reading it again would only be an echo.
   */
  refreshToken?: number;
  /**
   * The booking's installments, from the page's own read. `installmentsFresh` says whether that read
   * is current: a rebate is taken off an installment by id, and ids from a schedule that failed to
   * refresh are how a credit lands on a row the server has since replaced. Both are required so a
   * caller that forgot them cannot silently offer an empty picker.
   */
  installments: InstallmentOption[];
  installmentsFresh: boolean;
};

const rebateTitle = (rebate: Rebate) => `Rebate · ${rebate.calculationType === "Percentage" ? `${rebate.percentageRate ?? 0}%` : "Fixed amount"}`;

/**
 * The booking's Commission & rebate tab: each partner commission and the customer rebate as a simple
 * block with its payments underneath. Every action — add, edit, pay, apply, reverse, cancel — is a
 * popup, and nothing here is editable inline.
 */
export default function BookingCommissionRebatePanel({ bookingId, onChanged, refreshToken = 0, installments, installmentsFresh }: Props) {
  const isPhone = useIsPhone();
  const [workspace, setWorkspace] = useState<BookingWorkspace | null>(null);
  const [partners, setPartners] = useState<Partner[]>([]);
  const [accounts, setAccounts] = useState<FinanceAccountOption[]>([]);
  const [accountsError, setAccountsError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // A failed refresh leaves every figure describing the booking as it was before whatever broke it.
  // Reading them is harmless; acting on them is not, so everything that moves money stays locked
  // until a reload succeeds.
  const [stale, setStale] = useState(false);
  const [dialog, setDialog] = useState<Dialog>(null);
  const loadSequence = useRef(0);
  const optionsLoaded = useRef(false);

  // The directory lists do not move when the booking does: partners and finance accounts are read on
  // the first visit only.
  const load = useCallback(async () => {
    const request = ++loadSequence.current;
    setLoading(true);
    setError(null);
    try {
      const wantOptions = !optionsLoaded.current;
      const [next, directory, accountResponse] = await Promise.all([
        commissionRebateApi.workspace(bookingId),
        wantOptions ? commissionRebateApi.partners("", true, 0, 100) : null,
        wantOptions ? api("/api/finance/accounts/options") : null,
      ]);
      let accountOptions: FinanceAccountOption[] | null = null;
      if (accountResponse) {
        if (accountResponse.ok) accountOptions = await accountResponse.json() as FinanceAccountOption[];
        else setAccountsError((await apiError(accountResponse, "The list of finance accounts could not be loaded.")).message);
      }
      if (request !== loadSequence.current) return;
      setWorkspace(next);
      if (directory) setPartners(directory.items);
      if (accountOptions) { setAccounts(accountOptions); setAccountsError(null); }
      optionsLoaded.current = true;
      setStale(false);
    } catch (failure) {
      if (request === loadSequence.current) {
        setError(failure instanceof Error ? failure.message : "Commission and rebate details could not be loaded.");
        setStale(true);
      }
    } finally {
      if (request === loadSequence.current) setLoading(false);
    }
  }, [bookingId]);

  // The token counts changes made elsewhere, so it is also how a change in flight finds out the booking
  // moved underneath it. Mirrored into a ref because a closure made before the change captures the old
  // value, and it is the value at completion that decides whether the answer is current.
  const externalChanges = useRef(refreshToken);
  useEffect(() => { externalChanges.current = refreshToken; }, [refreshToken]);
  useEffect(() => { void load(); }, [load, refreshToken]);

  // Reloading this panel alone cannot fix a schedule the page failed to read, so Try again asks for both.
  const retry = useCallback(() => {
    void load();
    if (!installmentsFresh) onChanged?.();
  }, [load, installmentsFresh, onChanged]);

  // The server answers a change with the whole workspace, so nothing here reads it again — unless the
  // booking moved meanwhile (a payment from another tab), when the answer is an older picture and is
  // abandoned for a fresh read. Only a rebate reaches what the customer owes, so only it tells the page.
  const run: RunMutation = useCallback(async (operation, affectsBooking = true) => {
    const seenAtStart = externalChanges.current;
    const result = await operation();
    if (mutationSnapshotIsCurrent(seenAtStart, externalChanges.current)) setWorkspace(result);
    else await load();
    if (affectsBooking) onChanged?.();
    return result;
  }, [load, onChanged]);

  // A proof attached from a row only adds a file to the workspace, which is all that is read again.
  const refreshWorkspace = useCallback(async () => {
    setWorkspace(await commissionRebateApi.workspace(bookingId));
  }, [bookingId]);

  const takenPartnerIds = useMemo(
    () => new Set((workspace?.commissions ?? []).filter((c) => !closedStatuses.includes(c.status)).map((c) => c.partnerId)),
    [workspace?.commissions],
  );

  if (loading && !workspace) {
    return <p className="py-10 text-center text-sm text-ink-muted">Loading commissions and rebates…</p>;
  }
  if (!workspace) {
    return (
      <Notice tone="red" role="alert" title={error ?? "Commission and rebate details are unavailable."} action={<Button size="sm" variant="outline" onClick={() => void load()}>Retry</Button>} />
    );
  }

  // A cancelled booking keeps its commission and rebate readable, and changes none of it.
  const readOnly = workspace.bookingStatus === "Cancelled";
  const locked = stale || !installmentsFresh;
  const liveRebate = workspace.rebates.some((rebate) => !closedStatuses.includes(rebate.status));
  const buttonSize = isPhone ? "md" : "sm";
  const actionButton = (label: string, onClick: () => void, disabled = locked) => (
    <Button key={label} size={buttonSize} variant="outline" fullWidth={isPhone} disabled={disabled} onClick={onClick}>{label}</Button>
  );
  const addButton = (label: string, onClick: () => void, disabled: boolean) => (
    <Button size={buttonSize} variant="outline" icon={<IconPlus size={16} />} disabled={disabled} onClick={onClick}>{label}</Button>
  );

  const section = (title: string, add: ReactNode, empty: string, blocks: ReactNode[]) => (
    <section className={cx("font-ui", !isPhone && "overflow-hidden rounded-card border border-line bg-card")} aria-label={title}>
      <header className={cx("flex items-center justify-between gap-3", isPhone ? "mb-3" : "px-5 py-4")}>
        <h3 className="m-0 text-section font-extrabold text-ink">{title}</h3>
        {add}
      </header>
      {blocks.length > 0
        ? <div className={cx(isPhone && "flex flex-col gap-3")}>{blocks}</div>
        : <p className={cx("m-0 text-body text-ink-muted", isPhone ? "rounded-card border border-line bg-card p-4" : "px-5 pb-5")}>{empty}</p>}
    </section>
  );

  const commissionBlocks = workspace.commissions.map((commission) => {
    const actions = commissionActions(commission.status, commission.paidAmount);
    return (
      <MoneyBlock
        key={commission.id}
        title={commission.partnerName}
        status={commissionStatus(commission.status, commission.paidAmount)}
        subtitle={commissionSummary(commission)}
        figures={[
          { label: "Commission", value: formatPkr(commission.finalAmount) },
          { label: "Paid", value: formatPkr(commission.paidAmount) },
          { label: "Remaining", value: formatPkr(commission.outstandingAmount) },
        ]}
        actions={readOnly ? [] : [
          actions.canPay && commission.outstandingAmount > 0 && actionButton("Pay", () => setDialog({ kind: "pay", commission })),
          actions.canEdit && actionButton("Edit", () => setDialog({ kind: "commission", existing: commission })),
        ]}
      >
        <MovementRows
          heading="Payments"
          rows={commission.payouts}
          ownerType="CommissionPayout"
          readOnly={readOnly}
          locked={locked}
          onReverse={(row) => setDialog({ kind: "reverseCommission", commission, row })}
          onProofChanged={refreshWorkspace}
        />
      </MoneyBlock>
    );
  });

  const rebateBlocks = workspace.rebates.map((rebate) => {
    const actions = rebateActions(rebate.status, rebate.appliedOrPaidAmount);
    return (
      <MoneyBlock
        key={rebate.id}
        title={rebateTitle(rebate)}
        status={rebateStatus(rebate.status, rebate.appliedOrPaidAmount)}
        subtitle={[rebateWayShort(rebate.method), rebate.reason].filter(Boolean).join(" · ")}
        figures={[
          { label: "Rebate", value: formatPkr(rebate.finalAmount) },
          { label: "Given", value: formatPkr(rebate.appliedOrPaidAmount) },
          { label: "Remaining", value: formatPkr(rebate.outstandingAmount) },
        ]}
        actions={readOnly ? [] : [
          actions.canDisburse && rebate.outstandingAmount > 0 && actionButton("Apply", () => setDialog({ kind: "apply", rebate })),
          actions.canEdit && actionButton("Edit", () => setDialog({ kind: "rebate", existing: rebate })),
        ]}
      >
        <MovementRows
          heading="Given to customer"
          rows={rebate.disbursements}
          ownerType="RebateDisbursement"
          readOnly={readOnly}
          locked={locked}
          onReverse={(row) => setDialog({ kind: "reverseRebate", rebate, row })}
          onProofChanged={refreshWorkspace}
        />
      </MoneyBlock>
    );
  });

  const closeDialog = () => setDialog(null);

  return (
    <div className="flex flex-col gap-5">
      {error && <Notice tone="red" role="alert" title={error} />}
      {locked && (
        <Notice
          tone="orange"
          role="status"
          title={stale ? "These figures could not be refreshed, so they may no longer match the booking." : "The booking's installment schedule could not be refreshed, so a credit cannot be placed on it."}
          message="Actions stay locked until they load."
          action={<Button size="sm" variant="outline" onClick={retry}>Try again</Button>}
        />
      )}

      {section(
        "Partner commission",
        !readOnly && addButton(isPhone ? "Add" : "Add commission", () => setDialog({ kind: "commission", existing: null }), locked),
        "No commission on this booking.",
        commissionBlocks,
      )}
      {section(
        "Customer rebate",
        !readOnly && !liveRebate && addButton(isPhone ? "Add" : "Add rebate", () => setDialog({ kind: "rebate", existing: null }), locked),
        "No rebate on this booking.",
        rebateBlocks,
      )}

      {dialog?.kind === "commission" && (
        <CommissionDialog
          bookingId={bookingId}
          workspace={workspace}
          existing={dialog.existing}
          partners={partners}
          takenPartnerIds={takenPartnerIds}
          run={run}
          onPartnerCreated={(partner) => setPartners((list) => [...list.filter((p) => p.id !== partner.id), partner])}
          onClose={closeDialog}
        />
      )}
      {dialog?.kind === "pay" && (
        <PayCommissionDialog bookingId={bookingId} commission={dialog.commission} financeAccounts={accounts} accountsError={accountsError} run={run} onProofUploaded={refreshWorkspace} onClose={closeDialog} />
      )}
      {dialog?.kind === "reverseCommission" && (
        <ReasonDialog
          title="Reverse this payment?"
          message={`${formatPkr(dialog.row.amount - dialog.row.reversedAmount)} paid to ${dialog.commission.partnerName} on ${formatDay(dialog.row.date)} will be reversed. ${formatPkr(remainingAfterReversal(dialog.commission.outstandingAmount, dialog.row))} will then be remaining.`}
          confirmLabel="Reverse payment"
          keyPrefix={`commission-reversal-${dialog.row.id}`}
          onClose={closeDialog}
          onConfirm={async (reason, key) => {
            const { commission, row } = dialog;
            await run(() => commissionRebateApi.reversePayout(bookingId, commission.id, row.id, { amount: row.amount - row.reversedAmount, reason, idempotencyKey: key }), false);
          }}
        />
      )}
      {dialog?.kind === "rebate" && <RebateDialog bookingId={bookingId} workspace={workspace} existing={dialog.existing} run={run} onClose={closeDialog} />}
      {dialog?.kind === "apply" && (
        <ApplyRebateDialog
          bookingId={bookingId}
          rebate={dialog.rebate}
          workspace={workspace}
          installments={installments}
          financeAccounts={accounts}
          accountsError={accountsError}
          run={run}
          onProofUploaded={refreshWorkspace}
          onClose={closeDialog}
        />
      )}
      {dialog?.kind === "reverseRebate" && (
        <ReasonDialog
          title="Reverse this payment?"
          message={`${formatPkr(dialog.row.amount - dialog.row.reversedAmount)} given to the customer on ${formatDay(dialog.row.date)} will be reversed. ${formatPkr(remainingAfterReversal(dialog.rebate.outstandingAmount, dialog.row))} will then be remaining.`}
          confirmLabel="Reverse payment"
          keyPrefix={`rebate-reversal-${dialog.row.id}`}
          onClose={closeDialog}
          onConfirm={async (reason, key) => {
            const { rebate, row } = dialog;
            await run(() => commissionRebateApi.reverseDisbursement(bookingId, rebate.id, row.id, { amount: row.amount - row.reversedAmount, reason, idempotencyKey: key }));
          }}
        />
      )}
    </div>
  );
}
