import AppSelect from "../lib/AppSelect.tsx";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Modal from "../lib/Modal.tsx";
import { documentJson, jsonBody } from "../features/customerDocuments/documentApi.ts";
import type { AssignmentMode, DocumentCategory } from "../features/customerDocuments/types.ts";

type Props = { user: User | null };
type CustomerOption = { id: number; fullName: string; phone: string; status: string };

const blank = { name: "", code: "", description: "", required: true, displayOrder: "100", maxMb: "10", dueDays: "", active: true, future: false, assignment: "None" as AssignmentMode, selected: [] as number[], types: [".pdf", ".jpg", ".jpeg", ".png"] };

export default function CustomerDocumentCategoriesPage({ user }: Props) {
  const [categories, setCategories] = useState<DocumentCategory[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<DocumentCategory | "new" | null>(null);
  const [assigning, setAssigning] = useState<DocumentCategory | null>(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try { setCategories(await documentJson<DocumentCategory[]>("/api/customer-documents/categories?includeInactive=true")); }
    catch (caught) { setError(caught instanceof Error ? caught.message : "Document categories could not be loaded."); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { if (user?.role === "Admin") void load(); }, [load, user]);

  if (user?.role !== "Admin") return <Container className="py-16 text-center text-[var(--text-muted)]">Admin access required.</Container>;

  const remove = async (category: DocumentCategory) => {
    if (!window.confirm(`Delete unused category “${category.name}”? This cannot be undone.`)) return;
    try { await documentJson(`/api/customer-documents/categories/${category.id}`, { method: "DELETE" }); await load(); }
    catch (caught) { setError(caught instanceof Error ? caught.message : "Category could not be deleted."); }
  };

  return <Container className="py-10">
    <div className="mb-8 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between"><div><h1 className="text-2xl font-bold text-[var(--text-heading)]">Customer document categories</h1><p className="mt-1 text-sm text-[var(--text-muted)]">Reusable checklist definitions and explicit customer assignment rules.</p></div><div className="flex gap-2"><Button variant="outline" onClick={() => location.assign("/customers")}>← Customers</Button><Button onClick={() => setEditing("new")}>+ New category</Button></div></div>
    {error && <div role="alert" className="mb-5 rounded-xl border border-rose-500/20 bg-rose-500/10 p-4 text-sm text-rose-300">{error}</div>}
    {loading ? <State text="Loading document categories…" /> : categories.length === 0 ? <State text="No document categories configured." /> : <div className="overflow-x-auto rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]"><table className="w-full min-w-[940px] text-left text-sm"><thead><tr className="border-b border-[var(--border)] text-xs uppercase tracking-wide text-[var(--text-muted)]">{["Category", "Code", "Rule", "Files", "Usage", "Status", ""].map((h) => <th key={h} className="px-4 py-3">{h}</th>)}</tr></thead><tbody>{categories.map((category) => <tr key={category.id} className="border-b border-[var(--border)] last:border-0"><td className="px-4 py-4"><p className="font-medium text-[var(--text-heading)]">{category.name}</p><p className="mt-1 max-w-xs text-xs text-[var(--text-muted)]">{category.description ?? "No description"}</p></td><td className="px-4 py-4"><code className="text-xs">{category.code}</code></td><td className="px-4 py-4 text-[var(--text-secondary)]">{category.isRequiredByDefault ? "Required" : "Optional"}<br/><span className="text-xs text-[var(--text-muted)]">{category.assignToNewCustomers ? "New customers" : "Manual assignment"}</span></td><td className="px-4 py-4 text-xs text-[var(--text-secondary)]">{category.allowedFileTypes.join(", ").toUpperCase()}<br/>{category.maxFileSizeBytes / 1024 / 1024} MB</td><td className="px-4 py-4 text-[var(--text-secondary)]">{category.usageCount}</td><td className="px-4 py-4"><span className={`rounded-full border px-2.5 py-1 text-xs ${category.isActive ? "border-emerald-500/25 bg-emerald-500/10 text-emerald-300" : "border-amber-500/25 bg-amber-500/10 text-amber-300"}`}>{category.isActive ? "Active" : "Inactive"}</span></td><td className="px-4 py-4"><div className="flex justify-end gap-2"><Button size="sm" variant="outline" onClick={() => setEditing(category)}>Edit</Button>{category.isActive && <Button size="sm" variant="ghost" onClick={() => setAssigning(category)}>Assign</Button>}{category.usageCount === 0 && <Button size="sm" variant="ghost" onClick={() => void remove(category)}>Delete</Button>}</div></td></tr>)}</tbody></table></div>}
    <CategoryModal item={editing} onClose={() => setEditing(null)} onSaved={async () => { setEditing(null); await load(); }} />
    <AssignmentModal category={assigning} onClose={() => setAssigning(null)} onSaved={async () => { setAssigning(null); await load(); }} />
  </Container>;
}

function CategoryModal({ item, onClose, onSaved }: { item: DocumentCategory | "new" | null; onClose: () => void; onSaved: () => Promise<void> }) {
  const current = item && item !== "new" ? item : null;
  const [form, setForm] = useState(blank);
  const [saving, setSaving] = useState(false); const [error, setError] = useState<string | null>(null);
  useEffect(() => { if (!item) return; setError(null); setForm(current ? { name: current.name, code: current.code, description: current.description ?? "", required: current.isRequiredByDefault, displayOrder: String(current.displayOrder), maxMb: String(current.maxFileSizeBytes / 1024 / 1024), dueDays: current.defaultDueDays?.toString() ?? "", active: current.isActive, future: current.assignToNewCustomers, assignment: "None", selected: [], types: current.allowedFileTypes } : { ...blank, selected: [], types: [...blank.types] }); }, [item, current]);
  if (!item) return null;
  const toggleType = (type: string) => setForm((f) => ({ ...f, types: f.types.includes(type) ? f.types.filter((v) => v !== type) : [...f.types, type] }));
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!current && ["AllActiveCustomers", "SelectedCustomers"].includes(form.assignment)) {
      const target = form.assignment === "AllActiveCustomers" ? "every existing active customer" : `${form.selected.length} selected customer(s)`;
      if (!window.confirm(`Create “${form.name}” and assign it to ${target}? Existing assignments will not be duplicated.`)) return;
    }
    setSaving(true); setError(null);
    try {
      const common = { name: form.name, code: form.code, description: form.description || null, isRequiredByDefault: form.required, displayOrder: Number(form.displayOrder), allowedFileTypes: form.types, maxFileSizeBytes: Math.round(Number(form.maxMb) * 1024 * 1024), defaultDueDays: form.dueDays ? Number(form.dueDays) : null };
      if (current) await documentJson(`/api/customer-documents/categories/${current.id}`, jsonBody("PUT", { ...common, isActive: form.active, assignToNewCustomers: form.future, concurrencyToken: current.concurrencyToken }));
      else await documentJson("/api/customer-documents/categories", jsonBody("POST", { ...common, assignmentMode: form.assignment, selectedCustomerIds: form.selected }));
      await onSaved();
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Category could not be saved."); }
    finally { setSaving(false); }
  };
  return <Dialog open title={current ? "Edit document category" : "Create document category"} subtitle={current?.usageCount ? "The stable code is locked because this category has customer history." : "Choose exactly how the new category should be applied."} onClose={() => !saving && onClose()}><form onSubmit={(e) => void submit(e)} className="space-y-4">{error && <FormError text={error} />}<div className="grid gap-4 sm:grid-cols-2"><Text label="Name" required value={form.name} set={(name) => setForm({ ...form, name })} /><Text label="Unique code" required disabled={Boolean(current?.usageCount)} value={form.code} set={(code) => setForm({ ...form, code: code.toLowerCase().replace(/[^a-z0-9_-]/g, "_") })} /><Text label="Display order" required type="number" value={form.displayOrder} set={(displayOrder) => setForm({ ...form, displayOrder })} /><Text label="Maximum size (MB)" required type="number" value={form.maxMb} set={(maxMb) => setForm({ ...form, maxMb })} /><Text label="Default due period (days)" type="number" value={form.dueDays} set={(dueDays) => setForm({ ...form, dueDays })} /></div><Field label="Description"><textarea rows={3} className={inputClass} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></Field><div><p className="mb-2 text-sm text-[var(--text-secondary)]">Allowed file types</p><div className="flex flex-wrap gap-3">{[".pdf", ".jpg", ".jpeg", ".png"].map((type) => <label key={type} className="flex items-center gap-2 rounded-lg border border-[var(--border)] px-3 py-2 text-sm"><input type="checkbox" checked={form.types.includes(type)} onChange={() => toggleType(type)} />{type.toUpperCase()}</label>)}</div></div><label className="flex items-center gap-3 text-sm"><input type="checkbox" checked={form.required} onChange={(e) => setForm({ ...form, required: e.target.checked })} />Required by default</label>
    {current ? <><label className="flex items-center gap-3 text-sm"><input type="checkbox" checked={form.active} onChange={(e) => setForm({ ...form, active: e.target.checked })} />Active category</label><label className="flex items-center gap-3 text-sm"><input type="checkbox" checked={form.future} disabled={!form.active} onChange={(e) => setForm({ ...form, future: e.target.checked })} />Assign to new customers</label></> : <><Field label="Apply category"><AppSelect className={inputClass} value={form.assignment} onChange={(e) => setForm({ ...form, assignment: e.target.value as AssignmentMode })}><option value="None">Do not assign automatically</option><option value="NewCustomersOnly">New customers only</option><option value="AllActiveCustomers">All existing active customers</option><option value="SelectedCustomers">Selected customers</option></AppSelect></Field>{form.assignment === "SelectedCustomers" && <CustomerPicker selected={form.selected} setSelected={(selected) => setForm({ ...form, selected })} />}</>}
    <Footer saving={saving} onClose={onClose} label="Save category" /></form></Dialog>;
}

function AssignmentModal({ category, onClose, onSaved }: { category: DocumentCategory | null; onClose: () => void; onSaved: () => Promise<void> }) {
  const [mode, setMode] = useState<AssignmentMode>("AllActiveCustomers"); const [selected, setSelected] = useState<number[]>([]); const [saving, setSaving] = useState(false); const [error, setError] = useState<string | null>(null);
  useEffect(() => { setMode("AllActiveCustomers"); setSelected([]); setError(null); }, [category]);
  if (!category) return null;
  const submit = async (event: FormEvent) => { event.preventDefault(); const target = mode === "AllActiveCustomers" ? "all existing active customers" : mode === "SelectedCustomers" ? `${selected.length} selected customer(s)` : mode === "NewCustomersOnly" ? "new customers only" : "no customers automatically"; if (!window.confirm(`Apply “${category.name}” to ${target}? Duplicate requirements are prevented.`)) return; setSaving(true); setError(null); try { await documentJson(`/api/customer-documents/categories/${category.id}/assign`, jsonBody("POST", { assignmentMode: mode, selectedCustomerIds: selected })); await onSaved(); } catch (caught) { setError(caught instanceof Error ? caught.message : "Assignment could not be completed."); } finally { setSaving(false); } };
  return <Dialog open title={`Assign ${category.name}`} subtitle="Review the target before confirming. Repeating an assignment is safe and does not create duplicates." onClose={() => !saving && onClose()}><form onSubmit={(e) => void submit(e)} className="space-y-4">{error && <FormError text={error} />}<Field label="Apply to"><AppSelect className={inputClass} value={mode} onChange={(e) => setMode(e.target.value as AssignmentMode)}><option value="AllActiveCustomers">All existing active customers</option><option value="SelectedCustomers">Selected customers</option><option value="NewCustomersOnly">New customers only</option><option value="None">Do not assign automatically</option></AppSelect></Field>{mode === "SelectedCustomers" && <CustomerPicker selected={selected} setSelected={setSelected} />}<Footer saving={saving} onClose={onClose} label="Confirm assignment" /></form></Dialog>;
}

function CustomerPicker({ selected, setSelected }: { selected: number[]; setSelected: (ids: number[]) => void }) {
  const [search, setSearch] = useState(""); const [rows, setRows] = useState<CustomerOption[]>([]); const [loading, setLoading] = useState(false);
  useEffect(() => { const handle = window.setTimeout(() => { setLoading(true); const query = new URLSearchParams({ page: "1", pageSize: "50" }); if (search.trim()) query.set("search", search.trim()); void documentJson<{ items: CustomerOption[] }>(`/api/Customer?${query}`).then((data) => setRows(data.items ?? [])).catch(() => setRows([])).finally(() => setLoading(false)); }, 250); return () => window.clearTimeout(handle); }, [search]);
  const toggle = (id: number) => setSelected(selected.includes(id) ? selected.filter((value) => value !== id) : [...selected, id]);
  return <div className="rounded-xl border border-[var(--border)] p-3"><label className="text-sm text-[var(--text-secondary)]">Find customers<input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Name, phone, CNIC…" className={`${inputClass} mt-2`} /></label><p className="mt-2 text-xs text-[var(--text-muted)]">{selected.length} selected. Search results are limited to 50 at a time.</p><div className="mt-3 max-h-48 space-y-1 overflow-y-auto">{loading ? <p className="p-3 text-sm text-[var(--text-muted)]">Searching…</p> : rows.map((customer) => <label key={customer.id} className="flex cursor-pointer items-center gap-3 rounded-lg p-2 text-sm hover:bg-[var(--surface-glass-hover)]"><input type="checkbox" checked={selected.includes(customer.id)} onChange={() => toggle(customer.id)} /><span><span className="block text-[var(--text-primary)]">{customer.fullName}</span><span className="text-xs text-[var(--text-muted)]">{customer.phone} · {customer.status}</span></span></label>)}</div></div>;
}

function Dialog({ open, title, subtitle, onClose, children }: { open: boolean; title: string; subtitle: string; onClose: () => void; children: React.ReactNode }) { return <Modal open={open} onClose={onClose} align="top"><div role="dialog" aria-modal="true" aria-labelledby="category-dialog-title" className="relative my-8 w-full max-w-2xl rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-2xl"><h2 id="category-dialog-title" className="text-lg font-semibold text-[var(--text-heading)]">{title}</h2><p className="mt-1 text-sm text-[var(--text-muted)]">{subtitle}</p><div className="mt-5">{children}</div></div></Modal>; }
function Footer({ saving, onClose, label }: { saving: boolean; onClose: () => void; label: string }) { return <div className="flex justify-end gap-2 pt-2"><Button type="button" variant="ghost" disabled={saving} onClick={onClose}>Cancel</Button><Button type="submit" disabled={saving}>{saving ? "Saving…" : label}</Button></div>; }
function Field({ label, children }: { label: string; children: React.ReactNode }) { return <label className="block text-sm text-[var(--text-secondary)]">{label}<span className="mt-2 block">{children}</span></label>; }
function Text({ label, value, set, required, type = "text", disabled }: { label: string; value: string; set: (value: string) => void; required?: boolean; type?: string; disabled?: boolean }) { return <Field label={`${label}${required ? " *" : ""}`}><input className={inputClass} required={required} disabled={disabled} type={type} min={type === "number" ? 0 : undefined} value={value} onChange={(e) => set(e.target.value)} /></Field>; }
function FormError({ text }: { text: string }) { return <p role="alert" className="rounded-xl bg-rose-500/10 p-3 text-sm text-rose-300">{text}</p>; }
function State({ text }: { text: string }) { return <div className="rounded-2xl border border-dashed border-[var(--border)] p-16 text-center text-sm text-[var(--text-muted)]">{text}</div>; }
const inputClass = "w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2.5 text-[var(--text-primary)] outline-none focus:border-indigo-500/60";
