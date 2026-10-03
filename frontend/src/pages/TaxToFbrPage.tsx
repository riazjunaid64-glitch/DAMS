import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { User } from "../App.tsx";
import { Button, ConfirmDialog, FilterBar, IconDownload, IconPlus, PageHeader, Tabs, useIsPhone, useToast, type FilterValues } from "../components/ui";
import { pageAccess } from "../features/access/permissions.ts";
import { DepositDialog } from "../features/finance/taxFbr/DepositDialog.tsx";
import { DepositsList } from "../features/finance/taxFbr/DepositsList.tsx";
import { clampPage, deleteMessage, deleteTitle, PAGE_SIZE } from "../features/finance/taxFbr/rules.ts";
import { PeriodLine, SummaryCards } from "../features/finance/taxFbr/Summary.tsx";
import { SuppliersList } from "../features/finance/taxFbr/SuppliersList.tsx";
import { useCashAccounts, useRangeData } from "../features/finance/taxFbr/useTaxFbrData.ts";
import * as whtApi from "../features/finance/whtApi.ts";
import type { WhtDeposit } from "../features/finance/whtTypes.ts";
import { usePageTrail } from "../layouts/trail.ts";

type TabId = "deposits" | "suppliers";
type Dialog = { kind: "deposit"; deposit: WhtDeposit | null } | { kind: "delete"; deposit: WhtDeposit };

const page = "mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7";
const FAILED = "The withholding position could not be loaded.";
const FILTERS = [{ type: "dateRange", fromKey: "from", toKey: "to" }] as const;

/**
 * Finance → Tax to FBR: the tax kept back from suppliers and what has been paid over. The four cards
 * are the whole sum and ignore From / To; the dates narrow the two lists, the period line and Export.
 */
export default function TaxToFbrPage({ user }: { user: User | null }) {
  const access = pageAccess(user?.role, "finance");
  const allowed = access === "allow";
  const navigate = useNavigate();
  const isPhone = useIsPhone();
  const toast = useToast();
  const [tab, setTab] = useState<TabId>("deposits");
  const [range, setRange] = useState({ from: "", to: "" });
  const [pages, setPages] = useState({ deposits: 1, suppliers: 1 });
  const [shown, setShown] = useState({ deposits: PAGE_SIZE, suppliers: PAGE_SIZE });
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [exporting, setExporting] = useState(false);
  const { from, to } = range;
  const ranged = from !== "" || to !== "";
  const key = `${from}|${to}`;

  usePageTrail([{ label: "Tax to FBR" }]);

  // The cards and the chosen tab load now; the other list loads when its tab opens. The supplier
  // list is also needed under a date range, because the period line counts its rows.
  const summary = useRangeData(whtApi.payableSummary, from, to, allowed, FAILED);
  const deposits = useRangeData(whtApi.listDeposits, from, to, allowed && tab === "deposits", FAILED);
  const suppliers = useRangeData(whtApi.byVendor, from, to, allowed && (tab === "suppliers" || ranged), FAILED);
  const cash = useCashAccounts(allowed);

  useEffect(() => {
    if (access === "deny") navigate("/");
  }, [access, navigate]);

  const changeRange = useCallback((changes: FilterValues) => {
    setRange((current) => ({ from: changes.from ?? current.from, to: changes.to ?? current.to }));
    setPages({ deposits: 1, suppliers: 1 });
    setShown({ deposits: PAGE_SIZE, suppliers: PAGE_SIZE });
  }, []);
  const resetRange = useCallback(() => changeRange({ from: "", to: "" }), [changeRange]);

  // A delete can empty the last page. The list already shows the new last page; storing it as well
  // means the list growing again later does not jump back to a page the user left.
  const depositPage = deposits.data ? clampPage(pages.deposits, deposits.data.length) : pages.deposits;
  const supplierPage = suppliers.data ? clampPage(pages.suppliers, suppliers.data.length) : pages.suppliers;
  if (depositPage !== pages.deposits || supplierPage !== pages.suppliers) setPages({ deposits: depositPage, suppliers: supplierPage });

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
  // A deposit moves the cards and the deposits list; the supplier statement does not depend on it.
  const afterChange = () => {
    summary.reload();
    deposits.reload();
  };
  const openDeposit = (deposit: WhtDeposit | null) => {
    if (cash.error) cash.reload();
    setDialog({ kind: "deposit", deposit });
  };

  const exportStatement = async () => {
    setExporting(true);
    try {
      await whtApi.downloadWhtStatement(from, to);
    } catch {
      toast.error("The withholding statement could not be exported.");
    } finally {
      setExporting(false);
    }
  };

  const deleteDeposit = async (deposit: WhtDeposit) => {
    setDeleting(true);
    try {
      await whtApi.deleteDeposit(deposit.id, deposit.concurrencyToken);
      toast.success("Deposit deleted.");
    } catch (failure) {
      toast.error(failure instanceof Error ? failure.message : "The deposit could not be deleted.");
    } finally {
      setDeleting(false);
      close();
      afterChange();
    }
  };

  const summaryState = summary.data ? "ready" : summary.error ? "error" : "loading";
  // The line is one sentence about this range: the figures and the supplier rows it counts. An answer
  // for an earlier range is not shown under this one's name, and without the supplier list there is no
  // honest count, so the line waits for it (and stays away if it fails; that list shows its own error).
  const supplierCount = !suppliers.loading && suppliers.data !== null && suppliers.dataKey === key ? suppliers.data.length : null;
  const showLine = ranged && summary.data !== null && summary.dataKey === key && supplierCount !== null;
  // A new deposit opens on what is owed, so it waits for that figure. If the figure failed there is
  // nothing to wait for: the deposit can still be recorded, and the popup then opens with no amount.
  const awaitingOwed = summary.data === null && summary.loading;

  return (
    <div className={page}>
      <PageHeader
        title="Tax to FBR"
        className="max-md:flex-row max-md:items-center max-md:justify-between"
        actions={isPhone ? (
          <>
            <Button icon={<IconPlus size={16} />} disabled={awaitingOwed} onClick={() => openDeposit(null)}>Record deposit</Button>
            <Button variant="outline" iconOnly icon={<IconDownload size={18} />} aria-label="Export" loading={exporting} onClick={() => void exportStatement()} />
          </>
        ) : (
          <>
            <Button variant="outline" icon={<IconDownload size={16} />} loading={exporting} onClick={() => void exportStatement()}>Export</Button>
            <Button icon={<IconPlus size={16} />} disabled={awaitingOwed} onClick={() => openDeposit(null)}>Record deposit</Button>
          </>
        )}
      />

      <SummaryCards summary={summary.data} state={summaryState} error={summary.error} onRetry={summary.reload} />
      {showLine && summary.data && supplierCount !== null && <PeriodLine summary={summary.data} supplierCount={supplierCount} />}

      <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between md:gap-4">
        <Tabs
          items={[
            { id: "deposits", label: "Deposits to FBR" },
            { id: "suppliers", label: isPhone ? "By supplier" : "Withheld by supplier" },
          ]}
          value={tab}
          onChange={(id) => setTab(id as TabId)}
          aria-label="Tax to FBR lists"
        />
        <FilterBar filters={FILTERS} values={range} onChange={changeRange} onReset={resetRange} className="md:max-w-[460px] md:flex-1" />
      </div>

      {tab === "deposits" ? (
        <DepositsList
          rows={deposits.data}
          loading={deposits.loading}
          error={deposits.error}
          ranged={ranged}
          isPhone={isPhone}
          page={pages.deposits}
          shown={shown.deposits}
          onPage={(next) => setPages((current) => ({ ...current, deposits: next }))}
          onLoadMore={() => setShown((current) => ({ ...current, deposits: current.deposits + PAGE_SIZE }))}
          onEdit={openDeposit}
          onDelete={(deposit) => setDialog({ kind: "delete", deposit })}
          onRetry={deposits.reload}
        />
      ) : (
        <SuppliersList
          rows={suppliers.data}
          loading={suppliers.loading}
          error={suppliers.error}
          ranged={ranged}
          isPhone={isPhone}
          page={pages.suppliers}
          shown={shown.suppliers}
          onPage={(next) => setPages((current) => ({ ...current, suppliers: next }))}
          onLoadMore={() => setShown((current) => ({ ...current, suppliers: current.suppliers + PAGE_SIZE }))}
          onRetry={suppliers.reload}
        />
      )}

      {dialog?.kind === "deposit" && (
        <DepositDialog
          deposit={dialog.deposit}
          owed={summary.data?.outstandingPayable ?? 0}
          accounts={cash.accounts}
          accountsError={cash.error}
          onClose={close}
          onSaved={afterChange}
        />
      )}
      {dialog?.kind === "delete" && (
        <ConfirmDialog
          open
          onClose={() => !deleting && close()}
          onConfirm={() => void deleteDeposit(dialog.deposit)}
          title={deleteTitle(dialog.deposit)}
          message={deleteMessage(dialog.deposit)}
          confirmLabel="Delete"
          danger
          loading={deleting}
        />
      )}
    </div>
  );
}
