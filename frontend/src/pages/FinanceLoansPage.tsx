import { useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import type { User } from "../App.tsx";
import { Button, ConfirmDialog, Dropdown, EmptyState, IconPlus, IconWallet, Notice, PageHeader, SearchBar, useIsPhone, useToast } from "../components/ui";
import { pageAccess } from "../features/access/permissions.ts";
import { loansApi } from "../features/finance/loans/api.ts";
import { LoanActivity } from "../features/finance/loans/LoanActivity.tsx";
import { LoanCard } from "../features/finance/loans/LoanCard.tsx";
import { LoanFormDialog } from "../features/finance/loans/LoanFormDialog.tsx";
import { LoanSummary } from "../features/finance/loans/LoanSummary.tsx";
import { MovementDialog } from "../features/finance/loans/MovementDialog.tsx";
import { activityCsv, activityFileName, filterLoans, saveCsv, type StatusFilter } from "../features/finance/loans/rules.ts";
import type { Loan, LoanTransaction, MovementType } from "../features/finance/loans/types.ts";
import { useLoanActivity } from "../features/finance/loans/useLoanActivity.ts";
import { useLoansData } from "../features/finance/loans/useLoansData.ts";
import { usePageTrail } from "../layouts/trail.ts";
import { useIdempotencyKeys } from "../lib/idempotency.ts";

type Dialog =
  | { kind: "loan"; loan: Loan | null }
  | { kind: "movement"; loan: Loan; type: MovementType; movement: LoanTransaction | null }
  | { kind: "delete"; loan: Loan; movement: LoanTransaction };

const STATUS_OPTIONS = [
  { value: "all", label: "All" },
  { value: "active", label: "Active" },
  { value: "closed", label: "Closed" },
];

const page = "mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7";

/**
 * Finance → Loans. A desktop shows the loans as cards with the open loan and its activity under them;
 * a phone shows the list, and each loan on its own page (/finance/loans/:loanId) with "‹ Loans" back.
 */
export default function FinanceLoansPage({ user }: { user: User | null }) {
  const access = pageAccess(user?.role, "finance");
  const allowed = access === "allow";
  const navigate = useNavigate();
  const isPhone = useIsPhone();
  const toast = useToast();
  const { loanId: loanParam } = useParams();
  const keys = useIdempotencyKeys();
  const data = useLoansData(allowed);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<StatusFilter>("all");
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [exporting, setExporting] = useState(false);

  // The open loan is read from the list, which is refetched after every save, so the loan on screen,
  // the loan its buttons act on and the row version an edit sends are the same row. With no loan in
  // the address a desktop opens the first one; a phone shows the list. A popup keeps the loan it was
  // opened for, so a reload that reorders the list can never point it at another loan.
  const requestedId = loanParam === undefined ? null : Number(loanParam);
  const selected = requestedId !== null
    ? data.loans.find((loan) => loan.id === requestedId) ?? null
    : isPhone ? null : data.loans[0] ?? null;
  const activity = useLoanActivity(selected?.id ?? null, isPhone ? "more" : "pages");
  const visible = useMemo(() => filterLoans(data.loans, search, status), [data.loans, search, status]);
  const phoneLoanPage = isPhone && requestedId !== null;

  usePageTrail(phoneLoanPage
    ? [{ label: "Loans", to: "/finance/loans" }, { label: selected?.name ?? "Loan" }]
    : [{ label: "Loans" }]);

  useEffect(() => {
    if (access === "deny") navigate("/");
  }, [access, navigate]);

  if (!allowed) {
    return access === "deny" ? null : (
      <div role="status" aria-busy="true" className={page}>
        <span className="sr-only">Loading</span>
        <span aria-hidden="true" className="h-9 w-64 animate-pulse rounded bg-track" />
        <span aria-hidden="true" className="h-64 animate-pulse rounded-card bg-track" />
      </div>
    );
  }

  const close = () => setDialog(null);
  const open = (loan: Loan) => navigate(`/finance/loans/${loan.id}`, { replace: !isPhone });
  const addLoan = () => setDialog({ kind: "loan", loan: null });
  const afterMovement = () => {
    data.reload();
    activity.reload();
  };

  const openAttachment = (loan: Loan, movement: LoanTransaction, download: boolean) => {
    loansApi.openAttachment(loan.id, movement, download).catch((failure: unknown) => {
      toast.error(failure instanceof Error ? failure.message : "The attachment could not be opened.");
    });
  };

  const exportActivity = async () => {
    if (!selected || exporting) return;
    setExporting(true);
    try {
      saveCsv(activityCsv(await loansApi.allMovements(selected.id)), activityFileName(selected.name));
    } catch (failure) {
      toast.error(failure instanceof Error ? failure.message : "The activity could not be exported.");
    } finally {
      setExporting(false);
    }
  };

  const deleteMovement = async (loan: Loan, movement: LoanTransaction) => {
    setDeleting(true);
    try {
      await loansApi.deleteMovement(loan.id, movement);
      toast.success(movement.type === "Drawdown" ? "Loan funds deleted." : "Repayment deleted.");
      afterMovement();
    } catch (failure) {
      toast.error(failure instanceof Error ? failure.message : "The loan movement could not be deleted.");
    } finally {
      setDeleting(false);
      close();
    }
  };

  const failedEmpty = data.error !== null && data.loans.length === 0;
  const firstLoad = data.loading && data.loans.length === 0;

  const list = (
    <>
      <div className="flex gap-2.5">
        <SearchBar value={search} onSearch={setSearch} placeholder="Loan, lender or account" debounceMs={200} className="min-w-0 flex-1 md:max-w-[320px]" size={isPhone ? "lg" : "md"} />
        <Dropdown size="filter" label="Status" options={STATUS_OPTIONS} value={status} onChange={(value) => setStatus(value as StatusFilter)} className="shrink-0" />
      </div>
      {firstLoad ? (
        <div aria-hidden="true" className="grid gap-3 md:grid-cols-[repeat(auto-fill,minmax(300px,1fr))]">
          <span className="h-[106px] animate-pulse rounded-card bg-track" />
          <span className="h-[106px] animate-pulse rounded-card bg-track" />
        </div>
      ) : data.loans.length === 0 ? (
        <EmptyState
          icon={<IconWallet size={26} />}
          title="No loans yet"
          message="Add a loan and link it to the Liability account that carries what is owed."
          action={<Button icon={<IconPlus size={16} />} onClick={addLoan}>Add loan</Button>}
        />
      ) : visible.length === 0 ? (
        <p className="m-0 rounded-card border border-dashed border-line bg-card p-6 text-center text-small text-ink-muted">No loan matches this search or status.</p>
      ) : (
        <div className="grid gap-3 md:grid-cols-[repeat(auto-fill,minmax(300px,1fr))]">
          {visible.map((loan) => <LoanCard key={loan.id} loan={loan} selected={!isPhone && loan.id === selected?.id} onOpen={() => open(loan)} />)}
        </div>
      )}
    </>
  );

  const missing = requestedId !== null && !data.loading && !data.error && selected === null;
  const detail = selected ? (
    <>
      <LoanSummary
        loan={selected}
        isPhone={isPhone}
        canExport={activity.total > 0}
        exporting={exporting}
        onEdit={() => setDialog({ kind: "loan", loan: selected })}
        onRepay={() => setDialog({ kind: "movement", loan: selected, type: "Repayment", movement: null })}
        onReceive={() => setDialog({ kind: "movement", loan: selected, type: "Drawdown", movement: null })}
        onExport={() => void exportActivity()}
      />
      <LoanActivity
        activity={activity}
        isPhone={isPhone}
        exporting={exporting}
        onExport={() => void exportActivity()}
        onCorrect={(movement) => setDialog({ kind: "movement", loan: selected, type: movement.type, movement })}
        onDelete={(movement) => setDialog({ kind: "delete", loan: selected, movement })}
        onOpenAttachment={(movement, download) => openAttachment(selected, movement, download)}
      />
    </>
  ) : missing ? (
    <EmptyState
      icon={<IconWallet size={26} />}
      title="Loan not found"
      message="It may have been removed. Open another loan from the list."
      action={<Button variant="outline" onClick={() => navigate("/finance/loans")}>Back to loans</Button>}
    />
  ) : firstLoad && phoneLoanPage ? (
    <span aria-hidden="true" className="h-64 animate-pulse rounded-card bg-track" />
  ) : null;

  return (
    <div className={page}>
      {!phoneLoanPage && (
        <PageHeader
          title="Loans"
          className="max-md:flex-row max-md:items-center max-md:justify-between"
          actions={<Button icon={<IconPlus size={16} />} onClick={addLoan}>Add loan</Button>}
        />
      )}
      {data.error && (
        <Notice tone="red" role="alert" title={data.error} action={<Button variant="outline" onClick={data.reload}>Try again</Button>} />
      )}
      {!failedEmpty && (phoneLoanPage ? detail : (
        <>
          {list}
          {!isPhone && detail}
        </>
      ))}

      {dialog?.kind === "loan" && (
        <LoanFormDialog
          key={dialog.loan?.id ?? "new"}
          loan={dialog.loan}
          accounts={data.loanAccounts}
          accountsError={data.loanAccountsError}
          onClose={close}
          onSaved={(saved) => {
            data.reload();
            // Pin the saved loan in the address: a rename or a status change can move it in the
            // list, and the open loan must not silently become whichever loan is now first.
            if (saved.id !== requestedId) open(saved);
          }}
        />
      )}
      {dialog?.kind === "movement" && (
        <MovementDialog
          key={dialog.movement?.id ?? dialog.type}
          loan={dialog.loan}
          movement={dialog.movement}
          type={dialog.type}
          cashAccounts={data.cashAccounts}
          accountsError={data.cashAccountsError}
          keys={keys}
          onClose={close}
          onSaved={afterMovement}
          onOpenAttachment={(movement, download) => openAttachment(dialog.loan, movement, download)}
        />
      )}
      {dialog?.kind === "delete" && (
        <ConfirmDialog
          open
          onClose={() => !deleting && close()}
          onConfirm={() => void deleteMovement(dialog.loan, dialog.movement)}
          title={dialog.movement.type === "Drawdown" ? "Delete these loan funds?" : "Delete this repayment?"}
          message="The loan and bank balances will be worked out again."
          confirmLabel="Delete"
          danger
          loading={deleting}
        />
      )}
    </div>
  );
}
