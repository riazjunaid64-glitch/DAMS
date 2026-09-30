import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import { Modal, Notice, useToast } from "../components/ui";
import { pageAccess } from "../features/access/permissions.ts";
import type { FinanceAccountOption } from "../features/bookings/detailTypes.ts";
import { useIdempotencyKeys, moneyRequest } from "../lib/idempotency.ts";
import { CustomerStep } from "../features/newBooking/CustomerStep.tsx";
import { parseCustomers } from "../features/newBooking/customers.ts";
import { DoneCard } from "../features/newBooking/DoneCard.tsx";
import {
  bookingPayload, draftSummary, emptyDraft, firstStepWithErrors, isDirty, stepErrors, withUnit, type BookingDraft, type StepIndex,
} from "../features/newBooking/draft.ts";
import { KinStep } from "../features/newBooking/KinStep.tsx";
import { PriceStep } from "../features/newBooking/PriceStep.tsx";
import { ReviewStep } from "../features/newBooking/ReviewStep.tsx";
import { StepsFrame } from "../features/newBooking/StepsFrame.tsx";
import { UnitStep } from "../features/newBooking/UnitStep.tsx";
import type { PickerUnit } from "../features/newBooking/units.ts";
import { useProofUpload } from "../features/proof/useProofUpload.ts";
import Container from "../lib/Container.tsx";

type Props = { user: User | null };

interface Created {
  id: number;
  reference: string;
  paymentId: number | null;
  unitNumber: string;
  customerName: string;
}

/**
 * New booking in five steps: Unit → Customer → Next of kin → Price & payment → Review. Opens empty from
 * Bookings, with the unit already picked from a unit page (?unitId=), or with a customer already chosen
 * (?customerId=). Nothing is saved until Create booking, and that request carries a retry key so a
 * double click or a retry can never make a second booking or a second payment.
 */
export default function NewBookingPage({ user }: Props) {
  const navigate = useNavigate();
  const toast = useToast();
  const keys = useIdempotencyKeys();
  const proof = useProofUpload();
  const [params] = useSearchParams();
  const launchUnit = params.get("unitId");
  const launchCustomer = params.get("customerId");
  const access = pageAccess(user?.role, "bookings");

  const [projects, setProjects] = useState<{ id: number; projectName: string }[]>([]);
  const [accounts, setAccounts] = useState<FinanceAccountOption[]>([]);
  const [accountsError, setAccountsError] = useState<string | null>(null);
  const [ready, setReady] = useState(!launchUnit && !launchCustomer);
  const [draft, setDraft] = useState<BookingDraft>(emptyDraft);
  const baseline = useRef<BookingDraft>(draft);
  const [step, setStep] = useState<StepIndex>(0);
  const [attempted, setAttempted] = useState<ReadonlySet<number>>(new Set());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [launchNotice, setLaunchNotice] = useState<string | null>(null);
  const [created, setCreated] = useState<Created | null>(null);
  const [confirmDiscard, setConfirmDiscard] = useState(false);

  // Projects and accounts, then whatever the page was opened with.
  useEffect(() => {
    if (access !== "allow") return;
    let current = true;
    void (async () => {
      let start = emptyDraft();
      let list: { id: number; projectName: string }[] = [];
      try {
        const [projectsResponse, accountsResponse] = await Promise.all([api("/api/Project"), api("/api/finance/accounts/options")]);
        if (projectsResponse.ok) list = (await projectsResponse.json() as { id: number; projectName: string }[]).map(({ id, projectName }) => ({ id, projectName }));
        if (accountsResponse.ok) setAccounts(await accountsResponse.json() as FinanceAccountOption[]);
        else setAccountsError("The list of finance accounts could not be loaded, so money received cannot be recorded.");
      } catch {
        setAccountsError("The list of finance accounts could not be loaded, so money received cannot be recorded.");
      }
      if (launchUnit) {
        try {
          const response = await api(`/api/Unit/${launchUnit}`, undefined, false);
          if (response.ok) {
            const unit = await response.json() as PickerUnit & { projectId: number };
            const project = list.find((p) => p.id === unit.projectId);
            start = { ...start, projectId: String(unit.projectId), projectName: project?.projectName ?? "" };
            if (unit.status === "Available") start = withUnit(start, unit);
            else setLaunchNotice(`Unit ${unit.unitNumber} is no longer available. Pick another unit.`);
          }
        } catch { /* the picker still works */ }
      }
      if (launchCustomer) {
        try {
          const response = await api(`/api/Customer/${launchCustomer}`);
          if (response.ok) {
            const [customer] = parseCustomers({ items: [await response.json()] });
            if (customer) start = { ...start, customerMode: "existing", pickedCustomer: customer };
          }
        } catch { /* the customer can still be searched for */ }
      }
      if (!current) return;
      setProjects(list);
      baseline.current = start;
      setDraft(start);
      setReady(true);
    })();
    return () => { current = false; };
  }, [access, launchUnit, launchCustomer]);

  const dirty = isDirty(draft, baseline.current);
  // Closing a tab or reloading with something typed asks first, like the in-page close does.
  useEffect(() => {
    if (!dirty || created) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty, created]);

  const errors = useMemo(() => (attempted.has(step) ? stepErrors(step, draft) : {}), [attempted, step, draft]);
  const leave = useCallback(() => navigate("/confirmed-bookings"), [navigate]);
  const requestClose = () => { if (dirty && !created) setConfirmDiscard(true); else leave(); };

  const change = (next: BookingDraft) => { setDraft(next); setError(null); };

  const goContinue = () => {
    if (Object.keys(stepErrors(step, draft)).length > 0) {
      setAttempted((current) => new Set(current).add(step));
      return;
    }
    setStep((current) => Math.min(4, current + 1) as StepIndex);
  };

  const submit = async () => {
    if (saving) return;
    const bad = firstStepWithErrors(draft);
    if (bad !== null) {
      setAttempted((current) => new Set(current).add(bad));
      setStep(bad);
      return;
    }
    setSaving(true);
    setError(null);
    const payload = bookingPayload(draft);
    // The same details keep the same key, so pressing Create booking again after a dropped connection is
    // recognised as the retry it is rather than making a second booking.
    const signature = JSON.stringify(payload);
    try {
      const response = await api("/api/Booking", moneyRequest(keys.key(signature, "new-booking"), { method: "POST", body: JSON.stringify(payload) }));
      const data = await response.json().catch(() => null) as { id?: number; bookingReference?: string; recordedPaymentId?: number | null; customerName?: string; message?: string } | null;
      if (!response.ok || !data?.id) {
        setError(data?.message ?? "The booking could not be created. Check the details and try again.");
        setSaving(false);
        return;
      }
      keys.release(signature);
      toast.success(`Booking ${data.bookingReference} created.`);
      const paymentId = data.recordedPaymentId ?? null;
      // A failed proof never undoes the booking or the payment: it is attached later from Payments.
      if (paymentId !== null && proof.hasFile && !(await proof.upload("CustomerPayment", paymentId))) {
        toast.error("The booking and payment were saved, but the proof did not upload. Attach it from the booking's Payments tab.");
      }
      setCreated({
        id: data.id,
        reference: data.bookingReference ?? "",
        paymentId,
        unitNumber: draft.unit!.unitNumber,
        customerName: data.customerName ?? (draft.customerMode === "existing" ? draft.pickedCustomer!.fullName : draft.fullName.trim()),
      });
    } catch {
      setError("Something went wrong reaching the server. Check your connection and try again: the same booking will not be created twice.");
    } finally {
      setSaving(false);
    }
  };

  if (access === "wait") return null;
  if (access === "deny") {
    return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">You do not have access to bookings.</p></Container>;
  }
  if (!ready) return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">Loading...</p></Container>;

  if (created) {
    return (
      <DoneCard
        reference={created.reference}
        unitNumber={created.unitNumber}
        customerName={created.customerName}
        paymentId={created.paymentId}
        onPrintForm={() => navigate(`/application-form?bookingId=${created.id}`)}
        onPrintReceipt={() => navigate(`/receipt/${created.id}/${created.paymentId}`)}
        onOpen={() => navigate(`/confirmed-bookings/${created.id}`)}
      />
    );
  }

  const body = (
    <div className="flex flex-col gap-4">
      {launchNotice && step === 0 && <Notice tone="orange" role="status" title={launchNotice} />}
      {error && <Notice tone="red" role="alert" title={error} />}
      {step === 0 && <UnitStep draft={draft} errors={errors} projects={projects} onChange={change} />}
      {step === 1 && <CustomerStep draft={draft} errors={errors} onChange={change} />}
      {step === 2 && <KinStep draft={draft} errors={errors} onChange={change} />}
      {step === 3 && <PriceStep draft={draft} errors={errors} financeAccounts={accounts} accountsError={accountsError} proof={proof.fieldProps} onChange={change} />}
      {step === 4 && <ReviewStep draft={draft} financeAccounts={accounts} onEdit={(target) => { setStep(target); setError(null); }} />}
    </div>
  );

  return (
    <>
      <StepsFrame
        step={step}
        summary={draftSummary(draft)}
        onBack={() => (step === 0 ? requestClose() : setStep((current) => (current - 1) as StepIndex))}
        onContinue={step === 4 ? () => void submit() : goContinue}
        onClose={requestClose}
        continueLabel={step === 4 ? "Create booking" : "Continue"}
        continueDisabled={step === 0 && !draft.unit}
        saving={saving}
      >
        {body}
      </StepsFrame>
      {confirmDiscard && (
        <Modal
          open
          onClose={() => setConfirmDiscard(false)}
          size="sm"
          title="Discard this booking?"
          cancelLabel="Keep editing"
          primaryAction={{ label: "Discard", variant: "danger", onClick: leave }}
        >
          <p className="m-0">What you have entered will be lost.</p>
        </Modal>
      )}
    </>
  );
}
