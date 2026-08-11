import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { User } from "../App.tsx";
import { api } from "../api/api.ts";
import Button from "../lib/Button.tsx";
import { CrmModal, CrmTabs, ErrorBanner, inputClass, Label, StatePanel } from "../features/leads/CrmUi.tsx";
import * as whtApi from "../features/finance/whtApi.ts";
import {
  FILER_STATUSES,
  MONTHS,
  filerLabel,
  formatRate,
  formatRs,
  type ExpenseCategory,
  type FilerStatus,
  type FinanceSettings,
  type Vendor,
  type WhtDeposit,
  type WhtPayableSummary,
  type WhtVendorLine,
} from "../features/finance/whtTypes.ts";

type Props = { user: User | null };
type FinanceAccountOption = { id: number; name: string; accountHolderName: string; isActive: boolean };
type Tab = "rates" | "vendors" | "payable" | "year";

export default function FinanceSettingsPage({ user }: Props) {
  const navigate = useNavigate();
  useEffect(() => { if (user && user.role !== "Admin") navigate("/"); }, [user, navigate]);

  if (!user) return <StatePanel title="Sign in required" message="Sign in with an Admin account to manage finance settings." />;
  if (user.role !== "Admin") return <StatePanel title="Admin access required" message="Only an Admin can change withholding tax rates, vendors and the financial year." />;
  return <SettingsWorkspace />;
}

function SettingsWorkspace() {
  const [tab, setTab] = useState<Tab>("rates");
  const [settings, setSettings] = useState<FinanceSettings | null>(null);
  const [categories, setCategories] = useState<ExpenseCategory[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const loadShared = useCallback(async () => {
    setLoading(true); setError(null);
    try {
      const [settingRow, categoryRows] = await Promise.all([
        whtApi.getSettings(),
        whtApi.listCategories(true),
      ]);
      setSettings(settingRow);
      setCategories(categoryRows);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Finance settings could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, []);
  useEffect(() => { void loadShared(); }, [loadShared]);

  const unconfirmed = settings != null && settings.whtRatesConfirmedAt == null;

  return (
    <>
      <div className="border-b border-[var(--border)] bg-[var(--bg-card)]">
        <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-6 sm:px-6 lg:flex-row lg:items-end lg:justify-between lg:px-8">
          <div>
            <div className="mb-2 flex flex-wrap items-center gap-2 text-xs font-semibold uppercase tracking-[0.16em] text-[var(--accent)]">
              <Link to="/finance" className="hover:underline">Finance</Link>
              <span className="text-[var(--text-muted)]">/</span>
              <span className="text-[var(--text-muted)]">Settings</span>
            </div>
            <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">Finance settings</h1>
            <p className="mt-1 max-w-3xl text-sm text-[var(--text-muted)]">
              Expense heads and the withholding tax deducted from supplier payments, the vendors those
              rates depend on, and what is owed to FBR.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Link to="/finance"><Button variant="outline">← Finance dashboard</Button></Link>
            <Link to="/finance/accounts"><Button variant="outline">Accounts</Button></Link>
          </div>
        </div>
      </div>

      <div className="mx-auto w-full max-w-[1500px] space-y-5 px-4 py-6 sm:px-6 lg:px-8">
        {error && <ErrorBanner message={error} onRetry={() => void loadShared()} />}

        {unconfirmed && (
          <div className="rounded-2xl border border-amber-500/30 bg-amber-500/[0.07] px-5 py-4 text-sm text-amber-200">
            <p className="font-semibold">These rates have not been confirmed by an accountant.</p>
            <p className="mt-1 text-amber-200/80">
              The seeded percentages are starting values. Published rates for the same head differ
              between sources and changed again under the last two Finance Acts, so check every one
              against the current Act before relying on them — then mark them confirmed under
              Financial year.
            </p>
          </div>
        )}

        <CrmTabs
          active={tab}
          onChange={(id) => setTab(id as Tab)}
          items={[
            { id: "rates", label: "Expense categories & WHT rates", count: categories.length },
            { id: "vendors", label: "Vendors" },
            { id: "payable", label: "WHT payable" },
            { id: "year", label: "Financial year" },
          ]}
        />

        <section className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4 sm:p-6">
          {loading ? (
            <p className="py-16 text-center text-sm text-[var(--text-muted)]">Loading finance settings…</p>
          ) : (
            <>
              {tab === "rates" && <RatesTab categories={categories} onChanged={loadShared} />}
              {tab === "vendors" && <VendorsTab />}
              {tab === "payable" && <PayableTab />}
              {tab === "year" && settings && <YearTab settings={settings} onSaved={loadShared} />}
            </>
          )}
        </section>
      </div>
    </>
  );
}

// ── Expense categories & rates ────────────────────────────────────────────────

function RatesTab({ categories, onChanged }: { categories: ExpenseCategory[]; onChanged: () => Promise<void> }) {
  const [editing, setEditing] = useState<ExpenseCategory | null | "new">(null);
  const [showInactive, setShowInactive] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const visible = useMemo(
    () => categories.filter((c) => showInactive || c.isActive),
    [categories, showInactive]);

  const retire = async (category: ExpenseCategory) => {
    const used = category.expenseCount > 0;
    const message = used
      ? `"${category.name}" is used by ${category.expenseCount} expense(s), so it will be retired rather than deleted — the rate those expenses were entered at is kept. Continue?`
      : `Delete "${category.name}"? It has never been used.`;
    if (!window.confirm(message)) return;
    setBusy(true); setError(null);
    try {
      await whtApi.deleteCategory(category.id);
      await onChanged();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The category could not be removed.");
    } finally { setBusy(false); }
  };

  return (
    <div>
      <div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h2 className="text-lg font-semibold text-[var(--text-heading)]">Expense categories &amp; WHT rates</h2>
          <p className="mt-1 max-w-3xl text-sm text-[var(--text-muted)]">
            The rate an expense is entered at is copied onto that expense. Correcting a rate here
            changes what is withheld from future payments only — it never restates tax that has
            already been withheld or filed.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <label className="flex items-center gap-2 text-xs text-[var(--text-muted)]">
            <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
            Show retired
          </label>
          <Button size="sm" onClick={() => setEditing("new")}>+ Add category</Button>
        </div>
      </div>

      {error && <div className="mb-4"><ErrorBanner message={error} /></div>}

      <div className="overflow-x-auto">
        <table className="w-full min-w-[900px] text-left text-sm">
          <thead>
            <tr className="border-b border-[var(--border)] text-xs uppercase tracking-wider text-[var(--text-muted)]">
              <th className="px-3 py-3">Category</th>
              <th className="px-3 py-3">Section</th>
              <th className="px-3 py-3 text-right">Filer</th>
              <th className="px-3 py-3 text-right">Non-filer</th>
              <th className="px-3 py-3 text-right">Annual threshold</th>
              <th className="px-3 py-3 text-right">Used by</th>
              <th className="px-3 py-3" />
            </tr>
          </thead>
          <tbody>
            {visible.map((category) => (
              <tr key={category.id} className="border-b border-[var(--border)] last:border-0">
                <td className="px-3 py-3">
                  <p className="font-semibold text-[var(--text-heading)]">{category.name}</p>
                  <p className="text-xs text-[var(--text-muted)]">
                    <code>{category.code}</code>
                    {!category.isActive && <span className="ml-2 text-amber-400">Retired</span>}
                  </p>
                </td>
                <td className="px-3 py-3 text-[var(--text-secondary)]">
                  {category.isWhtApplicable
                    ? (category.taxSection ?? "—")
                    : <span className="text-[var(--text-muted)]">No withholding</span>}
                </td>
                <td className="px-3 py-3 text-right text-[var(--text-secondary)]">
                  {category.isWhtApplicable ? formatRate(category.filerRate) : "—"}
                </td>
                <td className="px-3 py-3 text-right text-[var(--text-secondary)]">
                  {category.isWhtApplicable ? formatRate(category.nonFilerRate) : "—"}
                </td>
                <td className="px-3 py-3 text-right text-[var(--text-secondary)]">
                  {!category.isWhtApplicable ? "—"
                    : category.annualThreshold > 0
                      ? category.annualThreshold.toLocaleString("en-PK")
                      : <span className="text-[var(--text-muted)]">From Rs 1</span>}
                </td>
                <td className="px-3 py-3 text-right text-[var(--text-muted)]">{category.expenseCount}</td>
                <td className="px-3 py-3">
                  <div className="flex justify-end gap-2">
                    <Button size="sm" variant="outline" onClick={() => setEditing(category)}>Edit</Button>
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() => void retire(category)}
                      className="text-xs font-semibold text-[var(--text-muted)] hover:text-rose-400 disabled:opacity-50"
                    >
                      {category.expenseCount > 0 ? "Retire" : "Delete"}
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {visible.length === 0 && (
          <p className="rounded-xl border border-dashed border-[var(--border)] py-16 text-center text-sm text-[var(--text-muted)]">
            No expense categories configured.
          </p>
        )}
      </div>

      {editing && (
        <CategoryModal
          item={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={async () => { setEditing(null); await onChanged(); }}
        />
      )}
    </div>
  );
}

function CategoryModal({ item, onClose, onSaved }: {
  item: ExpenseCategory | null;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [form, setForm] = useState({
    name: item?.name ?? "",
    code: item?.code ?? "",
    description: item?.description ?? "",
    isWhtApplicable: item?.isWhtApplicable ?? true,
    filerRate: String(item?.filerRate ?? 0),
    nonFilerRate: String(item?.nonFilerRate ?? 0),
    annualThreshold: String(item?.annualThreshold ?? 0),
    taxSection: item?.taxSection ?? "",
    displayOrder: String(item?.displayOrder ?? 900),
    isActive: item?.isActive ?? true,
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  const save = async () => {
    setSaving(true); setError(null);
    try {
      await whtApi.saveCategory(item?.id ?? null, {
        name: form.name,
        code: form.code || form.name,
        description: form.description || null,
        isWhtApplicable: form.isWhtApplicable,
        filerRate: Number(form.filerRate) || 0,
        nonFilerRate: Number(form.nonFilerRate) || 0,
        annualThreshold: Number(form.annualThreshold) || 0,
        taxSection: form.taxSection || null,
        displayOrder: Number(form.displayOrder) || 0,
        isActive: form.isActive,
        concurrencyToken: item?.concurrencyToken ?? null,
      });
      await onSaved();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The category could not be saved.");
    } finally { setSaving(false); }
  };

  return (
    <CrmModal
      open
      title={item ? `Edit ${item.name}` : "Add expense category"}
      subtitle={item && item.expenseCount > 0
        ? `${item.expenseCount} expense(s) already use this head. They keep the rate they were entered at.`
        : "Rates are percentages: enter 7.5 for 7.5%."}
      onClose={onClose}
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button disabled={saving} onClick={() => void save()}>{saving ? "Saving…" : "Save"}</Button>
        </div>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error} />}
        <div>
          <Label required>Category name</Label>
          <input className={inputClass} value={form.name} onChange={(e) => set("name", e.target.value)} />
        </div>
        <div>
          <Label required>Code</Label>
          <input
            className={inputClass}
            disabled={Boolean(item)}
            value={form.code}
            onChange={(e) => set("code", e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, "_"))}
          />
          <p className="mt-1 text-xs text-[var(--text-muted)]">
            {item ? "The code is fixed once expenses reference it." : "Leave blank to derive it from the name."}
          </p>
        </div>

        <label className="flex items-center gap-3 rounded-xl border border-[var(--border)] px-4 py-3 text-sm text-[var(--text-secondary)]">
          <input
            type="checkbox"
            checked={form.isWhtApplicable}
            onChange={(e) => set("isWhtApplicable", e.target.checked)}
          />
          Withholding tax is deducted from payments under this head
        </label>

        {form.isWhtApplicable ? (
          <>
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <Label required>Filer rate (%)</Label>
                <input className={inputClass} type="number" step="0.01" min="0" max="100"
                  value={form.filerRate} onChange={(e) => set("filerRate", e.target.value)} />
              </div>
              <div>
                <Label required>Non-filer rate (%)</Label>
                <input className={inputClass} type="number" step="0.01" min="0" max="100"
                  value={form.nonFilerRate} onChange={(e) => set("nonFilerRate", e.target.value)} />
              </div>
              <div>
                <Label>Tax section</Label>
                <input className={inputClass} placeholder="153(1)(a)"
                  value={form.taxSection} onChange={(e) => set("taxSection", e.target.value)} />
              </div>
              <div>
                <Label>Annual threshold (Rs)</Label>
                <input className={inputClass} type="number" step="1" min="0"
                  value={form.annualThreshold} onChange={(e) => set("annualThreshold", e.target.value)} />
              </div>
            </div>
            <p className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] px-4 py-3 text-xs text-[var(--text-muted)]">
              The threshold is an annual allowance per vendor, shared by every category with the same
              tax section — not a separate allowance per head. Nothing is withheld until a vendor's
              payments under that section pass it. Enter 0 to withhold from the first rupee.
            </p>
          </>
        ) : (
          <p className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] px-4 py-3 text-xs text-[var(--text-muted)]">
            Nothing will be deducted from payments under this head — use it for salaries (withheld by
            payroll under s.149), utilities (collected at source by the supplier), and heads that
            carry no withholding at all.
          </p>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label>Display order</Label>
            <input className={inputClass} type="number"
              value={form.displayOrder} onChange={(e) => set("displayOrder", e.target.value)} />
          </div>
          <div>
            <Label>Description</Label>
            <input className={inputClass} value={form.description} onChange={(e) => set("description", e.target.value)} />
          </div>
        </div>

        {item && (
          <label className="flex items-center gap-3 rounded-xl border border-[var(--border)] px-4 py-3 text-sm text-[var(--text-secondary)]">
            <input type="checkbox" checked={form.isActive} onChange={(e) => set("isActive", e.target.checked)} />
            Active — available when recording a new expense
          </label>
        )}
      </div>
    </CrmModal>
  );
}

// ── Vendors ───────────────────────────────────────────────────────────────────

function VendorsTab() {
  const [vendors, setVendors] = useState<Vendor[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Vendor | null | "new">(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try { setVendors((await whtApi.listVendors(search)).items); }
    catch (caught) { setError(caught instanceof Error ? caught.message : "Vendors could not be loaded."); }
    finally { setLoading(false); }
  }, [search]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 250);
    return () => window.clearTimeout(timer);
  }, [load]);

  return (
    <div>
      <div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h2 className="text-lg font-semibold text-[var(--text-heading)]">Vendors</h2>
          <p className="mt-1 max-w-3xl text-sm text-[var(--text-muted)]">
            A vendor's filer status decides which rate applies, and linking an expense to a vendor is
            what makes the annual threshold trackable. Expenses can still name a payee as free text —
            those are simply withheld at the non-filer rate.
          </p>
        </div>
        <Button size="sm" onClick={() => setEditing("new")}>+ Add vendor</Button>
      </div>

      {error && <div className="mb-4"><ErrorBanner message={error} onRetry={() => void load()} /></div>}

      <input
        className={`${inputClass} mb-4 max-w-md`}
        placeholder="Search by name, NTN, CNIC or phone"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        aria-label="Search vendors"
      />

      <div className="overflow-x-auto">
        <table className="w-full min-w-[860px] text-left text-sm">
          <thead>
            <tr className="border-b border-[var(--border)] text-xs uppercase tracking-wider text-[var(--text-muted)]">
              <th className="px-3 py-3">Vendor</th>
              <th className="px-3 py-3">Filer status</th>
              <th className="px-3 py-3">NTN / CNIC</th>
              <th className="px-3 py-3 text-right">Paid this year</th>
              <th className="px-3 py-3 text-right">Tax withheld</th>
              <th className="px-3 py-3 text-right">Expenses</th>
              <th className="px-3 py-3" />
            </tr>
          </thead>
          <tbody>
            {vendors.map((vendor) => (
              <tr key={vendor.id} className="border-b border-[var(--border)] last:border-0">
                <td className="px-3 py-3">
                  <p className="font-semibold text-[var(--text-heading)]">{vendor.name}</p>
                  {!vendor.isActive && <p className="text-xs text-amber-400">Inactive</p>}
                  {vendor.phone && <p className="text-xs text-[var(--text-muted)]">{vendor.phone}</p>}
                </td>
                <td className="px-3 py-3"><FilerBadge status={vendor.filerStatus} /></td>
                <td className="px-3 py-3 text-[var(--text-secondary)]">
                  {vendor.ntn ?? vendor.cnic ?? <span className="text-[var(--text-muted)]">Not recorded</span>}
                </td>
                <td className="px-3 py-3 text-right text-[var(--text-secondary)]">{formatRs(vendor.yearToDateGross)}</td>
                <td className="px-3 py-3 text-right text-[var(--text-secondary)]">{formatRs(vendor.yearToDateWht)}</td>
                <td className="px-3 py-3 text-right text-[var(--text-muted)]">{vendor.expenseCount}</td>
                <td className="px-3 py-3 text-right">
                  <Button size="sm" variant="outline" onClick={() => setEditing(vendor)}>Edit</Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {loading && <p className="py-10 text-center text-sm text-[var(--text-muted)]">Loading vendors…</p>}
        {!loading && vendors.length === 0 && (
          <p className="rounded-xl border border-dashed border-[var(--border)] py-16 text-center text-sm text-[var(--text-muted)]">
            No vendors yet. Add the suppliers you pay regularly — one-off payees can stay free text on the expense.
          </p>
        )}
      </div>

      {editing && (
        <VendorModal
          item={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={async () => { setEditing(null); await load(); }}
        />
      )}
    </div>
  );
}

function VendorModal({ item, onClose, onSaved }: {
  item: Vendor | null;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [form, setForm] = useState({
    name: item?.name ?? "",
    ntn: item?.ntn ?? "",
    cnic: item?.cnic ?? "",
    phone: item?.phone ?? "",
    address: item?.address ?? "",
    notes: item?.notes ?? "",
    filerStatus: (item?.filerStatus ?? "Unknown") as FilerStatus,
    markFilerStatusChecked: false,
    isActive: item?.isActive ?? true,
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  const save = async () => {
    setSaving(true); setError(null);
    try {
      await whtApi.saveVendor(item?.id ?? null, {
        ...form,
        ntn: form.ntn || null,
        cnic: form.cnic || null,
        phone: form.phone || null,
        address: form.address || null,
        notes: form.notes || null,
        concurrencyToken: item?.concurrencyToken ?? null,
      });
      await onSaved();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The vendor could not be saved.");
    } finally { setSaving(false); }
  };

  return (
    <CrmModal
      open
      title={item ? `Edit ${item.name}` : "Add vendor"}
      subtitle="Filer status is maintained by hand against the FBR Active Taxpayer List — there is no live lookup."
      onClose={onClose}
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button disabled={saving} onClick={() => void save()}>{saving ? "Saving…" : "Save"}</Button>
        </div>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error} />}
        <div>
          <Label required>Vendor name</Label>
          <input className={inputClass} value={form.name} onChange={(e) => set("name", e.target.value)} />
        </div>
        <div>
          <Label required>Filer status</Label>
          <select className={inputClass} value={form.filerStatus}
            onChange={(e) => set("filerStatus", e.target.value as FilerStatus)}>
            {FILER_STATUSES.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
          <p className="mt-1 text-xs text-[var(--text-muted)]">
            Unknown is withheld at the non-filer rate. Deducting too little is the penalised
            direction; deducting too much is refunded to the vendor when they file.
          </p>
        </div>
        <label className="flex items-center gap-3 rounded-xl border border-[var(--border)] px-4 py-3 text-sm text-[var(--text-secondary)]">
          <input type="checkbox" checked={form.markFilerStatusChecked}
            onChange={(e) => set("markFilerStatusChecked", e.target.checked)} />
          I have just verified this against the ATL
          {item?.filerStatusCheckedAt && (
            <span className="ml-auto text-xs text-[var(--text-muted)]">
              Last checked {new Date(item.filerStatusCheckedAt).toLocaleDateString("en-GB")}
            </span>
          )}
        </label>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label>NTN</Label>
            <input className={inputClass} value={form.ntn} onChange={(e) => set("ntn", e.target.value)} />
          </div>
          <div>
            <Label>CNIC</Label>
            <input className={inputClass} value={form.cnic} onChange={(e) => set("cnic", e.target.value)} />
          </div>
          <div>
            <Label>Phone</Label>
            <input className={inputClass} value={form.phone} onChange={(e) => set("phone", e.target.value)} />
          </div>
          <div>
            <Label>Address</Label>
            <input className={inputClass} value={form.address} onChange={(e) => set("address", e.target.value)} />
          </div>
        </div>
        <div>
          <Label>Notes</Label>
          <input className={inputClass} value={form.notes} onChange={(e) => set("notes", e.target.value)} />
        </div>
        {item && (
          <label className="flex items-center gap-3 rounded-xl border border-[var(--border)] px-4 py-3 text-sm text-[var(--text-secondary)]">
            <input type="checkbox" checked={form.isActive} onChange={(e) => set("isActive", e.target.checked)} />
            Active — available when recording a new expense
          </label>
        )}
      </div>
    </CrmModal>
  );
}

function FilerBadge({ status }: { status: FilerStatus }) {
  const tone = status === "Filer" ? "border-emerald-500/25 bg-emerald-500/10 text-emerald-400"
    : status === "NonFiler" ? "border-rose-500/25 bg-rose-500/10 text-rose-400"
    : "border-amber-500/25 bg-amber-500/10 text-amber-400";
  return <span className={`inline-flex rounded-full border px-2.5 py-1 text-[11px] font-semibold ${tone}`}>{filerLabel(status)}</span>;
}

// ── WHT payable & FBR deposits ────────────────────────────────────────────────

function PayableTab() {
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [summary, setSummary] = useState<WhtPayableSummary | null>(null);
  const [lines, setLines] = useState<WhtVendorLine[]>([]);
  const [deposits, setDeposits] = useState<WhtDeposit[]>([]);
  const [accounts, setAccounts] = useState<FinanceAccountOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<WhtDeposit | null | "new">(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try {
      const [summaryRow, vendorLines, depositRows] = await Promise.all([
        whtApi.payableSummary(from, to),
        whtApi.byVendor(from, to),
        whtApi.listDeposits(from, to),
      ]);
      setSummary(summaryRow); setLines(vendorLines); setDeposits(depositRows);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The withholding position could not be loaded.");
    } finally { setLoading(false); }
  }, [from, to]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    void api("/api/finance/accounts/options?includeInactive=true")
      .then(async (res) => { if (res.ok) setAccounts(await res.json()); })
      .catch(() => { /* the deposit form shows its own validation if accounts are unavailable */ });
  }, []);

  const removeDeposit = async (deposit: WhtDeposit) => {
    if (!window.confirm(`Delete the ${formatRs(deposit.amount)} deposit${deposit.challanNumber ? ` (${deposit.challanNumber})` : ""}? The amount goes back to being owed to FBR.`)) return;
    try { await whtApi.deleteDeposit(deposit.id); await load(); }
    catch (caught) { setError(caught instanceof Error ? caught.message : "The deposit could not be deleted."); }
  };

  return (
    <div>
      <div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h2 className="text-lg font-semibold text-[var(--text-heading)]">Withholding tax payable</h2>
          <p className="mt-1 max-w-3xl text-sm text-[var(--text-muted)]">
            Tax deducted from suppliers is money held on FBR's behalf. It stays in the account
            balance until it is deposited, so it is not available to spend.
          </p>
        </div>
        <div className="flex flex-wrap items-end gap-2">
          <div>
            <Label>From</Label>
            <input type="date" className={inputClass} value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div>
            <Label>To</Label>
            <input type="date" className={inputClass} value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
          <Button size="sm" variant="outline"
            onClick={() => void whtApi.downloadWhtStatement(from, to).catch((e: unknown) =>
              setError(e instanceof Error ? e.message : "The export failed."))}>
            Export CSV
          </Button>
          <Button size="sm" onClick={() => setEditing("new")}>+ Record deposit</Button>
        </div>
      </div>

      {error && <div className="mb-4"><ErrorBanner message={error} onRetry={() => void load()} /></div>}
      {loading && <p className="py-10 text-center text-sm text-[var(--text-muted)]">Loading…</p>}

      {summary && !loading && (
        <>
          <div className="mb-6 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Stat label="Still owed to FBR" value={formatRs(summary.outstandingPayable)} accent
              hint="All time, withheld less deposited" />
            <Stat label="Withheld in period" value={formatRs(summary.withheldInPeriod)}
              hint={`${summary.expenseCount} expense(s), ${summary.vendorCount} vendor(s)`} />
            <Stat label="Deposited in period" value={formatRs(summary.depositedInPeriod)} />
            <Stat label="Withheld all time" value={formatRs(summary.totalWithheldAllTime)}
              hint={`${formatRs(summary.totalDepositedAllTime)} deposited`} />
          </div>

          {summary.bySection.length > 0 && (
            <div className="mb-6 flex flex-wrap gap-2">
              {summary.bySection.map((section) => (
                <span key={section.taxSection}
                  className="rounded-full border border-[var(--border)] bg-[var(--surface-glass)] px-3 py-1.5 text-xs text-[var(--text-secondary)]">
                  <strong className="text-[var(--text-heading)]">{section.taxSection}</strong>
                  {" · "}{formatRs(section.whtAmount)} on {formatRs(section.grossAmount)}
                </span>
              ))}
            </div>
          )}

          <h3 className="mb-3 text-sm font-semibold uppercase tracking-wider text-[var(--text-muted)]">
            By vendor and section (s.165 statement)
          </h3>
          <div className="mb-8 overflow-x-auto">
            <table className="w-full min-w-[820px] text-left text-sm">
              <thead>
                <tr className="border-b border-[var(--border)] text-xs uppercase tracking-wider text-[var(--text-muted)]">
                  <th className="px-3 py-3">Vendor</th>
                  <th className="px-3 py-3">NTN / CNIC</th>
                  <th className="px-3 py-3">Status</th>
                  <th className="px-3 py-3">Section</th>
                  <th className="px-3 py-3 text-right">Gross</th>
                  <th className="px-3 py-3 text-right">Withheld</th>
                  <th className="px-3 py-3 text-right">Net paid</th>
                </tr>
              </thead>
              <tbody>
                {lines.map((line, index) => (
                  <tr key={`${line.vendorId ?? "free"}-${line.taxSection ?? "none"}-${index}`}
                    className="border-b border-[var(--border)] last:border-0">
                    <td className="px-3 py-3 font-medium text-[var(--text-heading)]">{line.vendorName}</td>
                    <td className="px-3 py-3 text-[var(--text-secondary)]">{line.ntn ?? line.cnic ?? "—"}</td>
                    <td className="px-3 py-3"><FilerBadge status={line.filerStatus} /></td>
                    <td className="px-3 py-3 text-[var(--text-secondary)]">{line.taxSection ?? "—"}</td>
                    <td className="px-3 py-3 text-right text-[var(--text-secondary)]">{formatRs(line.grossAmount)}</td>
                    <td className="px-3 py-3 text-right font-semibold text-[var(--text-heading)]">{formatRs(line.whtAmount)}</td>
                    <td className="px-3 py-3 text-right text-[var(--text-secondary)]">{formatRs(line.netPaid)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {lines.length === 0 && (
              <p className="rounded-xl border border-dashed border-[var(--border)] py-12 text-center text-sm text-[var(--text-muted)]">
                No tax was withheld in this period.
              </p>
            )}
          </div>

          <h3 className="mb-3 text-sm font-semibold uppercase tracking-wider text-[var(--text-muted)]">
            Deposits to FBR
          </h3>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-left text-sm">
              <thead>
                <tr className="border-b border-[var(--border)] text-xs uppercase tracking-wider text-[var(--text-muted)]">
                  <th className="px-3 py-3">Date</th>
                  <th className="px-3 py-3">Challan / CPR</th>
                  <th className="px-3 py-3">Paid from</th>
                  <th className="px-3 py-3">Covers</th>
                  <th className="px-3 py-3 text-right">Amount</th>
                  <th className="px-3 py-3" />
                </tr>
              </thead>
              <tbody>
                {deposits.map((deposit) => (
                  <tr key={deposit.id} className="border-b border-[var(--border)] last:border-0">
                    <td className="px-3 py-3 text-[var(--text-secondary)]">
                      {new Date(deposit.depositDate).toLocaleDateString("en-GB")}
                    </td>
                    <td className="px-3 py-3 text-[var(--text-heading)]">{deposit.challanNumber ?? "—"}</td>
                    <td className="px-3 py-3 text-[var(--text-secondary)]">{deposit.financeAccountName ?? "—"}</td>
                    <td className="px-3 py-3 text-[var(--text-muted)]">
                      {deposit.periodFrom && deposit.periodTo
                        ? `${new Date(deposit.periodFrom).toLocaleDateString("en-GB")} – ${new Date(deposit.periodTo).toLocaleDateString("en-GB")}`
                        : "—"}
                    </td>
                    <td className="px-3 py-3 text-right font-semibold text-[var(--text-heading)]">{formatRs(deposit.amount)}</td>
                    <td className="px-3 py-3">
                      <div className="flex justify-end gap-2">
                        <Button size="sm" variant="outline" onClick={() => setEditing(deposit)}>Edit</Button>
                        <button type="button" onClick={() => void removeDeposit(deposit)}
                          className="text-xs font-semibold text-[var(--text-muted)] hover:text-rose-400">Delete</button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {deposits.length === 0 && (
              <p className="rounded-xl border border-dashed border-[var(--border)] py-12 text-center text-sm text-[var(--text-muted)]">
                Nothing deposited in this period. Record a challan once the withheld tax has been paid
                to FBR — that is what takes it out of the account balance.
              </p>
            )}
          </div>
        </>
      )}

      {editing && (
        <DepositModal
          item={editing === "new" ? null : editing}
          accounts={accounts}
          suggested={summary?.outstandingPayable ?? 0}
          onClose={() => setEditing(null)}
          onSaved={async () => { setEditing(null); await load(); }}
        />
      )}
    </div>
  );
}

function DepositModal({ item, accounts, suggested, onClose, onSaved }: {
  item: WhtDeposit | null;
  accounts: FinanceAccountOption[];
  suggested: number;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [form, setForm] = useState({
    financeAccountId: item ? String(item.financeAccountId) : "",
    amount: item ? String(item.amount) : suggested > 0 ? String(suggested) : "",
    depositDate: (item?.depositDate ?? new Date().toISOString()).slice(0, 10),
    challanNumber: item?.challanNumber ?? "",
    periodFrom: item?.periodFrom?.slice(0, 10) ?? "",
    periodTo: item?.periodTo?.slice(0, 10) ?? "",
    notes: item?.notes ?? "",
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  const save = async () => {
    if (!form.financeAccountId) { setError("Select the account this was paid from."); return; }
    setSaving(true); setError(null);
    try {
      await whtApi.saveDeposit(item?.id ?? null, {
        financeAccountId: Number(form.financeAccountId),
        amount: Number(form.amount) || 0,
        depositDate: form.depositDate || null,
        challanNumber: form.challanNumber || null,
        periodFrom: form.periodFrom || null,
        periodTo: form.periodTo || null,
        notes: form.notes || null,
        concurrencyToken: item?.concurrencyToken ?? null,
      });
      await onSaved();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The deposit could not be saved.");
    } finally { setSaving(false); }
  };

  return (
    <CrmModal
      open
      title={item ? "Edit WHT deposit" : "Record WHT deposit"}
      subtitle="This reduces the account balance without being a business expense — the cost was already booked when the supplier was paid."
      onClose={onClose}
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button disabled={saving} onClick={() => void save()}>{saving ? "Saving…" : "Save"}</Button>
        </div>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error} />}
        <div>
          <Label required>Paid from account</Label>
          <select className={inputClass} value={form.financeAccountId}
            onChange={(e) => set("financeAccountId", e.target.value)}>
            <option value="">Select account</option>
            {accounts.filter((a) => a.isActive || String(a.id) === form.financeAccountId).map((a) => (
              <option key={a.id} value={a.id}>{a.name} — {a.accountHolderName}{a.isActive ? "" : " (Inactive)"}</option>
            ))}
          </select>
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label required>Amount (Rs)</Label>
            <input className={inputClass} type="number" step="0.01" min="0"
              value={form.amount} onChange={(e) => set("amount", e.target.value)} />
            {!item && suggested > 0 && (
              <p className="mt-1 text-xs text-[var(--text-muted)]">Currently owed: {formatRs(suggested)}</p>
            )}
          </div>
          <div>
            <Label required>Deposit date</Label>
            <input className={inputClass} type="date" value={form.depositDate}
              onChange={(e) => set("depositDate", e.target.value)} />
          </div>
          <div>
            <Label>Challan / CPR number</Label>
            <input className={inputClass} value={form.challanNumber}
              onChange={(e) => set("challanNumber", e.target.value)} />
          </div>
          <div>
            <Label>Notes</Label>
            <input className={inputClass} value={form.notes} onChange={(e) => set("notes", e.target.value)} />
          </div>
          <div>
            <Label>Period covered from</Label>
            <input className={inputClass} type="date" value={form.periodFrom}
              onChange={(e) => set("periodFrom", e.target.value)} />
          </div>
          <div>
            <Label>Period covered to</Label>
            <input className={inputClass} type="date" value={form.periodTo}
              onChange={(e) => set("periodTo", e.target.value)} />
          </div>
        </div>
      </div>
    </CrmModal>
  );
}

// ── Financial year ────────────────────────────────────────────────────────────

function YearTab({ settings, onSaved }: { settings: FinanceSettings; onSaved: () => Promise<void> }) {
  const [month, setMonth] = useState(String(settings.financialYearStartMonth));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const submit = async (extra: { markRatesConfirmed?: boolean; clearRatesConfirmation?: boolean }) => {
    setSaving(true); setError(null); setMessage(null);
    try {
      await whtApi.saveSettings({
        financialYearStartMonth: Number(month),
        markRatesConfirmed: false,
        clearRatesConfirmation: false,
        ...extra,
        concurrencyToken: settings.concurrencyToken,
      });
      await onSaved();
      setMessage("Finance settings saved.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Finance settings could not be saved.");
    } finally { setSaving(false); }
  };

  return (
    <div className="max-w-2xl">
      <h2 className="text-lg font-semibold text-[var(--text-heading)]">Financial year</h2>
      <p className="mt-1 text-sm text-[var(--text-muted)]">
        Annual withholding thresholds reset at the start of this month. Pakistan's tax year runs
        1 July – 30 June.
      </p>

      {error && <div className="mt-4"><ErrorBanner message={error} /></div>}
      {message && (
        <p className="mt-4 rounded-xl border border-emerald-500/25 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-300">{message}</p>
      )}

      <div className="mt-5 space-y-4">
        <div>
          <Label required>Year starts in</Label>
          <select className={inputClass} value={month} onChange={(e) => setMonth(e.target.value)}>
            {MONTHS.map((name, index) => <option key={name} value={index + 1}>{name}</option>)}
          </select>
          <p className="mt-1 text-xs text-[var(--text-muted)]">Current financial year: {settings.currentFinancialYear}</p>
        </div>
        <Button disabled={saving} onClick={() => void submit({})}>{saving ? "Saving…" : "Save"}</Button>
      </div>

      <hr className="my-8 border-[var(--border)]" />

      <h2 className="text-lg font-semibold text-[var(--text-heading)]">Rate confirmation</h2>
      <p className="mt-1 text-sm text-[var(--text-muted)]">
        The seeded rates are starting values taken from published summaries that disagree with each
        other. Confirming records that an accountant has checked them against the current Finance
        Act, and removes the warning banner.
      </p>
      <div className="mt-5">
        {settings.whtRatesConfirmedAt ? (
          <div className="space-y-3">
            <p className="rounded-xl border border-emerald-500/25 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-300">
              Confirmed {new Date(settings.whtRatesConfirmedAt).toLocaleDateString("en-GB")}
              {settings.whtRatesConfirmedByName ? ` by ${settings.whtRatesConfirmedByName}` : ""}.
            </p>
            <Button variant="outline" disabled={saving} onClick={() => void submit({ clearRatesConfirmation: true })}>
              Clear confirmation
            </Button>
          </div>
        ) : (
          <Button disabled={saving} onClick={() => void submit({ markRatesConfirmed: true })}>
            Mark rates as confirmed
          </Button>
        )}
      </div>
    </div>
  );
}

function Stat({ label, value, hint, accent }: { label: string; value: string; hint?: string; accent?: boolean }) {
  return (
    <div className={`rounded-xl border p-4 ${accent ? "border-amber-500/30 bg-amber-500/[0.07]" : "border-[var(--border)] bg-[var(--surface-glass)]"}`}>
      <p className="text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">{label}</p>
      <p className={`mt-1.5 text-xl font-bold ${accent ? "text-amber-300" : "text-[var(--text-heading)]"}`}>{value}</p>
      {hint && <p className="mt-1 text-xs text-[var(--text-muted)]">{hint}</p>}
    </div>
  );
}
