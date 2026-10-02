import { useCallback, useEffect, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import type { User } from "../App.tsx";
import { Button, IconPlus, Notice, PageHeader, StatCard, Tabs, useIsPhone, type TabItem } from "../components/ui";
import { pageAccess } from "../features/access/permissions.ts";
import { commissionRebateApi } from "../features/commissionRebates/api.ts";
import { CommissionsList } from "../features/commissionRebates/CommissionsList.tsx";
import { PartnersList } from "../features/commissionRebates/PartnersList.tsx";
import { RebatesList } from "../features/commissionRebates/RebatesList.tsx";
import type { CommissionRebateSummary } from "../features/commissionRebates/types.ts";
import { usePageTrail } from "../layouts/trail.ts";
import { formatPkr } from "../utils/currency.ts";

type Tab = "commissions" | "rebates" | "partners";

const TABS: readonly TabItem[] = [
  { id: "commissions", label: "Commissions" },
  { id: "rebates", label: "Rebates" },
  { id: "partners", label: "Partners" },
];

const tabOf = (value: string | null): Tab => (value === "rebates" || value === "partners" ? value : "commissions");
const cents = (value: number) => Math.round(value * 100) / 100;

type SummaryState = "loading" | "ready" | "error";

/** Finance → Commissions & Rebates: what partners are owed and what customers get back, and the partner directory. */
export default function CommissionRebatesPage({ user }: { user: User | null }) {
  usePageTrail([{ label: "Commissions & Rebates" }]);
  const access = pageAccess(user?.role, "finance");
  const allowed = access === "allow";
  const navigate = useNavigate();
  const isPhone = useIsPhone();
  const [params, setParams] = useSearchParams();
  const tab = tabOf(params.get("tab"));
  const partnerParam = Number(params.get("partner"));
  const openPartnerId = tab === "partners" && Number.isInteger(partnerParam) && partnerParam > 0 ? partnerParam : null;

  const [summary, setSummary] = useState<CommissionRebateSummary | null>(null);
  const [summaryState, setSummaryState] = useState<SummaryState>("loading");
  const [summaryAttempt, setSummaryAttempt] = useState(0);
  const [adding, setAdding] = useState(false);

  useEffect(() => {
    if (access === "deny") navigate("/");
  }, [access, navigate]);

  // The cards keep their figures while a newer answer is on its way; only a failed first load shows a dash.
  useEffect(() => {
    if (!allowed) return;
    let current = true;
    commissionRebateApi.summary()
      .then((loaded) => {
        if (!current) return;
        setSummary(loaded);
        setSummaryState("ready");
      })
      .catch(() => {
        if (current) setSummaryState("error");
      });
    return () => { current = false; };
  }, [allowed, summaryAttempt]);

  const reloadSummary = useCallback(() => {
    setSummaryState((state) => (state === "error" ? "loading" : state));
    setSummaryAttempt((attempt) => attempt + 1);
  }, []);

  const onTab = (id: string) => {
    setAdding(false);
    setParams({ tab: id });
  };

  const onPartnerOpened = useCallback(() => {
    setParams((current) => {
      const next = new URLSearchParams(current);
      next.delete("partner");
      return next;
    }, { replace: true });
  }, [setParams]);

  if (!allowed) {
    return access === "deny" ? null : (
      <div role="status" aria-busy="true" className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:px-8 md:py-7">
        <span className="sr-only">Loading</span>
        <span aria-hidden="true" className="h-9 w-64 animate-pulse rounded bg-track" />
        <span aria-hidden="true" className="h-64 animate-pulse rounded-card bg-track" />
      </div>
    );
  }

  const cardState = summaryState === "ready" && summary ? "ready" : summaryState === "error" ? "error" : "loading";
  // Notes and the red tone belong to a figure that is on screen; a failed reload leaves the old summary in memory.
  const ready = cardState === "ready" && summary !== null;
  const reversal = summary ? cents(summary.commissionReversalRequired + summary.rebateReversalRequired) : 0;

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      <PageHeader
        title="Commissions & Rebates"
        className="max-md:flex-row max-md:items-start max-md:justify-between"
        actions={tab === "partners" ? (
          <Button icon={<IconPlus size={16} />} onClick={() => setAdding(true)}>{isPhone ? "Add" : "Add partner"}</Button>
        ) : undefined}
      />

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4 md:gap-4">
        <StatCard
          label="Commission owed"
          value={summary ? formatPkr(summary.payableCommission) : ""}
          note={ready ? `of ${formatPkr(summary.accruedCommission)} agreed` : undefined}
          state={cardState}
        />
        <StatCard label="Commission paid" value={summary ? formatPkr(summary.commissionPaid) : ""} state={cardState} />
        <StatCard
          label="Rebates"
          value={summary ? formatPkr(summary.rebatesGranted) : ""}
          note={ready ? `${formatPkr(summary.rebatesAppliedOrPaid)} given so far` : undefined}
          state={cardState}
        />
        <StatCard label="Reversal required" value={formatPkr(reversal)} tone={ready && reversal > 0 ? "red" : undefined} state={cardState} />
      </div>
      {summaryState === "error" && (
        <Notice
          tone="red"
          role="alert"
          title="The totals could not be loaded."
          action={<Button variant="outline" onClick={reloadSummary}>Try again</Button>}
        />
      )}

      <Tabs items={TABS} value={tab} onChange={onTab} aria-label="Commissions and rebates sections" phoneDropdownFrom={3} className="md:self-start" />

      {tab === "commissions" && <CommissionsList />}
      {tab === "rebates" && <RebatesList />}
      {tab === "partners" && (
        <PartnersList
          adding={adding}
          onAddClose={() => setAdding(false)}
          onChanged={reloadSummary}
          openPartnerId={openPartnerId}
          onOpened={onPartnerOpened}
        />
      )}
    </div>
  );
}
