import { useMemo, useState } from "react";
import Button from "../../lib/Button.tsx";
import { CrmModal, ErrorBanner, inputClass, Label } from "../leads/CrmUi.tsx";
import {
  deleteRevenueCategory,
  saveRevenueCategory,
  type RevenueCategory,
} from "./revenueCategoryApi.ts";

/**
 * Revenue heads, managed the same way expense heads are on the tab next door — added, renamed,
 * reordered and retired without a developer.
 *
 * The one rule that matters is retiring. A head that revenue has been filed under keeps its row
 * forever, because the name was copied onto each revenue entry when it was recorded and the
 * income statements already issued read from that copy. Retiring takes the head off new entries
 * and leaves every past report exactly as it was published. A head nothing has ever used has no
 * such history to protect, so it is deleted outright rather than left in the list as clutter.
 */
export default function RevenueCategoriesPanel({ categories, onChanged }: {
  categories: RevenueCategory[];
  onChanged: () => Promise<void>;
}) {
  const [editing, setEditing] = useState<RevenueCategory | null | "new">(null);
  const [showInactive, setShowInactive] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const visible = useMemo(
    () => categories.filter((c) => showInactive || c.isActive),
    [categories, showInactive]);

  const retire = async (category: RevenueCategory) => {
    const used = category.revenueCount > 0;
    const message = used
      ? `"${category.name}" is used by ${category.revenueCount} revenue entr${category.revenueCount === 1 ? "y" : "ies"}, so it will be retired rather than deleted — those entries keep the name they were recorded under. Continue?`
      : `Delete "${category.name}"? It has never been used.`;
    if (!window.confirm(message)) return;
    setBusy(true); setError(null);
    try {
      await deleteRevenueCategory(category.id);
      await onChanged();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The category could not be removed.");
    } finally { setBusy(false); }
  };

  return (
    <div>
      <div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h2 className="text-lg font-semibold text-[var(--text-heading)]">Revenue categories</h2>
          <p className="mt-1 max-w-3xl text-sm text-[var(--text-muted)]">
            The heads income is recorded under, and the order they appear in on the Profit &amp; Loss.
            The name is copied onto each revenue entry as it is recorded, so correcting a name here
            changes what future entries are filed under — it never restates a statement already
            issued.
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
        <table className="w-full min-w-[720px] text-left text-sm">
          <thead>
            <tr className="border-b border-[var(--border)] text-xs uppercase tracking-wider text-[var(--text-muted)]">
              <th className="px-3 py-3">Category</th>
              <th className="px-3 py-3">Description</th>
              <th className="px-3 py-3 text-right">Order</th>
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
                  {category.description ?? <span className="text-[var(--text-muted)]">—</span>}
                </td>
                <td className="px-3 py-3 text-right text-[var(--text-secondary)]">{category.displayOrder}</td>
                <td className="px-3 py-3 text-right text-[var(--text-muted)]">{category.revenueCount}</td>
                <td className="px-3 py-3">
                  <div className="flex justify-end gap-2">
                    <Button size="sm" variant="outline" onClick={() => setEditing(category)}>Edit</Button>
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() => void retire(category)}
                      className="text-xs font-semibold text-[var(--text-muted)] hover:text-rose-400 disabled:opacity-50"
                    >
                      {category.revenueCount > 0 ? "Retire" : "Delete"}
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {visible.length === 0 && (
          <p className="rounded-xl border border-dashed border-[var(--border)] py-16 text-center text-sm text-[var(--text-muted)]">
            No revenue categories configured.
          </p>
        )}
      </div>

      {editing && (
        <RevenueCategoryModal
          item={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={async () => { setEditing(null); await onChanged(); }}
        />
      )}
    </div>
  );
}

function RevenueCategoryModal({ item, onClose, onSaved }: {
  item: RevenueCategory | null;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [form, setForm] = useState({
    name: item?.name ?? "",
    code: item?.code ?? "",
    description: item?.description ?? "",
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
      await saveRevenueCategory(item?.id ?? null, {
        name: form.name,
        code: form.code || form.name,
        description: form.description || null,
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
      title={item ? `Edit ${item.name}` : "Add revenue category"}
      subtitle={item && item.revenueCount > 0
        ? `${item.revenueCount} revenue entr${item.revenueCount === 1 ? "y is" : "ies are"} already filed under this head. They keep the name they were recorded under.`
        : "The head income is recorded under, and how it is titled on the Profit & Loss."}
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
            {item ? "The code is fixed once revenue references it." : "Leave blank to derive it from the name."}
          </p>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label>Display order</Label>
            <input className={inputClass} type="number"
              value={form.displayOrder} onChange={(e) => set("displayOrder", e.target.value)} />
            <p className="mt-1 text-xs text-[var(--text-muted)]">
              Lower numbers come first on the Profit &amp; Loss.
            </p>
          </div>
          <div>
            <Label>Description</Label>
            <input className={inputClass} value={form.description} onChange={(e) => set("description", e.target.value)} />
          </div>
        </div>

        {item && (
          <label className="flex items-center gap-3 rounded-xl border border-[var(--border)] px-4 py-3 text-sm text-[var(--text-secondary)]">
            <input type="checkbox" checked={form.isActive} onChange={(e) => set("isActive", e.target.checked)} />
            Active — available when recording new revenue
          </label>
        )}
      </div>
    </CrmModal>
  );
}
