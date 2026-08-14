import { useCallback, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { User } from "../App";
import { api } from "../api/api";
import Button from "../lib/Button";
import Container from "../lib/Container";
import { pakistanToday } from "../lib/financePeriods";

type Props = { user: User | null };

type Holder = {
  financeAccountId: number;
  personName: string;
  accountName: string;
  isActive: boolean;
  openingBalance: number;
  currentBalance: number;
  outstandingSince: string | null;
  daysOutstanding: number | null;
  lastActivityDate: string | null;
  transactionCount: number;
};

type Overview = {
  totalHeldByStaff: number;
  totalOwedToStaff: number;
  netStaffBalance: number;
  cashAndBankBalance: number;
  trackedCompanyCash: number;
  holdingCount: number;
  owedCount: number;
  holders: Holder[];
};

type HistoryItem = {
  recordType: "Transfer" | "Expense";
  recordId: number;
  kind: string;
  date: string;
  description: string;
  reference: string | null;
  projectName: string | null;
  amount: number;
  grossAmount: number;
  whtAmount: number;
  runningBalance: number;
  movementType: "FundsGiven" | "FundsReturned" | null;
  counterpartyFinanceAccountId: number | null;
  counterpartyFinanceAccountName: string | null;
  note: string | null;
  concurrencyToken: string | null;
};

type Statement = { holder: Holder; items: HistoryItem[]; hasMore: boolean; nextCursor: string | null };
type AccountOption = { id: number; name: string; accountHolderName: string; isActive: boolean };
type TransferForm = {
  id: number | null;
  type: "FundsGiven" | "FundsReturned";
  amount: string;
  date: string;
  counterpartyFinanceAccountId: string;
  reference: string;
  note: string;
  concurrencyToken: string;
};

const today = pakistanToday;
/** How many movements one page of a person's history holds. */
const PAGE_SIZE = 100;
const money = (value: number) => {
  const sign = value < 0 ? "−" : "";
  return `${sign}Rs ${Math.abs(value).toLocaleString("en-PK", { maximumFractionDigits: 2 })}`;
};
const date = (value: string | null) => value
  ? new Date(value).toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" })
  : "—";
const emptyTransfer = (type: TransferForm["type"], amount = ""): TransferForm => ({
  id: null, type, amount, date: today(), counterpartyFinanceAccountId: "",
  reference: "", note: "", concurrencyToken: "",
});

export default function StaffCashPage({ user }: Props) {
  const navigate = useNavigate();
  const [overview, setOverview] = useState<Overview | null>(null);
  const [accounts, setAccounts] = useState<AccountOption[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [statement, setStatement] = useState<Statement | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingStatement, setLoadingStatement] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [holderName, setHolderName] = useState<string | null>(null);
  const [transfer, setTransfer] = useState<TransferForm | null>(null);
  const [saving, setSaving] = useState(false);

  // Mirrors selectedId so `load` can read the current selection without listing it as a dependency.
  const selectedRef = useRef<number | null>(null);
  useEffect(() => { selectedRef.current = selectedId; }, [selectedId]);

  // Which person the newest history request was for. Without this, selecting A then B can let A's
  // slower response land last: the panel would then name A while every button on it still posts to
  // B, which is how company cash ends up recorded against the wrong employee.
  const statementRequest = useRef(0);

  const loadStatement = useCallback(async (id: number, cursor: string | null = null) => {
    const request = ++statementRequest.current;
    setLoadingStatement(true);
    try {
      const params = new URLSearchParams({ take: String(PAGE_SIZE) });
      if (cursor) params.set("cursor", cursor);
      const response = await api(`/api/finance/staff-cash/${id}?${params.toString()}`);
      if (request !== statementRequest.current) return;
      if (!response.ok) throw new Error(await message(response, "Could not load this person's history."));
      const next = await response.json() as Statement;
      if (request !== statementRequest.current) return;
      // Older pages append; anything else replaces. The id check is what keeps a page of one
      // person's movements from being appended to another person's statement.
      setStatement((current) => cursor && current?.holder.financeAccountId === id
        ? { ...next, items: [...current.items, ...next.items] }
        : next);
    } finally {
      if (request === statementRequest.current) setLoadingStatement(false);
    }
  }, []);

  const load = useCallback(async (keepSelected = true) => {
    setLoading(true);
    setError(null);
    try {
      const [overviewResponse, accountsResponse] = await Promise.all([
        api("/api/finance/staff-cash?includeSettled=true"),
        api("/api/finance/accounts/options?includeInactive=true&cashLikeOnly=true"),
      ]);
      if (!overviewResponse.ok) throw new Error(await message(overviewResponse, "Could not load staff cash."));
      const nextOverview = await overviewResponse.json() as Overview;
      setOverview(nextOverview);
      if (accountsResponse.ok) setAccounts(await accountsResponse.json());
      // Read through a ref rather than a dependency: naming selectedId as one would rebuild this
      // callback on every selection and re-run the mount effect below, refetching the whole
      // overview each time somebody clicked a name.
      const current = selectedRef.current;
      if (keepSelected && current && nextOverview.holders.some((h) => h.financeAccountId === current)) {
        await loadStatement(current);
      } else if (!keepSelected) {
        setSelectedId(null);
        setStatement(null);
      }
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Could not load staff cash.");
    } finally {
      setLoading(false);
    }
  }, [loadStatement]);

  useEffect(() => {
    if (user && user.role !== "Admin") { navigate("/"); return; }
    if (user?.role === "Admin") void load();
  }, [load, navigate, user]);

  const selectHolder = async (holder: Holder) => {
    setSelectedId(holder.financeAccountId);
    setError(null);
    try { await loadStatement(holder.financeAccountId); }
    catch (loadError) { setError(loadError instanceof Error ? loadError.message : "Could not load history."); }
  };

  const loadOlder = async () => {
    // Page from the statement that belongs to the current selection, never from one left on screen
    // by a previous person — the cursor would be counted against the wrong history.
    if (!selectedId || loadingStatement) return;
    if (!statement || statement.holder.financeAccountId !== selectedId) return;
    if (!statement.nextCursor) return;
    try { await loadStatement(selectedId, statement.nextCursor); }
    catch (loadError) { setError(loadError instanceof Error ? loadError.message : "Could not load older movements."); }
  };

  const createHolder = async () => {
    if (saving || holderName === null) return;
    if (!holderName.trim()) { setError("Enter the person's name."); return; }
    setSaving(true); setError(null);
    try {
      const response = await api("/api/finance/staff-cash/holders", {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ personName: holderName.trim() }),
      });
      if (!response.ok) throw new Error(await message(response, "Could not add this person."));
      const holder = await response.json() as Holder;
      setHolderName(null);
      await load(false);
      await selectHolder(holder);
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Could not add this person.");
    } finally { setSaving(false); }
  };

  const saveTransfer = async () => {
    if (!transfer || !selectedId || saving) return;
    const amount = Number(transfer.amount);
    if (!Number.isFinite(amount) || amount <= 0 || !transfer.counterpartyFinanceAccountId) {
      setError("Enter an amount greater than zero and select the cash or bank account used.");
      return;
    }
    setSaving(true); setError(null);
    try {
      const url = transfer.id
        ? `/api/finance/staff-cash/${selectedId}/transfers/${transfer.id}`
        : `/api/finance/staff-cash/${selectedId}/transfers`;
      const response = await api(url, {
        method: transfer.id ? "PUT" : "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          type: transfer.type, amount, date: transfer.date,
          counterpartyFinanceAccountId: Number(transfer.counterpartyFinanceAccountId),
          reference: transfer.reference.trim() || null, note: transfer.note.trim() || null,
          concurrencyToken: transfer.concurrencyToken,
        }),
      });
      if (!response.ok) throw new Error(await message(response, "Could not record this movement."));
      setTransfer(null);
      await load();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Could not record this movement.");
    } finally { setSaving(false); }
  };

  const editTransfer = (row: HistoryItem) => setTransfer({
    id: row.recordId, type: row.movementType ?? "FundsGiven", amount: String(row.grossAmount),
    date: row.date.slice(0, 10), counterpartyFinanceAccountId: String(row.counterpartyFinanceAccountId ?? ""),
    reference: row.reference ?? "", note: row.note ?? "", concurrencyToken: row.concurrencyToken ?? "",
  });

  const deleteTransfer = async (row: HistoryItem) => {
    if (!selectedId || !confirm("Delete this staff cash movement? The account balances will be recalculated.")) return;
    setError(null);
    const response = await api(
      `/api/finance/staff-cash/${selectedId}/transfers/${row.recordId}?concurrencyToken=${encodeURIComponent(row.concurrencyToken ?? "")}`,
      { method: "DELETE" },
    );
    if (!response.ok) { setError(await message(response, "Could not delete this movement.")); return; }
    await load();
  };

  if (!user || user.role !== "Admin") return null;
  // The person named on this panel is looked up by the same id every button on it posts to, so the
  // heading and the mutation target cannot disagree — whatever order responses arrived in.
  const selected = overview?.holders.find((h) => h.financeAccountId === selectedId) ?? null;
  const shownStatement = statement && statement.holder.financeAccountId === selectedId ? statement : null;

  return <Container className="py-8">
    <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
      <div>
        <Link to="/finance" className="text-sm text-[var(--accent)]">← Finance dashboard</Link>
        <h1 className="mt-1 text-3xl font-bold text-[var(--text-heading)]">Cash held by staff</h1>
        <p className="mt-1 max-w-3xl text-sm text-[var(--text-muted)]">Track company money after it leaves the safe or bank, without treating it as an expense until a receipt is recorded.</p>
      </div>
      <div className="flex gap-2">
        <Link to="/finance/accounts"><Button variant="outline">Finance accounts</Button></Link>
        <Button onClick={() => { setError(null); setHolderName(""); }}>Add person</Button>
      </div>
    </div>

    {error && <div className="mb-5 rounded-xl border border-rose-500/25 bg-rose-500/[0.08] p-4 text-sm text-rose-300">{error}</div>}

    <div className="mb-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
      <Stat label="Held by staff" value={money(overview?.totalHeldByStaff ?? 0)} hint={`${overview?.holdingCount ?? 0} people`} tone="emerald" />
      <Stat label="Company owes staff" value={money(overview?.totalOwedToStaff ?? 0)} hint={`${overview?.owedCount ?? 0} people`} tone="rose" />
      <Stat label="Net staff balance" value={money(overview?.netStaffBalance ?? 0)} hint="Held less owed" />
      <Stat label="Cash / bank" value={money(overview?.cashAndBankBalance ?? 0)} hint="Physical accounts" />
      <Stat label="Tracked company cash" value={money(overview?.trackedCompanyCash ?? 0)} hint="Cash / bank + net staff" tone="accent" />
    </div>
    <p className="mb-6 text-xs text-[var(--text-muted)]">Reconciliation: {money(overview?.cashAndBankBalance ?? 0)} in cash/bank + {money(overview?.netStaffBalance ?? 0)} net with staff = {money(overview?.trackedCompanyCash ?? 0)} tracked company cash.</p>

    <div className="grid gap-5 xl:grid-cols-[minmax(320px,0.8fr)_minmax(620px,1.5fr)]">
      <section className="overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)]">
        <div className="border-b border-[var(--border)] px-5 py-4"><h2 className="font-bold text-[var(--text-heading)]">People</h2><p className="text-xs text-[var(--text-muted)]">Positive, owed, and settled floats</p></div>
        <div className="divide-y divide-[var(--border)]">
          {overview?.holders.map((holder) => {
            const positive = holder.currentBalance > 0;
            const negative = holder.currentBalance < 0;
            return <button key={holder.financeAccountId} onClick={() => void selectHolder(holder)} className={`w-full p-4 text-left transition hover:bg-[var(--surface-glass)] ${selectedId === holder.financeAccountId ? "bg-[var(--surface-glass)] ring-1 ring-inset ring-[var(--accent)]" : ""}`}>
              <div className="flex items-start justify-between gap-3"><div><p className="font-semibold text-[var(--text-heading)]">{holder.personName}</p><p className="mt-0.5 text-xs text-[var(--text-muted)]">{holder.transactionCount} movement{holder.transactionCount === 1 ? "" : "s"}{holder.isActive ? "" : " · Inactive"}</p></div><p className={`font-bold ${positive ? "text-emerald-300" : negative ? "text-rose-300" : "text-[var(--text-muted)]"}`}>{money(holder.currentBalance)}</p></div>
              <div className="mt-2 flex items-center justify-between text-xs"><span className={positive ? "text-emerald-300" : negative ? "text-rose-300" : "text-[var(--text-muted)]"}>{positive ? "Holding company cash" : negative ? "Company owes this person" : "Settled"}</span><span className="text-[var(--text-muted)]">{holder.daysOutstanding != null ? `${holder.daysOutstanding} day${holder.daysOutstanding === 1 ? "" : "s"} open` : "No open balance"}</span></div>
            </button>;
          })}
          {!loading && !overview?.holders.length && <p className="p-8 text-center text-sm text-[var(--text-muted)]">No staff floats yet. Add a person, then record the money handed to them.</p>}
          {loading && <p className="p-8 text-center text-sm text-[var(--text-muted)]">Loading…</p>}
        </div>
      </section>

      <section className="overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)]">
        {selected && shownStatement ? <>
          <div className="border-b border-[var(--border)] p-5">
            <div className="flex flex-wrap items-start justify-between gap-3"><div><p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">Staff float</p><h2 className="text-2xl font-bold text-[var(--text-heading)]">{selected.personName}</h2><p className="text-sm text-[var(--text-muted)]">Open since {date(selected.outstandingSince)}{selected.daysOutstanding != null ? ` · ${selected.daysOutstanding} days` : " · Fully settled"}</p></div><div className="text-right"><p className="text-xs text-[var(--text-muted)]">Current balance</p><p className={`text-2xl font-bold ${selected.currentBalance >= 0 ? "text-emerald-300" : "text-rose-300"}`}>{money(selected.currentBalance)}</p><p className="text-xs text-[var(--text-muted)]">{selected.currentBalance < 0 ? "Payable to staff" : "Company asset"}</p></div></div>
            <div className="mt-4 flex flex-wrap gap-2">
              <Button variant="outline" disabled={!selected.isActive} onClick={() => setTransfer(emptyTransfer("FundsGiven", selected.currentBalance < 0 ? String(Math.abs(selected.currentBalance)) : ""))}>{selected.currentBalance < 0 ? "Settle amount owed" : "Give / reimburse money"}</Button>
              <Button variant="outline" disabled={!selected.isActive || selected.currentBalance <= 0} onClick={() => setTransfer(emptyTransfer("FundsReturned", String(Math.max(0, selected.currentBalance))))}>Record cash returned</Button>
              <Link to="/finance"><Button>Record an expense</Button></Link>
            </div>
            <p className="mt-2 text-xs text-[var(--text-muted)]">On the expense form, choose “{selected.accountName}” under Paid From Account.</p>
          </div>
          <div className="overflow-x-auto"><table className="w-full min-w-[820px] text-sm"><thead><tr className="border-b border-[var(--border)] text-left text-[var(--text-muted)]">{["Date / movement", "Account / project", "Amount", "Running balance", "Actions"].map((label) => <th key={label} className="p-3">{label}</th>)}</tr></thead><tbody>{shownStatement.items.map((row) => <tr key={`${row.recordType}-${row.recordId}`} className="border-b border-[var(--border)] align-top"><td className="p-3"><p className="font-semibold text-[var(--text-heading)]">{row.kind}</p><p className="text-xs text-[var(--text-muted)]">{date(row.date)}</p>{row.description && <p className="mt-1 text-xs text-[var(--text-muted)]">{row.description}</p>}{row.note && <p className="mt-1 max-w-xs text-xs text-[var(--text-muted)]">{row.note}</p>}</td><td className="p-3"><p>{row.counterpartyFinanceAccountName ?? row.projectName ?? "General"}</p>{row.reference && <p className="text-xs text-[var(--text-muted)]">Ref: {row.reference}</p>}{row.recordType === "Expense" && row.whtAmount > 0 && <p className="text-xs text-amber-300">Gross {money(row.grossAmount)} · WHT {money(row.whtAmount)}</p>}</td><td className={`p-3 font-bold ${row.amount >= 0 ? "text-emerald-300" : "text-rose-300"}`}>{row.amount >= 0 ? "+" : "−"}{money(Math.abs(row.amount))}</td><td className={`p-3 font-bold ${row.runningBalance < 0 ? "text-rose-300" : "text-[var(--text-heading)]"}`}>{money(row.runningBalance)}</td><td className="p-3">{row.recordType === "Transfer" ? <div className="flex gap-3"><button className="text-[var(--accent)]" onClick={() => editTransfer(row)}>Correct</button><button className="text-rose-300" onClick={() => void deleteTransfer(row)}>Delete</button></div> : <Link to="/finance" className="text-[var(--accent)]">View expenses</Link>}</td></tr>)}</tbody></table></div>
          {!shownStatement.items.length && !loadingStatement && <p className="p-8 text-center text-sm text-[var(--text-muted)]">No movements yet. Record money given to begin this float.</p>}
          {loadingStatement && <p className="border-t border-[var(--border)] p-4 text-center text-sm text-[var(--text-muted)]">Loading movements…</p>}
          {shownStatement.hasMore && (
            <div className="border-t border-[var(--border)] p-4 text-center">
              <Button variant="outline" disabled={loadingStatement} onClick={() => void loadOlder()}>Load older movements</Button>
              <p className="mt-2 text-xs text-[var(--text-muted)]">Showing the {shownStatement.items.length} most recent movements.</p>
            </div>
          )}
        </> : <div className="flex min-h-80 items-center justify-center p-8 text-center text-sm text-[var(--text-muted)]">Select a person to see every receipt-backed expense, transfer, and running balance.</div>}
      </section>
    </div>

    {holderName !== null && <Modal title="Add staff float" close={() => !saving && setHolderName(null)}><div className="space-y-4"><Field label="Person name" value={holderName} set={setHolderName} autoFocus /><p className="rounded-xl bg-[var(--surface-glass)] p-3 text-xs text-[var(--text-muted)]">The float starts at zero. Record money given from the relevant cash or bank account next.</p><div className="flex justify-end gap-2"><Button variant="ghost" disabled={saving} onClick={() => setHolderName(null)}>Cancel</Button><Button disabled={saving} onClick={() => void createHolder()}>{saving ? "Adding…" : "Add person"}</Button></div></div></Modal>}

    {transfer && selected && <Modal title={`${transfer.id ? "Correct" : "Record"} movement · ${selected.personName}`} close={() => !saving && setTransfer(null)}><div className="space-y-4">
      <label className="block text-sm text-[var(--text-muted)]">Movement<select value={transfer.type} onChange={(event) => setTransfer({ ...transfer, type: event.target.value as TransferForm["type"] })} className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-[var(--text-primary)]"><option value="FundsGiven">Money given / reimbursement (account → staff)</option><option value="FundsReturned">Cash returned (staff → account)</option></select></label>
      <div className="grid gap-3 sm:grid-cols-2"><Field label="Amount (Rs)" type="number" value={transfer.amount} set={(value) => setTransfer({ ...transfer, amount: value })} /><Field label="Date" type="date" value={transfer.date} set={(value) => setTransfer({ ...transfer, date: value })} /></div>
      <label className="block text-sm text-[var(--text-muted)]">{transfer.type === "FundsGiven" ? "Paid from company account" : "Returned to company account"}<select value={transfer.counterpartyFinanceAccountId} onChange={(event) => setTransfer({ ...transfer, counterpartyFinanceAccountId: event.target.value })} className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-[var(--text-primary)]"><option value="">Select cash, bank, or wallet</option>{accounts.filter((account) => account.isActive || String(account.id) === transfer.counterpartyFinanceAccountId).map((account) => <option key={account.id} value={account.id}>{account.name} · {account.accountHolderName}{account.isActive ? "" : " (Inactive)"}</option>)}</select></label>
      <Field label="Reference (optional)" value={transfer.reference} set={(value) => setTransfer({ ...transfer, reference: value })} />
      <label className="block text-sm text-[var(--text-muted)]">Note (optional)<textarea value={transfer.note} onChange={(event) => setTransfer({ ...transfer, note: event.target.value })} className="mt-1 min-h-24 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-[var(--text-primary)]" /></label>
      <p className="rounded-xl border border-indigo-500/20 bg-indigo-500/[0.06] p-3 text-xs text-[var(--text-muted)]">This movement only relocates company cash. It does not change profit.</p>
      <div className="flex justify-end gap-2"><Button variant="ghost" disabled={saving} onClick={() => setTransfer(null)}>Cancel</Button><Button disabled={saving} onClick={() => void saveTransfer()}>{saving ? "Saving…" : "Record movement"}</Button></div>
    </div></Modal>}
  </Container>;
}

async function message(response: Response, fallback: string) {
  const body = await response.json().catch(() => null) as { message?: string } | null;
  return body?.message ?? fallback;
}

function Stat({ label, value, hint, tone }: { label: string; value: string; hint: string; tone?: "emerald" | "rose" | "accent" }) {
  const color = tone === "emerald" ? "text-emerald-300" : tone === "rose" ? "text-rose-300" : tone === "accent" ? "text-[var(--accent)]" : "text-[var(--text-heading)]";
  return <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4"><p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">{label}</p><p className={`mt-2 text-xl font-bold ${color}`}>{value}</p><p className="mt-1 text-xs text-[var(--text-muted)]">{hint}</p></div>;
}

function Field({ label, value, set, type = "text", autoFocus = false }: { label: string; value: string; set: (value: string) => void; type?: string; autoFocus?: boolean }) {
  return <label className="block text-sm text-[var(--text-muted)]">{label}<input autoFocus={autoFocus} type={type} value={value} onChange={(event) => set(event.target.value)} className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-[var(--text-primary)]" /></label>;
}

function Modal({ title, close, children }: { title: string; close: () => void; children: ReactNode }) {
  return <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/65 p-4" onMouseDown={close}><div className="max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-6 shadow-2xl" onMouseDown={(event) => event.stopPropagation()}><div className="mb-5 flex items-center justify-between"><h2 className="text-xl font-bold text-[var(--text-heading)]">{title}</h2><button onClick={close} className="text-xl text-[var(--text-muted)]">×</button></div>{children}</div></div>;
}
