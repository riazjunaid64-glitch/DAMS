import { useCallback, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import type { User } from "../App.tsx";
import { Button, ConfirmDialog, EmptyState, IconPlus, IconUsers, Notice, PageHeader, StatCard, useIsPhone, useToast } from "../components/ui";
import { useProjects } from "../contexts/projectsContextValue.ts";
import { pageAccess } from "../features/access/permissions.ts";
import { AddPersonDialog } from "../features/finance/staffCash/AddPersonDialog.tsx";
import { staffCashApi } from "../features/finance/staffCash/api.ts";
import { ExpenseDialog } from "../features/finance/staffCash/ExpenseDialog.tsx";
import { MovementDialog } from "../features/finance/staffCash/MovementDialog.tsx";
import { Movements, type MovementsList } from "../features/finance/staffCash/Movements.tsx";
import { PeopleList } from "../features/finance/staffCash/PeopleList.tsx";
import { PersonPanel } from "../features/finance/staffCash/PersonPanel.tsx";
import { peopleNote, rupees, type MovementIntent } from "../features/finance/staffCash/rules.ts";
import type { HistoryItem, Holder } from "../features/finance/staffCash/types.ts";
import { useStaffCashData } from "../features/finance/staffCash/useStaffCashData.ts";
import { usePageTrail } from "../layouts/trail.ts";
import { useIdempotencyKeys } from "../lib/idempotency.ts";
import { usePagedList, type PagedListQuery } from "../lib/usePagedList.ts";

type Dialog =
  | { kind: "add" }
  | { kind: "expense"; holder: Holder }
  | { kind: "movement"; holder: Holder; intent: MovementIntent; movement: HistoryItem | null }
  | { kind: "delete"; holder: Holder; movement: HistoryItem };

const page = "mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7";
const ROOT = "/finance/staff-cash";

// A transfer and an expense can share an id, so the kind is part of the key.
const movementKey = (row: HistoryItem) => `${row.recordType}-${row.recordId}`;

/**
 * Finance → Cash with staff: company money that has left the safe or the bank and is in a staff
 * member's hands. A desktop shows the people beside the open person and their movements; a phone
 * shows the people, and each person on their own page (/finance/staff-cash/:accountId).
 */
export default function StaffCashPage({ user }: { user: User | null }) {
  const access = pageAccess(user?.role, "finance");
  const allowed = access === "allow";
  const navigate = useNavigate();
  const isPhone = useIsPhone();
  const toast = useToast();
  const { accountId } = useParams();
  const keys = useIdempotencyKeys();
  const { projects } = useProjects();
  const data = useStaffCashData(allowed);
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [deleting, setDeleting] = useState(false);

  // The open person is read from the overview, which is refetched after every save, so the person
  // named on the panel and the person every button posts to are the same row.
  const holders = data.overview?.holders ?? [];
  const requestedId = accountId === undefined ? null : Number(accountId);
  const selected = requestedId === null ? null : holders.find((holder) => holder.financeAccountId === requestedId) ?? null;
  const personId = selected?.financeAccountId ?? null;
  const personPage = isPhone && requestedId !== null;

  usePageTrail(personPage
    ? [{ label: "Cash with staff", to: ROOT }, { label: selected?.personName ?? "Person" }]
    : [{ label: "Cash with staff" }]);

  // Another person is another list: it starts at page one, and an answer for the previous person
  // is dropped rather than shown under this one's name.
  const queryKey = String(personId ?? "none");
  const fetchPage = useCallback(async ({ skip, take, signal }: PagedListQuery) => {
    if (personId === null) return { items: [] as HistoryItem[], totalCount: 0 };
    const statement = await staffCashApi.statement(personId, skip, take, signal);
    return { items: statement.items, totalCount: statement.totalCount, hasMore: statement.hasMore };
  }, [personId]);
  const paged = usePagedList<HistoryItem>({ queryKey, fetchPage, itemKey: movementKey });
  const mine = paged.rowsKey === queryKey;
  const movements: MovementsList = {
    rows: mine ? paged.rows : [],
    total: mine ? paged.total ?? 0 : 0,
    page: paged.page,
    pageSize: paged.pagination.pageSize,
    loading: paged.loading,
    loadingMore: paged.loadingMore,
    error: paged.error,
    setPage: paged.setPage,
    loadMore: paged.loadMore,
    reload: paged.reload,
  };

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
  const open = (holder: Holder) => navigate(`${ROOT}/${holder.financeAccountId}`, { replace: !isPhone });
  const addPerson = () => setDialog({ kind: "add" });
  const afterChange = () => {
    data.reload();
    paged.reload();
  };

  const openAttachment = (holder: Holder, row: HistoryItem, download: boolean) => {
    staffCashApi.openAttachment(holder.financeAccountId, row, download).catch((failure: unknown) => {
      toast.error(failure instanceof Error ? failure.message : "The attachment could not be opened.");
    });
  };

  const deleteMovement = async (holder: Holder, row: HistoryItem) => {
    setDeleting(true);
    try {
      await staffCashApi.deleteMovement(holder.financeAccountId, row);
      toast.success("Movement deleted.");
      data.reload();
      paged.afterDelete();
    } catch (failure) {
      toast.error(failure instanceof Error ? failure.message : "Could not delete this movement.");
    } finally {
      setDeleting(false);
      close();
    }
  };

  const overview = data.overview;
  const failedEmpty = data.error !== null && overview === null;
  const firstLoad = data.loading && overview === null;
  const statState = overview ? "ready" : failedEmpty ? "error" : "loading";

  const stats = (
    <div className="grid gap-2.5 md:grid-cols-3 md:gap-4">
      <StatCard label="Held by staff" tone="green" state={statState} value={rupees(overview?.totalHeldByStaff ?? 0)} note={peopleNote(overview?.holdingCount ?? 0)} />
      <StatCard label="Company owes staff" tone="red" state={statState} value={rupees(overview?.totalOwedToStaff ?? 0)} note={peopleNote(overview?.owedCount ?? 0)} />
      <StatCard label="Net staff balance" state={statState} value={rupees(overview?.netStaffBalance ?? 0)} note="Held less owed" />
    </div>
  );

  const people = firstLoad ? (
    <div aria-hidden="true" className="flex flex-col gap-2.5">
      <span className="h-[76px] animate-pulse rounded-card bg-track" />
      <span className="h-[76px] animate-pulse rounded-card bg-track" />
      <span className="h-[76px] animate-pulse rounded-card bg-track" />
    </div>
  ) : holders.length === 0 ? (
    <EmptyState
      icon={<IconUsers size={26} />}
      title="No staff floats yet"
      message="Add a person, then record the money handed to them."
      action={<Button icon={<IconPlus size={16} />} onClick={addPerson}>Add person</Button>}
    />
  ) : (
    <PeopleList holders={holders} selectedId={isPhone ? null : personId} onOpen={open} />
  );

  // Wait for a reload before calling a person missing: one just added is not in the old list yet.
  const missing = requestedId !== null && !data.loading && overview !== null && selected === null;
  const detail = selected ? (
    <>
      <PersonPanel
        holder={selected}
        isPhone={isPhone}
        onExpense={() => setDialog({ kind: "expense", holder: selected })}
        onMovement={(intent) => setDialog({ kind: "movement", holder: selected, intent, movement: null })}
      />
      <Movements
        holder={selected}
        list={movements}
        isPhone={isPhone}
        onCorrect={(movement) => setDialog({ kind: "movement", holder: selected, intent: "give", movement })}
        onDelete={(movement) => setDialog({ kind: "delete", holder: selected, movement })}
        onOpenAttachment={(row, download) => openAttachment(selected, row, download)}
      />
    </>
  ) : missing ? (
    <EmptyState
      icon={<IconUsers size={26} />}
      title="Person not found"
      message="They may have been removed. Choose someone from the list."
      action={<Button variant="outline" onClick={() => navigate(ROOT)}>Back to Cash with staff</Button>}
    />
  ) : requestedId !== null && data.loading ? (
    <span aria-hidden="true" className="h-64 animate-pulse rounded-card bg-track" />
  ) : (
    <EmptyState icon={<IconUsers size={26} />} title="Select a person to see their movements." />
  );

  return (
    <div className={page}>
      {!personPage && (
        <PageHeader
          title="Cash with staff"
          className="max-md:flex-row max-md:items-center max-md:justify-between"
          actions={<Button icon={<IconPlus size={16} />} onClick={addPerson}>Add person</Button>}
        />
      )}
      {data.error && (
        <Notice tone="red" role="alert" title={data.error} action={<Button variant="outline" onClick={data.reload}>Try again</Button>} />
      )}
      {personPage ? (!failedEmpty && detail) : (
        <>
          {stats}
          {!failedEmpty && (isPhone ? people : (
            <div className="grid items-start gap-4 lg:grid-cols-[minmax(280px,380px)_minmax(0,1fr)] lg:gap-5">
              {people}
              <div className="flex min-w-0 flex-col gap-4 md:gap-5">{detail}</div>
            </div>
          ))}
        </>
      )}

      {dialog?.kind === "add" && (
        <AddPersonDialog
          onClose={close}
          onAdded={(holder) => {
            data.reload();
            open(holder);
          }}
        />
      )}
      {dialog?.kind === "expense" && (
        <ExpenseDialog
          holder={dialog.holder}
          categories={data.categories}
          vendors={data.vendors}
          projects={projects}
          lookupsError={data.lookupsError}
          keys={keys}
          onClose={close}
          onSaved={afterChange}
        />
      )}
      {dialog?.kind === "movement" && (
        <MovementDialog
          key={dialog.movement?.recordId ?? dialog.intent}
          holder={dialog.holder}
          intent={dialog.intent}
          movement={dialog.movement}
          cashAccounts={data.cashAccounts}
          accountsError={data.cashAccountsError}
          keys={keys}
          onClose={close}
          onSaved={afterChange}
          onOpenAttachment={(movement, download) => openAttachment(dialog.holder, movement, download)}
        />
      )}
      {dialog?.kind === "delete" && (
        <ConfirmDialog
          open
          onClose={() => !deleting && close()}
          onConfirm={() => void deleteMovement(dialog.holder, dialog.movement)}
          title="Delete this movement?"
          message="The account balances will be worked out again."
          confirmLabel="Delete"
          danger
          loading={deleting}
        />
      )}
    </div>
  );
}
