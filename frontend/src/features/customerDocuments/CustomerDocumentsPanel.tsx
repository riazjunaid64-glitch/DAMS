import { useEffect, useMemo, useState, type FormEvent } from "react";
import Button from "../../lib/Button.tsx";
import Modal from "../../lib/Modal.tsx";
import { documentJson, jsonBody, openPrivateDocument } from "./documentApi.ts";
import { canOverride, matchesDocumentFilter, statusLabel, statusTone, uploadAllowed, type DocumentFilter } from "./documentState.ts";
import DocumentSummaryBadge from "./DocumentSummaryBadge.tsx";
import type { AssignmentMode, DocumentCategory, DocumentChecklist, DocumentRequirement, DocumentStatus, DocumentVersion } from "./types.ts";

type ActionKind = "upload" | "approve" | "reject" | "replacement" | "postpone" | "waive" | "notApplicable" | "requested" | "expired" | "due";
type ActionState = { kind: ActionKind; requirement: DocumentRequirement } | null;

const FILTERS: { id: DocumentFilter; label: string }[] = [
  { id: "all", label: "All" }, { id: "missing", label: "Missing" },
  { id: "review", label: "Awaiting review" }, { id: "approved", label: "Approved" },
  { id: "rejected", label: "Rejected" }, { id: "replacement", label: "Replacement required" },
  { id: "postponed", label: "Postponed" }, { id: "waived", label: "Waived" },
  { id: "notApplicable", label: "Not applicable" }, { id: "expired", label: "Expired" },
];

export default function CustomerDocumentsPanel({ customerId, checklist, loading, error, onRefresh }: {
  customerId: number;
  checklist: DocumentChecklist | null;
  loading: boolean;
  error: string | null;
  onRefresh: () => Promise<void>;
}) {
  const [filter, setFilter] = useState<DocumentFilter>("all");
  const [sort, setSort] = useState("required");
  const [action, setAction] = useState<ActionState>(null);
  const [adding, setAdding] = useState(false);
  const [categories, setCategories] = useState<DocumentCategory[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);

  useEffect(() => {
    void documentJson<DocumentCategory[]>("/api/customer-documents/categories")
      .then(setCategories)
      .catch(() => setCategories([]));
  }, []);

  const rows = useMemo(() => {
    const filtered = (checklist?.requirements ?? []).filter((r) => matchesDocumentFilter(r, filter));
    return [...filtered].sort((a, b) => {
      if (sort === "recent") return new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime();
      if (sort === "display") return a.displayOrder - b.displayOrder || a.name.localeCompare(b.name);
      if (sort === "missing") return Number(a.status !== "Missing") - Number(b.status !== "Missing") || a.displayOrder - b.displayOrder;
      return Number(b.isRequired) - Number(a.isRequired) || a.displayOrder - b.displayOrder;
    });
  }, [checklist, filter, sort]);

  const refresh = async (message: string) => {
    setNotice(message); setLocalError(null); await onRefresh();
  };

  if (loading && !checklist) return <State text="Loading document checklist…" />;
  if (error && !checklist) return <State text={error} error onRetry={() => void onRefresh()} />;
  if (!checklist) return null;

  return (
    <div className="space-y-5">
      <section className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="text-lg font-semibold text-[var(--text-heading)]">Customer documents</h2>
            <p className="mt-1 text-sm text-[var(--text-muted)]">Private identity and supporting records. Missing items are warnings and do not stop other work.</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <DocumentSummaryBadge summary={checklist.summary} />
            <Button size="sm" onClick={() => setAdding(true)}>+ Add requirement</Button>
          </div>
        </div>
        <div className="mt-4 h-2 overflow-hidden rounded-full bg-[var(--surface-glass-hover)]" aria-label={`${checklist.summary.completionPercent}% complete`}>
          <div className="h-full rounded-full bg-emerald-500 transition-all" style={{ width: `${checklist.summary.completionPercent}%` }} />
        </div>
      </section>

      {(notice || localError || error) && <div role="status" className={`rounded-xl border px-4 py-3 text-sm ${localError || error ? "border-rose-500/20 bg-rose-500/10 text-rose-300" : "border-emerald-500/20 bg-emerald-500/10 text-emerald-300"}`}>{localError ?? error ?? notice}</div>}

      <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex gap-2 overflow-x-auto pb-1" role="tablist" aria-label="Filter document checklist">
          {FILTERS.map((item) => <button key={item.id} type="button" role="tab" aria-selected={filter === item.id}
            onClick={() => setFilter(item.id)} className={`whitespace-nowrap rounded-full border px-3 py-1.5 text-xs ${filter === item.id ? "border-indigo-400 bg-indigo-500/15 text-indigo-300" : "border-[var(--border)] text-[var(--text-muted)] hover:text-[var(--text-primary)]"}`}>{item.label}</button>)}
        </div>
        <label className="flex items-center gap-2 text-xs text-[var(--text-muted)]">Sort
          <select value={sort} onChange={(e) => setSort(e.target.value)} className="rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-[var(--text-primary)]">
            <option value="required">Required first</option><option value="missing">Missing first</option>
            <option value="display">Display order</option><option value="recent">Recently updated</option>
          </select>
        </label>
      </div>

      {rows.length === 0 ? <State text="No documents match this filter." /> : (
        <div className="grid gap-4">
          {rows.map((requirement) => <RequirementCard key={requirement.id} customerId={customerId} requirement={requirement}
            onAction={(kind) => { setNotice(null); setLocalError(null); setAction({ kind, requirement }); }}
            onFileError={setLocalError} />)}
        </div>
      )}

      <ActionModal customerId={customerId} action={action} onClose={() => setAction(null)} onSaved={async (message) => { setAction(null); await refresh(message); }} />
      <AddRequirementModal customerId={customerId} open={adding} categories={categories} existing={checklist.requirements}
        onClose={() => setAdding(false)} onSaved={async () => { setAdding(false); await refresh("Document requirement added."); }} />
    </div>
  );
}

function RequirementCard({ customerId, requirement, onAction, onFileError }: {
  customerId: number; requirement: DocumentRequirement; onAction: (kind: ActionKind) => void; onFileError: (message: string) => void;
}) {
  const [versions, setVersions] = useState<DocumentVersion[]>(requirement.versions);
  const [hasMoreVersions, setHasMoreVersions] = useState(requirement.hasMoreVersions);
  const [versionsBusy, setVersionsBusy] = useState(false);
  useEffect(() => {
    setVersions(requirement.versions);
    setHasMoreVersions(requirement.hasMoreVersions);
  }, [requirement.updatedAt, requirement.versions, requirement.hasMoreVersions]);
  const postponedDue = requirement.status === "Postponed"
    && !!requirement.postponedUntil
    && new Date(requirement.postponedUntil).getTime() <= Date.now();
  const tone = statusTone(requirement.status);
  const badge = tone === "emerald" ? "border-emerald-500/25 bg-emerald-500/10 text-emerald-300"
    : tone === "rose" ? "border-rose-500/25 bg-rose-500/10 text-rose-300"
    : tone === "amber" ? "border-amber-500/25 bg-amber-500/10 text-amber-300"
    : tone === "violet" ? "border-violet-500/25 bg-violet-500/10 text-violet-300"
    : "border-[var(--border)] bg-[var(--surface-glass-hover)] text-[var(--text-secondary)]";
  const open = (download: boolean, version = requirement.latestVersion) => {
    if (!version) return;
    void openPrivateDocument(customerId, requirement.id, version.id, version.originalFileName, download)
      .catch((caught) => onFileError(caught instanceof Error ? caught.message : "The private document could not be opened."));
  };
  const loadOlderVersions = async () => {
    const beforeVersionNumber = versions[versions.length - 1]?.versionNumber;
    if (!beforeVersionNumber || versionsBusy) return;
    setVersionsBusy(true);
    try {
      const page = await documentJson<{ items: DocumentVersion[]; hasMore: boolean }>(
        `/api/customer-documents/customers/${customerId}/requirements/${requirement.id}/versions?beforeVersionNumber=${beforeVersionNumber}&take=20`,
      );
      setVersions((current) => [...current, ...page.items.filter((item) => !current.some((row) => row.id === item.id))]);
      setHasMoreVersions(page.hasMore);
    } catch (caught) {
      onFileError(caught instanceof Error ? caught.message : "Older document versions could not be loaded.");
    } finally {
      setVersionsBusy(false);
    }
  };
  return (
    <article className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="font-semibold text-[var(--text-heading)]">{requirement.name}</h3>
            <span className={`rounded-full border px-2.5 py-0.5 text-xs ${badge}`}>{statusLabel(requirement.status)}</span>
            <span className="rounded-full border border-[var(--border)] px-2 py-0.5 text-xs text-[var(--text-muted)]">{requirement.isRequired ? "Required" : "Optional"}</span>
            {requirement.categoryId && !requirement.categoryIsActive && <span className="text-xs text-amber-300">Inactive category</span>}
          </div>
          {requirement.description && <p className="mt-2 text-sm text-[var(--text-secondary)]">{requirement.description}</p>}
          <div className="mt-3 flex flex-wrap gap-x-5 gap-y-1 text-xs text-[var(--text-muted)]">
            <span>Types: {requirement.allowedFileTypes.join(", ").toUpperCase()}</span>
            <span>Max: {formatBytes(requirement.maxFileSizeBytes)}</span>
            {requirement.dueDate && <span>Due: {formatDate(requirement.dueDate)}</span>}
            {requirement.postponedUntil && <span className={postponedDue ? "font-medium text-rose-300" : undefined}>
              {postponedDue ? "Collection date reached" : "Collect by"}: {formatDate(requirement.postponedUntil)}
            </span>}
            {requirement.lastActionByName && <span>Last action: {requirement.lastActionByName}</span>}
          </div>
        </div>
        <div className="flex max-w-xl flex-wrap gap-2">
          {requirement.latestVersion && <><Button size="sm" variant="outline" onClick={() => open(false)}>View</Button><Button size="sm" variant="ghost" onClick={() => open(true)}>Download</Button></>}
          {uploadAllowed(requirement.status) && <Button size="sm" onClick={() => onAction("upload")}>{requirement.versions.length ? "Upload replacement" : "Upload"}</Button>}
          {requirement.status === "UnderReview" && <><Button size="sm" onClick={() => onAction("approve")}>Approve</Button><Button size="sm" variant="outline" onClick={() => onAction("reject")}>Reject</Button><Button size="sm" variant="ghost" onClick={() => onAction("replacement")}>Request replacement</Button></>}
          {requirement.status === "Missing" && <Button size="sm" variant="ghost" onClick={() => onAction("requested")}>Mark requested</Button>}
          {["Missing", "Requested", "Rejected", "ReplacementRequired", "Expired"].includes(requirement.status) && <Button size="sm" variant="ghost" onClick={() => onAction("postpone")}>Postpone</Button>}
          {canOverride(requirement.status) && <><Button size="sm" variant="ghost" onClick={() => onAction("waive")}>Waive</Button><Button size="sm" variant="ghost" onClick={() => onAction("notApplicable")}>Not applicable</Button></>}
          {requirement.status === "Approved" && <Button size="sm" variant="ghost" onClick={() => onAction("expired")}>Mark expired</Button>}
          <Button size="sm" variant="ghost" onClick={() => onAction("due")}>Due date</Button>
        </div>
      </div>
      {versions.length > 0 && <details className="mt-4 border-t border-[var(--border)] pt-3">
        <summary className="cursor-pointer text-sm font-medium text-[var(--text-secondary)]">Version history ({versions.length}{hasMoreVersions ? "+" : ""})</summary>
        <div className="mt-3 space-y-2">{versions.map((version) => <div key={version.id} className="flex flex-col gap-2 rounded-xl bg-[var(--surface-glass-hover)] p-3 text-xs sm:flex-row sm:items-center sm:justify-between">
          <div><p className="font-medium text-[var(--text-primary)]">Version {version.versionNumber} · {version.originalFileName} {version.isCurrent && "· Current"}</p><p className="mt-1 text-[var(--text-muted)]">Uploaded {new Date(version.uploadedAt).toLocaleString()} by {version.uploadedByName ?? "Admin"} · {statusLabel(version.reviewStatus)}</p>{version.reviewReason && <p className="mt-1 text-rose-300">{version.reviewReason}</p>}</div>
          <div className="flex gap-2"><button type="button" className="text-indigo-300 hover:underline" onClick={() => open(false, version)}>View</button><button type="button" className="text-indigo-300 hover:underline" onClick={() => open(true, version)}>Download</button></div>
        </div>)}</div>
        {hasMoreVersions && <div className="mt-3 text-center"><Button type="button" size="sm" variant="ghost" disabled={versionsBusy} onClick={() => void loadOlderVersions()}>{versionsBusy ? "Loading…" : "Load older versions"}</Button></div>}
      </details>}
    </article>
  );
}

function ActionModal({ customerId, action, onClose, onSaved }: { customerId: number; action: ActionState; onClose: () => void; onSaved: (message: string) => Promise<void> }) {
  const [reason, setReason] = useState(""); const [date, setDate] = useState(""); const [file, setFile] = useState<File | null>(null);
  const [saving, setSaving] = useState(false); const [error, setError] = useState<string | null>(null);
  useEffect(() => { setReason(""); setDate(""); setFile(null); setError(null); }, [action]);
  if (!action) return null;
  const { requirement, kind } = action;
  const title = kind === "upload" ? (requirement.versions.length ? "Upload replacement" : "Upload document") : actionLabel(kind);
  const submit = async (event: FormEvent) => {
    event.preventDefault(); setSaving(true); setError(null);
    try {
      const base = `/api/customer-documents/customers/${customerId}/requirements/${requirement.id}`;
      if (kind === "upload") {
        if (!file) throw new Error("Choose a file to upload.");
        const form = new FormData(); form.set("file", file); form.set("concurrencyToken", requirement.concurrencyToken);
        await documentJson(`${base}/upload`, { method: "POST", body: form });
      } else if (kind === "due") {
        await documentJson(`${base}/due-date`, jsonBody("PUT", { dueDate: date || null, reason: reason || null, concurrencyToken: requirement.concurrencyToken }));
      } else {
        const status: Record<Exclude<ActionKind, "upload" | "due">, DocumentStatus> = {
          approve: "Approved", reject: "Rejected", replacement: "ReplacementRequired", postpone: "Postponed",
          waive: "Waived", notApplicable: "NotApplicable", requested: "Requested", expired: "Expired",
        };
        await documentJson(`${base}/status`, jsonBody("POST", { status: status[kind], reason: reason || null, postponedUntil: kind === "postpone" && date ? date : null, concurrencyToken: requirement.concurrencyToken }));
      }
      await onSaved(kind === "upload" ? "Document uploaded for review." : `${actionLabel(kind)} saved.`);
    } catch (caught) { setError(caught instanceof Error ? caught.message : "The document action could not be saved."); }
    finally { setSaving(false); }
  };
  const needsReason = ["reject", "replacement", "postpone", "waive", "notApplicable", "expired"].includes(kind);
  return <Modal open onClose={() => !saving && onClose()} align="top"><form onSubmit={(e) => void submit(e)} role="dialog" aria-modal="true" aria-labelledby="document-action-title" className="relative my-8 w-full max-w-lg rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-2xl">
    <h2 id="document-action-title" className="text-lg font-semibold text-[var(--text-heading)]">{title}</h2><p className="mt-1 text-sm text-[var(--text-muted)]">{requirement.name} · {statusLabel(requirement.status)}</p>
    {error && <p className="mt-4 rounded-xl bg-rose-500/10 p-3 text-sm text-rose-300" role="alert">{error}</p>}
    <div className="mt-5 space-y-4">{kind === "upload" && <label className="block text-sm text-[var(--text-secondary)]">File <span aria-hidden>*</span><input required type="file" accept={requirement.allowedFileTypes.join(",")} onChange={(e) => setFile(e.target.files?.[0] ?? null)} className="mt-2 block w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-sm" /><span className="mt-1 block text-xs text-[var(--text-muted)]">{requirement.allowedFileTypes.join(", ").toUpperCase()} · up to {formatBytes(requirement.maxFileSizeBytes)}</span></label>}
      {(kind === "postpone" || kind === "due") && <label className="block text-sm text-[var(--text-secondary)]">{kind === "postpone" ? "Collect by (optional)" : "Due date"}<input type="date" min={kind === "postpone" ? new Date(Date.now() + 86400000).toISOString().slice(0, 10) : undefined} value={date} onChange={(e) => setDate(e.target.value)} className="mt-2 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3" /></label>}
      {kind !== "upload" && kind !== "requested" && <label className="block text-sm text-[var(--text-secondary)]">{needsReason ? "Reason" : "Review note (optional)"}<textarea required={needsReason} rows={4} value={reason} onChange={(e) => setReason(e.target.value)} className="mt-2 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3" /></label>}
    </div><div className="mt-6 flex justify-end gap-2"><Button type="button" variant="ghost" disabled={saving} onClick={onClose}>Cancel</Button><Button type="submit" disabled={saving}>{saving ? "Saving…" : title}</Button></div>
  </form></Modal>;
}

function AddRequirementModal({ customerId, open, categories, existing, onClose, onSaved }: { customerId: number; open: boolean; categories: DocumentCategory[]; existing: DocumentRequirement[]; onClose: () => void; onSaved: () => Promise<void> }) {
  const [form, setForm] = useState({ categoryId: "", name: "", description: "", required: true, dueDate: "", saveGlobal: false, code: "", assignment: "None" as AssignmentMode });
  const [saving, setSaving] = useState(false); const [error, setError] = useState<string | null>(null);
  useEffect(() => { if (open) { setForm({ categoryId: "", name: "", description: "", required: true, dueDate: "", saveGlobal: false, code: "", assignment: "None" }); setError(null); } }, [open]);
  const available = categories.filter((c) => c.isActive && !existing.some((r) => r.categoryId === c.id));
  const submit = async (event: FormEvent) => {
    event.preventDefault(); setSaving(true); setError(null);
    try {
      await documentJson(`/api/customer-documents/customers/${customerId}/requirements`, jsonBody("POST", {
        categoryId: form.categoryId ? Number(form.categoryId) : null, name: form.categoryId ? null : form.name,
        description: form.description || null, isRequired: form.required, dueDate: form.dueDate || null,
        saveAsGlobalCategory: !form.categoryId && form.saveGlobal, globalCategoryCode: form.code || null,
        allowedFileTypes: [".pdf", ".jpg", ".jpeg", ".png"], maxFileSizeBytes: 10 * 1024 * 1024,
        globalAssignmentMode: form.assignment, selectedCustomerIds: form.assignment === "SelectedCustomers" ? [customerId] : [],
      })); await onSaved();
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Requirement could not be added."); }
    finally { setSaving(false); }
  };
  return <Modal open={open} onClose={() => !saving && onClose()} align="top"><form onSubmit={(e) => void submit(e)} role="dialog" aria-modal="true" aria-labelledby="add-requirement-title" className="relative my-8 w-full max-w-xl rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-2xl">
    <h2 id="add-requirement-title" className="text-lg font-semibold text-[var(--text-heading)]">Add document requirement</h2><p className="mt-1 text-sm text-[var(--text-muted)]">Choose a reusable category or create a requirement for this customer.</p>
    {error && <p role="alert" className="mt-4 rounded-xl bg-rose-500/10 p-3 text-sm text-rose-300">{error}</p>}
    <div className="mt-5 space-y-4"><FieldLabel label="Existing category"><select value={form.categoryId} onChange={(e) => setForm({ ...form, categoryId: e.target.value })} className={inputClass}><option value="">Custom requirement</option>{available.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select></FieldLabel>
      {!form.categoryId && <><FieldLabel label="Requirement name" required><input required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} className={inputClass} /></FieldLabel><FieldLabel label="Description"><textarea value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} className={inputClass} rows={3} /></FieldLabel>
        <label className="flex items-center gap-3 text-sm text-[var(--text-secondary)]"><input type="checkbox" checked={form.saveGlobal} onChange={(e) => setForm({ ...form, saveGlobal: e.target.checked })} />Save as a reusable global category</label>
        {form.saveGlobal && <><FieldLabel label="Stable category code" required><input required value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value.toLowerCase().replace(/[^a-z0-9_-]/g, "_") })} className={inputClass} /></FieldLabel><FieldLabel label="Apply global category"><select value={form.assignment} onChange={(e) => setForm({ ...form, assignment: e.target.value as AssignmentMode })} className={inputClass}><option value="None">Do not assign automatically</option><option value="NewCustomersOnly">New customers only</option><option value="AllActiveCustomers">All existing active customers</option><option value="SelectedCustomers">This customer only</option></select></FieldLabel></>}
      </>}
      <div className="grid gap-4 sm:grid-cols-2"><label className="flex items-center gap-3 rounded-xl border border-[var(--border)] p-3 text-sm text-[var(--text-secondary)]"><input type="checkbox" checked={form.required} onChange={(e) => setForm({ ...form, required: e.target.checked })} />Required for this customer</label><FieldLabel label="Due date"><input type="date" value={form.dueDate} onChange={(e) => setForm({ ...form, dueDate: e.target.value })} className={inputClass} /></FieldLabel></div>
    </div><div className="mt-6 flex justify-end gap-2"><Button type="button" variant="ghost" disabled={saving} onClick={onClose}>Cancel</Button><Button type="submit" disabled={saving}>{saving ? "Adding…" : "Add requirement"}</Button></div>
  </form></Modal>;
}

function State({ text, error = false, onRetry }: { text: string; error?: boolean; onRetry?: () => void }) { return <div className={`rounded-2xl border border-dashed border-[var(--border)] p-12 text-center text-sm ${error ? "text-rose-300" : "text-[var(--text-muted)]"}`}><p>{text}</p>{onRetry && <Button className="mt-4" size="sm" variant="outline" onClick={onRetry}>Retry</Button>}</div>; }
function FieldLabel({ label, required, children }: { label: string; required?: boolean; children: React.ReactNode }) { return <label className="block text-sm text-[var(--text-secondary)]"><span>{label}{required && <span aria-hidden> *</span>}</span><span className="mt-2 block">{children}</span></label>; }
function actionLabel(kind: ActionKind) { return ({ upload: "Upload", approve: "Approve document", reject: "Reject document", replacement: "Request replacement", postpone: "Postpone requirement", waive: "Waive requirement", notApplicable: "Mark not applicable", requested: "Mark requested", expired: "Mark expired", due: "Change due date" } as const)[kind]; }
function formatBytes(bytes: number) { return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(bytes % (1024 * 1024) === 0 ? 0 : 1)} MB` : `${Math.ceil(bytes / 1024)} KB`; }
function formatDate(value: string) { return new Date(value).toLocaleDateString(); }
const inputClass = "w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2.5 text-[var(--text-primary)] outline-none focus:border-indigo-500/60";
