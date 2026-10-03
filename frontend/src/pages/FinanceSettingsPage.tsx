import AppSelect from "../lib/AppSelect.tsx";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { User } from "../App.tsx";
import { can } from "../features/access/permissions.ts";
import Button from "../lib/Button.tsx";
import { StatusBadge } from "../components/ui";
import { CrmModal, CrmTabs, ErrorBanner, inputClass, Label, StatePanel } from "../features/leads/CrmUi.tsx";
import * as whtApi from "../features/finance/whtApi.ts";
import { FinancialYearCard } from "../features/finance/FinancialYearCard.tsx";
import RevenueCategoriesPanel from "../features/finance/RevenueCategoriesPanel.tsx";
import { listRevenueCategories, type RevenueCategory } from "../features/finance/revenueCategoryApi.ts";
import {
  FILER_STATUSES,
  formatRate,
  formatRs,
  type ExpenseCategory,
  type FilerStatus,
  type FinanceSettings,
  type Vendor,
} from "../features/finance/whtTypes.ts";

type Props = { user: User | null };
type Tab = "rates" | "revenue" | "vendors" | "year";

export default function FinanceSettingsPage({ user }: Props) {
  const navigate = useNavigate();
  useEffect(() => { if (user && !can(user.role, "finance")) navigate("/"); }, [user, navigate]);

  if (!user) return <StatePanel title="Sign in required" message="Sign in with an Admin account to manage finance settings." />;
  if (!can(user.role, "finance")) return <StatePanel title="Finance access required" message="Only an Admin or Accountant can change withholding tax rates, vendors and the financial year." />;
  return <SettingsWorkspace />;
}

function SettingsWorkspace() {
  const [tab, setTab] = useState<Tab>("rates");
  const [settings, setSettings] = useState<FinanceSettings | null>(null);
  const [categories, setCategories] = useState<ExpenseCategory[]>([]);
  const [revenueCategories, setRevenueCategories] = useState<RevenueCategory[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const loadShared = useCallback(async () => {
    setLoading(true); setError(null);
    try {
      const [settingRow, categoryRows, revenueRows] = await Promise.all([
        whtApi.getSettings(),
        whtApi.listCategories(true),
        listRevenueCategories(true),
      ]);
      setSettings(settingRow);
      setCategories(categoryRows);
      setRevenueCategories(revenueRows);
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
              The heads income and spending are recorded under, the withholding tax deducted from
              supplier payments, and the vendors those rates depend on.
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
            { id: "revenue", label: "Revenue categories", count: revenueCategories.length },
            { id: "vendors", label: "Vendors" },
            { id: "year", label: "Financial year" },
          ]}
        />

        <section className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4 sm:p-6">
          {loading ? (
            <p className="py-16 text-center text-sm text-[var(--text-muted)]">Loading finance settings…</p>
          ) : (
            <>
              {tab === "rates" && <RatesTab categories={categories} onChanged={loadShared} />}
              {tab === "revenue" && <RevenueCategoriesPanel categories={revenueCategories} onChanged={loadShared} />}
              {tab === "vendors" && <VendorsTab />}
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
    const used = category.usageCount > 0;
    const message = used
      ? `"${category.name}" is used by ${category.usageCount} payment record(s), so it will be retired rather than deleted — the rate those records were entered at is kept. Continue?`
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
                <td className="px-3 py-3 text-right text-[var(--text-muted)]">{category.usageCount}</td>
                <td className="px-3 py-3">
                  <div className="flex justify-end gap-2">
                    <Button size="sm" variant="outline" onClick={() => setEditing(category)}>Edit</Button>
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() => void retire(category)}
                      className="text-xs font-semibold text-[var(--text-muted)] hover:text-rose-400 disabled:opacity-50"
                    >
                      {category.usageCount > 0 ? "Retire" : "Delete"}
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
      subtitle={item && item.usageCount > 0
        ? `${item.usageCount} payment record(s) already use this head. They keep the rate they were entered at.`
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
              <th className="px-3 py-3 text-right">Payments</th>
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
                <td className="px-3 py-3"><StatusBadge status={vendor.filerStatus} /></td>
                <td className="px-3 py-3 text-[var(--text-secondary)]">
                  {vendor.ntn ?? vendor.cnic ?? <span className="text-[var(--text-muted)]">Not recorded</span>}
                </td>
                <td className="px-3 py-3 text-right text-[var(--text-secondary)]">{formatRs(vendor.yearToDateGross)}</td>
                <td className="px-3 py-3 text-right text-[var(--text-secondary)]">{formatRs(vendor.yearToDateWht)}</td>
                <td className="px-3 py-3 text-right text-[var(--text-muted)]">{vendor.paymentCount}</td>
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
          <AppSelect className={inputClass} value={form.filerStatus}
            onChange={(e) => set("filerStatus", e.target.value as FilerStatus)}>
            {FILER_STATUSES.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </AppSelect>
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

// ── Financial year ────────────────────────────────────────────────────────────

function YearTab({ settings, onSaved }: { settings: FinanceSettings; onSaved: () => Promise<void> }) {
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  // The Rate confirmation buttons below keep the saved year and send no go-live date.
  const submit = async (extra: { markRatesConfirmed?: boolean; clearRatesConfirmation?: boolean }) => {
    setSaving(true); setError(null); setMessage(null);
    try {
      await whtApi.saveSettings({
        financialYearStartMonth: settings.financialYearStartMonth,
        goLiveDate: null,
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
      <FinancialYearCard settings={settings} onSaved={onSaved} />

      <hr className="my-8 border-[var(--border)]" />

      <h2 className="text-lg font-semibold text-[var(--text-heading)]">Rate confirmation</h2>
      <p className="mt-1 text-sm text-[var(--text-muted)]">
        The seeded rates are starting values taken from published summaries that disagree with each
        other. Confirming records that an accountant has checked them against the current Finance
        Act, and removes the warning banner.
      </p>
      {error && <div className="mt-4"><ErrorBanner message={error} /></div>}
      {message && (
        <p className="mt-4 rounded-xl border border-emerald-500/25 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-300">{message}</p>
      )}
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
