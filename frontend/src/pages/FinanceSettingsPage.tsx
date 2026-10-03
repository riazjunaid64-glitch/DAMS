import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { User } from "../App.tsx";
import { Button, EmptyState, IconPlus, IconSettings, Notice, PageHeader, Tabs, useIsPhone, useToast } from "../components/ui";
import { pageAccess } from "../features/access/permissions.ts";
import { ExpenseCategoriesTab } from "../features/finance/settings/ExpenseCategoriesTab.tsx";
import { FinancialYearTab } from "../features/finance/settings/FinancialYearTab.tsx";
import { RevenueCategoriesTab } from "../features/finance/settings/RevenueCategoriesTab.tsx";
import { useSettingsData } from "../features/finance/settings/useSettingsData.ts";
import { VendorsTab } from "../features/finance/settings/VendorsTab.tsx";
import * as whtApi from "../features/finance/whtApi.ts";
import { usePageTrail } from "../layouts/trail.ts";

type TabId = "expense" | "revenue" | "vendors" | "year";

const page = "mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7";

/** The header's button on each tab; the Financial year tab has none. */
const ADD_LABELS: Record<TabId, string | null> = { expense: "Add category", revenue: "Add category", vendors: "Add vendor", year: null };

/**
 * Finance → Settings: the lists the rest of Finance depends on. Expense categories (with their
 * withholding tax), revenue categories, vendors, and the financial year with the tax-rates check.
 */
export default function FinanceSettingsPage({ user }: { user: User | null }) {
  usePageTrail([{ label: "Settings" }]);
  const access = pageAccess(user?.role, "finance");
  const navigate = useNavigate();

  useEffect(() => {
    if (access === "deny") navigate("/");
  }, [access, navigate]);

  if (access !== "allow") {
    // While the user loads nothing is decided and nobody is sent away; only a known other role is.
    return (
      <div className={page}>
        <EmptyState
          icon={<IconSettings size={26} />}
          title={access === "deny" ? "Finance access required" : "Sign in required"}
          message={access === "deny" ? "Only an Admin or Accountant can change finance settings." : "Sign in with an Admin or Accountant account to manage finance settings."}
        />
      </div>
    );
  }
  return <SettingsWorkspace />;
}

function SettingsWorkspace() {
  const isPhone = useIsPhone();
  const toast = useToast();
  const data = useSettingsData();
  const [tab, setTab] = useState<TabId>("expense");
  const [adding, setAdding] = useState(false);
  const [checking, setChecking] = useState(false);

  const settings = data.data?.settings ?? null;
  const addLabel = ADD_LABELS[tab];
  const failedEmpty = data.error !== null && data.data === null;

  // "Mark as checked" and "Clear check" send the saved month, never a month picked but not saved in
  // the Financial year card, so one button cannot change the other setting by accident. No go-live
  // date is sent: null leaves it as it is.
  const changeCheck = async (checked: boolean) => {
    if (!settings || checking) return;
    setChecking(true);
    try {
      await whtApi.saveSettings({
        financialYearStartMonth: settings.financialYearStartMonth,
        goLiveDate: null,
        markRatesConfirmed: checked,
        clearRatesConfirmation: !checked,
        concurrencyToken: settings.concurrencyToken,
      });
      toast.success(checked ? "Tax rates marked as checked." : "Check cleared.");
      data.reload();
    } catch (failure) {
      toast.error(failure instanceof Error ? failure.message : "The tax rates check could not be saved.");
    } finally {
      setChecking(false);
    }
  };

  const changeTab = (id: string) => {
    setTab(id as TabId);
    setAdding(false);
  };

  return (
    <div className={page}>
      <PageHeader
        title="Finance settings"
        className="max-md:flex-row max-md:items-center max-md:justify-between"
        actions={addLabel && (
          // A category tab whose list could not be read is not on screen, so it could not open the popup.
          <Button icon={<IconPlus size={16} />} disabled={tab !== "vendors" && failedEmpty} onClick={() => setAdding(true)}>
            {isPhone ? "Add" : addLabel}
          </Button>
        )}
      />

      <Tabs
        items={[
          { id: "expense", label: "Expense categories", count: data.data?.categories.length },
          { id: "revenue", label: "Revenue categories", count: data.data?.revenueCategories.length },
          { id: "vendors", label: "Vendors" },
          { id: "year", label: "Financial year" },
        ]}
        value={tab}
        onChange={changeTab}
        aria-label="Finance settings sections"
      />

      {data.error && tab !== "vendors" && (
        <Notice tone="red" role="alert" title={data.error} action={<Button variant="outline" onClick={data.reload}>Try again</Button>} />
      )}

      {tab === "expense" && settings && settings.whtRatesConfirmedAt === null && (
        <Notice
          tone="gold"
          title="Tax rates have not been checked by an accountant yet."
          action={<Button variant="outline" loading={checking} disabled={data.loading} onClick={() => void changeCheck(true)}>Mark as checked</Button>}
        />
      )}

      {tab === "expense" && !failedEmpty && (
        <ExpenseCategoriesTab
          categories={data.data?.categories ?? null}
          loading={data.loading}
          adding={adding}
          onAddClose={() => setAdding(false)}
          onChanged={data.reload}
        />
      )}
      {tab === "revenue" && !failedEmpty && (
        <RevenueCategoriesTab
          categories={data.data?.revenueCategories ?? null}
          loading={data.loading}
          adding={adding}
          onAddClose={() => setAdding(false)}
          onChanged={data.reload}
        />
      )}
      {tab === "vendors" && <VendorsTab adding={adding} onAddClose={() => setAdding(false)} />}
      {tab === "year" && (settings ? (
        <FinancialYearTab settings={settings} reloading={data.loading} checking={checking} onCheck={(checked) => void changeCheck(checked)} onSaved={data.reload} />
      ) : !failedEmpty && (
        <div aria-hidden="true" className="grid gap-4 md:grid-cols-2 md:gap-5">
          <span className="h-72 animate-pulse rounded-card bg-track" />
          <span className="h-36 animate-pulse rounded-card bg-track" />
        </div>
      ))}
    </div>
  );
}
