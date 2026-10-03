import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { User } from "../App.tsx";
import { Button, ConfirmDialog, FilterBar, IconPlus, Notice, PageHeader, useToast, type FilterDef } from "../components/ui";
import { pageAccess } from "../features/access/permissions.ts";
import { AccountDetailsDialog } from "../features/finance/accounts/AccountDetailsDialog.tsx";
import { AccountFormDialog } from "../features/finance/accounts/AccountFormDialog.tsx";
import { AccountsTable } from "../features/finance/accounts/AccountsTable.tsx";
import { TYPE_OPTIONS, openingMismatch, type Account } from "../features/finance/accounts/accountGroups.ts";
import { accountsApi } from "../features/finance/accounts/api.ts";
import { useAccountsData } from "../features/finance/accounts/useAccountsData.ts";
import { usePageTrail } from "../layouts/trail.ts";

type Dialog =
  | { kind: "details"; account: Account }
  | { kind: "form"; account: Account | null }
  | { kind: "status"; account: Account };

const NO_FILTERS = { search: "", status: "", type: "", holder: "" };
const STATUS_OPTIONS = [{ value: "active", label: "Active" }, { value: "inactive", label: "Inactive" }];

/** Finance → Manage accounts: the chart of accounts, with the opening balance typed once, on the account. */
export default function FinanceAccountsPage({ user }: { user: User | null }) {
  usePageTrail([{ label: "Manage accounts" }]);
  const access = pageAccess(user?.role, "finance");
  const allowed = access === "allow";
  const navigate = useNavigate();
  const toast = useToast();
  const [filters, setFilters] = useState(NO_FILTERS);
  const data = useAccountsData(allowed, { search: filters.search, status: filters.status || "all", typeFilter: filters.type, holderFilter: filters.holder });
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [changing, setChanging] = useState(false);

  useEffect(() => {
    if (access === "deny") navigate("/");
  }, [access, navigate]);

  const filterDefs = useMemo<FilterDef[]>(() => [
    { type: "select", key: "type", label: "Type", options: TYPE_OPTIONS },
    { type: "select", key: "holder", label: "Holder", options: (data.overview?.holderNames ?? []).map((name) => ({ value: name, label: name })) },
    { type: "select", key: "status", label: "Status", options: STATUS_OPTIONS },
  ], [data.overview?.holderNames]);

  if (!allowed) {
    return access === "deny" ? null : (
      <div role="status" aria-busy="true" className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:px-8 md:py-7">
        <span className="sr-only">Loading</span>
        <span aria-hidden="true" className="h-9 w-64 animate-pulse rounded bg-track" />
        <span aria-hidden="true" className="h-64 animate-pulse rounded-card bg-track" />
      </div>
    );
  }

  const goLiveDate = data.overview?.goLiveDate ?? null;
  const mismatch = !data.loading && !data.error && data.overview ? openingMismatch(data.overview) : null;
  const failedEmpty = data.error !== null && data.accounts.length === 0;
  const close = () => setDialog(null);

  const changeStatus = async (account: Account) => {
    setChanging(true);
    try {
      await accountsApi.setActive(account.id, !account.isActive, account.concurrencyToken);
      toast.success(`${account.name} ${account.isActive ? "deactivated" : "reactivated"}.`);
      close();
      data.reload();
    } catch (failure) {
      // A refusal (a system account, a linked loan, a staff float that is not at zero) is a toast.
      toast.error(failure instanceof Error ? failure.message : "The status could not be changed.");
      close();
    } finally {
      setChanging(false);
    }
  };

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      <PageHeader
        title="Manage accounts"
        className="max-md:flex-row max-md:items-start max-md:justify-between"
        actions={<Button icon={<IconPlus size={16} />} onClick={() => setDialog({ kind: "form", account: null })}>Add account</Button>}
      />

      {mismatch && <Notice tone="red" role="alert" title={mismatch} />}
      {data.hasMore && <Notice tone="gold" title="Showing the first 200 accounts. Narrow the search to see the rest." />}

      <FilterBar
        search={{ value: filters.search, onSearch: (search) => setFilters((current) => ({ ...current, search })), placeholder: "Account, holder, bank or wallet", debounceMs: 250 }}
        filters={filterDefs}
        values={{ type: filters.type, holder: filters.holder, status: filters.status }}
        onChange={(changes) => setFilters((current) => ({ ...current, ...changes }))}
        onReset={() => setFilters(NO_FILTERS)}
      />

      {data.error && (
        <Notice tone="red" role="alert" title={data.error} action={<Button variant="outline" onClick={data.reload}>Try again</Button>} />
      )}
      {!failedEmpty && (
        <AccountsTable
          accounts={data.accounts}
          loading={data.loading}
          onOpen={(account) => setDialog({ kind: "details", account })}
          onEdit={(account) => setDialog({ kind: "form", account })}
          onToggleActive={(account) => setDialog({ kind: "status", account })}
        />
      )}

      {dialog?.kind === "form" && (
        <AccountFormDialog key={dialog.account?.id ?? "new"} account={dialog.account} goLiveDate={goLiveDate} onClose={close} onSaved={data.reload} />
      )}
      {dialog?.kind === "details" && (
        <AccountDetailsDialog
          account={dialog.account}
          onClose={close}
          onEdit={(account) => setDialog({ kind: "form", account })}
          onToggleActive={(account) => setDialog({ kind: "status", account })}
        />
      )}
      {dialog?.kind === "status" && (
        <ConfirmDialog
          open
          onClose={() => !changing && close()}
          onConfirm={() => void changeStatus(dialog.account)}
          title={dialog.account.isActive ? `Deactivate ${dialog.account.name}?` : `Reactivate ${dialog.account.name}?`}
          message={dialog.account.isActive ? "Past transactions are kept. It can't be picked for new entries." : "It can be picked for new entries again."}
          confirmLabel={dialog.account.isActive ? "Deactivate" : "Reactivate"}
          danger={dialog.account.isActive}
          loading={changing}
        />
      )}
    </div>
  );
}
