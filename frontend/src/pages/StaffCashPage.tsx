import { useCallback, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { User } from "../App";
import { api } from "../api/api";
import Button from "../lib/Button";
import Container from "../lib/Container";
import FinanceAttachmentField from "../components/FinanceAttachmentField";
import { financeApiError, openAttachmentAt, openFinanceAttachment, type FinanceAttachmentInfo } from "../api/financeAttachments";
import { pakistanToday } from "../lib/financePeriods";
import { moneyRequest, useIdempotencyKeys } from "../lib/idempotency";
import { useProjects } from "../contexts/projectsContextValue";
import { listCategories, vendorOptions } from "../features/finance/whtApi";
import { emptyWht, type ExpenseCategory, type VendorOption, type WhtFormValue } from "../features/finance/whtTypes";
import ExpenseWhtFields from "../components/ExpenseWhtFields";

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
  attachment: FinanceAttachmentInfo | null;
};

type Statement = { holder: Holder; items: HistoryItem[]; hasMore: boolean; nextCursor: string | null };

// Money the person has already spent out of the float. It is an ordinary company expense — the
// only thing fixed about it is which account paid, which is this float.
//
// It carries the vendor's identity and the withholding block for a reason. The rate depends on
// the supplier's filer status, and a free-text payee resolves to Unknown, which is withheld at
// the non-filer rate — so a form without the vendor list quietly over-deducts from every filer
// it pays. Same picker and same shared tax component as the dashboard's expense form, so the
// figure does not depend on which screen the expense was entered from.
type ExpenseForm = {
  amount: string;
  date: string;
  categoryId: string;
  projectId: string;
  /** The registered supplier, when it is one. Empty for a one-off payee. */
  vendorId: string;
  vendor: string;
  description: string;
  attachment: File | null;
  wht: WhtFormValue;
};
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
  attachment: FinanceAttachmentInfo | null;
  selectedAttachment: File | null;
  removeAttachment: boolean;
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
// Distinct from a real vendor id, and from the empty string a cleared select would give.
const CUSTOM_PAYEE = "__one_off__";

const emptyExpense = (): ExpenseForm => ({
  amount: "", date: today(), categoryId: "", projectId: "", vendorId: "", vendor: "",
  description: "", attachment: null, wht: emptyWht(),
});

const emptyTransfer = (type: TransferForm["type"], amount = ""): TransferForm => ({
  id: null, type, amount, date: today(), counterpartyFinanceAccountId: "",
  reference: "", note: "", concurrencyToken: "",
  attachment: null, selectedAttachment: null, removeAttachment: false,
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
  const [expense, setExpense] = useState<ExpenseForm | null>(null);
  const [categories, setCategories] = useState<ExpenseCategory[]>([]);
  const [vendors, setVendors] = useState<VendorOption[]>([]);
  const { projects } = useProjects();
  const [saving, setSaving] = useState(false);
  const idempotency = useIdempotencyKeys();

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
      // Heads are only needed by the expense form; a failure to read them must not take the
      // whole page down, so it is deliberately not awaited into the throwing path above.
      void listCategories().then(setCategories).catch(() => setCategories([]));
      void vendorOptions().then(setVendors).catch(() => setVendors([]));
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
      // Always the multipart route, file or no file: the slip and the figures are one save, so
      // there is no second request that can leave the cash recorded and the evidence lost.
      const url = transfer.id
        ? `/api/finance/staff-cash/${selectedId}/transfers/${transfer.id}/form`
        : `/api/finance/staff-cash/${selectedId}/transfers/form`;
      const body = new FormData();
      body.append("type", transfer.type);
      body.append("amount", String(amount));
      body.append("date", transfer.date);
      body.append("counterpartyFinanceAccountId", transfer.counterpartyFinanceAccountId);
      if (transfer.reference.trim()) body.append("reference", transfer.reference.trim());
      if (transfer.note.trim()) body.append("note", transfer.note.trim());
      body.append("concurrencyToken", transfer.concurrencyToken);
      if (transfer.selectedAttachment) body.append("attachment", transfer.selectedAttachment);
      if (transfer.removeAttachment) body.append("removeAttachment", "true");
      const init: RequestInit = { method: transfer.id ? "PUT" : "POST", body };
      // New movements only: an edit is already protected by its row version, and it is the insert a
      // lost response can duplicate.
      const signature = `staff-cash:${selectedId}:${transfer.type}:${amount}:${transfer.date}`;
      const response = await api(
        url,
        transfer.id ? init : moneyRequest(idempotency.key(signature, "staff-cash-transfer"), init),
      );
      if (!response.ok) throw new Error(await financeApiError(response, "Could not record this movement."));
      if (!transfer.id) idempotency.release(signature);
      setTransfer(null);
      await load();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Could not record this movement.");
    } finally { setSaving(false); }
  };

  // Recorded here, but it is not a staff-cash record: it posts to the ordinary expense endpoint
  // with this float as the paying account. That is the whole double entry — the expense hits the
  // P&L, and the credit side lands on the float, which is why the row comes back in this person's
  // history AND in Finance ▸ Expenses without either screen being told about the other.
  const saveExpense = async () => {
    if (!expense || !selectedId || saving) return;
    const amount = Number(expense.amount);
    if (!Number.isFinite(amount) || amount <= 0) {
      setError("Enter an amount greater than zero.");
      return;
    }
    // Not merely preferred: a new expense with no managed head is refused server-side, because
    // a free-text head has no rate table behind it and would be a way to pay a taxable supplier
    // with nothing withheld.
    if (!expense.categoryId) {
      setError("Choose an expense head. Heads are managed under Finance ▸ Settings ▸ Expense heads & rates.");
      return;
    }
    setSaving(true); setError(null);
    try {
      const body = new FormData();
      body.append("financeAccountId", String(selectedId));
      body.append("amount", String(amount));
      body.append("date", expense.date);
      // The head carries the withholding rate. Sending the id and no WHT figures is what asks the
      // server to work the tax out itself, exactly as the dashboard form does for a managed head.
      if (expense.categoryId) body.append("categoryId", expense.categoryId);
      if (expense.projectId) body.append("projectId", expense.projectId);
      // The id is what carries filer status into the rate; the text is still sent so a one-off
      // payee is still named on the record.
      if (expense.vendorId) body.append("vendorId", expense.vendorId);
      if (expense.vendor.trim()) body.append("vendor", expense.vendor.trim());
      // Only what the operator confirmed on screen. The server recomputes and rejects a figure
      // that does not belong to the head, so a stale value cannot slip through.
      if (expense.wht.rate !== "") body.append("whtRate", expense.wht.rate);
      if (expense.wht.amount !== "") body.append("whtAmount", expense.wht.amount);
      if (expense.wht.overrideReason.trim()) body.append("whtOverrideReason", expense.wht.overrideReason.trim());
      if (expense.description.trim()) body.append("description", expense.description.trim());
      if (expense.attachment) body.append("attachment", expense.attachment);
      const signature = `staff-expense:${selectedId}:${amount}:${expense.date}:${expense.categoryId}`;
      const response = await api(
        "/api/Finance/expenses/form",
        moneyRequest(idempotency.key(signature, "expense"), { method: "POST", body }),
      );
      if (!response.ok) throw new Error(await financeApiError(response, "Could not record this expense."));
      idempotency.release(signature);
      setExpense(null);
      await load();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Could not record this expense.");
    } finally { setSaving(false); }
  };

  const editTransfer = (row: HistoryItem) => setTransfer({
    id: row.recordId, type: row.movementType ?? "FundsGiven", amount: String(row.grossAmount),
    date: row.date.slice(0, 10), counterpartyFinanceAccountId: String(row.counterpartyFinanceAccountId ?? ""),
    reference: row.reference ?? "", note: row.note ?? "", concurrencyToken: row.concurrencyToken ?? "",
    attachment: row.attachment, selectedAttachment: null, removeAttachment: false,
  });

  // Two different stores behind one column: a transfer's slip hangs off the float, an expense's
  // receipt off the expense itself, and the row already says which it is.
  const viewAttachment = async (row: HistoryItem, download: boolean) => {
    if (!row.attachment || !selectedId) return;
    try {
      if (row.recordType === "Expense") {
        await openFinanceAttachment("expense", row.recordId, row.attachment.fileName, download);
      } else {
        await openAttachmentAt(
          `/api/finance/staff-cash/${selectedId}/transfers/${row.recordId}/attachment`,
          row.attachment.fileName, download,
        );
      }
    } catch (openError) {
      setError(openError instanceof Error ? openError.message : "The attachment could not be opened.");
    }
  };

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
              <Button disabled={!selected.isActive} onClick={() => setExpense(emptyExpense())}>Record an expense</Button>
            </div>
            <p className="mt-2 text-xs text-[var(--text-muted)]">An expense recorded here is paid from “{selected.accountName}” — it reduces this float and appears in Finance ▸ Expenses.</p>
          </div>
          <div className="overflow-x-auto"><table className="w-full min-w-[950px] text-sm"><thead><tr className="border-b border-[var(--border)] text-left text-[var(--text-muted)]">{["Date / movement", "Account / project", "Amount", "Running balance", "Attachment", "Actions"].map((label) => <th key={label} className="p-3">{label}</th>)}</tr></thead><tbody>{shownStatement.items.map((row) => <tr key={`${row.recordType}-${row.recordId}`} className="border-b border-[var(--border)] align-top"><td className="p-3"><p className="font-semibold text-[var(--text-heading)]">{row.kind}</p><p className="text-xs text-[var(--text-muted)]">{date(row.date)}</p>{row.description && <p className="mt-1 text-xs text-[var(--text-muted)]">{row.description}</p>}{row.note && <p className="mt-1 max-w-xs text-xs text-[var(--text-muted)]">{row.note}</p>}</td><td className="p-3"><p>{row.counterpartyFinanceAccountName ?? row.projectName ?? "General"}</p>{row.reference && <p className="text-xs text-[var(--text-muted)]">Ref: {row.reference}</p>}{row.recordType === "Expense" && row.whtAmount > 0 && <p className="text-xs text-amber-300">Gross {money(row.grossAmount)} · WHT {money(row.whtAmount)}</p>}</td><td className={`p-3 font-bold ${row.amount >= 0 ? "text-emerald-300" : "text-rose-300"}`}>{row.amount >= 0 ? "+" : "−"}{money(Math.abs(row.amount))}</td><td className={`p-3 font-bold ${row.runningBalance < 0 ? "text-rose-300" : "text-[var(--text-heading)]"}`}>{money(row.runningBalance)}</td><td className="p-3">{row.attachment ? <span className="inline-flex items-center gap-2 whitespace-nowrap"><button type="button" onClick={() => void viewAttachment(row, false)} className="rounded-full border border-indigo-500/20 bg-indigo-500/10 px-2.5 py-1 text-[10px] font-semibold text-indigo-300 hover:bg-indigo-500/20">Attached</button><button type="button" aria-label={`Download ${row.attachment.fileName}`} title={`Download ${row.attachment.fileName}`} onClick={() => void viewAttachment(row, true)} className="text-xs font-semibold text-[var(--text-muted)] hover:text-[var(--text-primary)]">↓</button></span> : <span className="text-xs text-[var(--text-muted)]">None</span>}</td><td className="p-3">{row.recordType === "Transfer" ? <div className="flex gap-3"><button className="text-[var(--accent)]" onClick={() => editTransfer(row)}>Correct</button><button className="text-rose-300" onClick={() => void deleteTransfer(row)}>Delete</button></div> : <Link to="/finance" className="text-[var(--accent)]">View expenses</Link>}</td></tr>)}</tbody></table></div>
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
      <FinanceAttachmentField existing={transfer.attachment} selected={transfer.selectedAttachment} removeExisting={transfer.removeAttachment} disabled={saving} onSelected={(file) => setTransfer((current) => current ? { ...current, selectedAttachment: file } : current)} onRemoveExisting={(remove) => setTransfer((current) => current ? { ...current, removeAttachment: remove } : current)} onViewExisting={() => { const row = shownStatement?.items.find((item) => item.recordType === "Transfer" && item.recordId === transfer.id); if (row) void viewAttachment(row, false); }} onDownloadExisting={() => { const row = shownStatement?.items.find((item) => item.recordType === "Transfer" && item.recordId === transfer.id); if (row) void viewAttachment(row, true); }} />
      <p className="rounded-xl border border-indigo-500/20 bg-indigo-500/[0.06] p-3 text-xs text-[var(--text-muted)]">This movement only relocates company cash. It does not change profit.</p>
      <div className="flex justify-end gap-2"><Button variant="ghost" disabled={saving} onClick={() => setTransfer(null)}>Cancel</Button><Button disabled={saving} onClick={() => void saveTransfer()}>{saving ? "Saving…" : "Record movement"}</Button></div>
    </div></Modal>}

  {expense && selected && <Modal title={`Record an expense · ${selected.personName}`} close={() => !saving && setExpense(null)}><div className="space-y-4">
    <label className="block text-sm text-[var(--text-muted)]">Expense head<select value={expense.categoryId} onChange={(event) => setExpense({ ...expense, categoryId: event.target.value })} className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-[var(--text-primary)]"><option value="">Select a head</option>{categories.filter((category) => category.isActive).map((category) => <option key={category.id} value={category.id}>{category.name}{category.isWhtApplicable ? " · WHT" : ""}</option>)}</select></label>
    <div className="grid gap-3 sm:grid-cols-2"><Field label="Amount (Rs)" type="number" value={expense.amount} set={(value) => setExpense({ ...expense, amount: value })} /><Field label="Date" type="date" value={expense.date} set={(value) => setExpense({ ...expense, date: value })} /></div>
    <label className="block text-sm text-[var(--text-muted)]">Project (optional)<select value={expense.projectId} onChange={(event) => setExpense({ ...expense, projectId: event.target.value })} className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-[var(--text-primary)]"><option value="">General — no project</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.projectName}</option>)}</select></label>
    <label className="block text-sm text-[var(--text-muted)]">Paid to<select value={expense.vendorId || CUSTOM_PAYEE} onChange={(event) => { const picked = event.target.value; setExpense({ ...expense, vendorId: picked === CUSTOM_PAYEE ? "" : picked, vendor: picked === CUSTOM_PAYEE ? "" : (vendors.find((vendor) => String(vendor.id) === picked)?.name ?? ""), wht: emptyWht() }); }} className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-[var(--text-primary)]"><option value={CUSTOM_PAYEE}>One-off payee (enter below)…</option>{vendors.filter((vendor) => vendor.isActive || String(vendor.id) === expense.vendorId).map((vendor) => <option key={vendor.id} value={vendor.id}>{vendor.name} — {vendor.filerStatus === "NonFiler" ? "Non-filer" : vendor.filerStatus}</option>)}</select></label>
    {/* Shown only for a payee who is not on the list, because a registered one is already named
        by the picker — and its filer status, not this text, is what sets the rate. */}
    {!expense.vendorId && <Field label="Payee name (optional)" value={expense.vendor} set={(value) => setExpense({ ...expense, vendor: value })} />}
    <label className="block text-sm text-[var(--text-muted)]">What it was for<textarea value={expense.description} onChange={(event) => setExpense({ ...expense, description: event.target.value })} className="mt-1 min-h-20 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-[var(--text-primary)]" /></label>
    {/* The same component the dashboard expense form uses, so the rate table, the filer status
        and the shared annual allowance produce one figure regardless of which screen was used. */}
    <ExpenseWhtFields
      categoryId={expense.categoryId}
      vendorId={expense.vendorId}
      grossAmount={expense.amount}
      date={expense.date}
      excludeExpenseId={null}
      value={expense.wht}
      disabled={saving}
      onChange={(wht) => setExpense((current) => current ? { ...current, wht } : current)}
    />
    <FinanceAttachmentField existing={null} selected={expense.attachment} removeExisting={false} disabled={saving} onSelected={(file) => setExpense((current) => current ? { ...current, attachment: file } : current)} onRemoveExisting={() => {}} onViewExisting={() => {}} onDownloadExisting={() => {}} />
    {/* Said on the form, because this is the one button on the page that spends money rather than
        moving it, and the difference is the whole point of tracking a float. */}
    <p className="rounded-xl border border-amber-500/20 bg-amber-500/[0.06] p-3 text-xs text-amber-200/90">This spends the money: it reduces {selected.personName}’s float and is a cost in the Profit &amp; Loss. The float is charged the gross amount; any tax withheld stays in the company and is owed to FBR.</p>
    <div className="flex justify-end gap-2"><Button variant="ghost" disabled={saving} onClick={() => setExpense(null)}>Cancel</Button><Button disabled={saving} onClick={() => void saveExpense()}>{saving ? "Saving…" : "Record expense"}</Button></div>
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
