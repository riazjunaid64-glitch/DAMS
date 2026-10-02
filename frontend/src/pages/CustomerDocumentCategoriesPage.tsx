import { useCallback, useEffect, useState, type FormEvent } from "react";
import type { User } from "../App.tsx";
import {
  Button,
  ConfirmDialog,
  DataTable,
  EmptyState,
  IconFile,
  IconPlus,
  IconPencil,
  Modal,
  Notice,
  PageHeader,
  Spinner,
  TextField,
  Toggle,
  useIsPhone,
  useToast,
  type DataTableColumn,
} from "../components/ui";
import { can } from "../features/access/permissions.ts";
import { documentJson, jsonBody } from "../features/customerDocuments/documentApi.ts";
import type { DocumentSetupItem, DocumentSetupList } from "../features/customerDocuments/types.ts";
import { usePageTrail } from "../layouts/trail.ts";

type Props = { user: User | null };

const FORM_ID = "document-setup-name";

export default function CustomerDocumentCategoriesPage({ user }: Props) {
  usePageTrail([{ label: "Document setup" }]);
  if (!user || !can(user.role, "customers")) {
    return (
      <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:px-8 md:py-7">
        <EmptyState
          icon={<IconFile size={26} />}
          title={user ? "You don't have access to Document setup." : "Sign in required"}
          message={user ? undefined : "Sign in with a staff account to open Document setup."}
        />
      </div>
    );
  }
  return <DocumentSetupWorkspace />;
}

function DocumentSetupWorkspace() {
  const toast = useToast();
  const isPhone = useIsPhone();
  const [list, setList] = useState<DocumentSetupList | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<DocumentSetupItem | "new" | null>(null);
  const [pending, setPending] = useState<{ document: DocumentSetupItem; next: boolean } | null>(null);
  const [savingId, setSavingId] = useState<number | null>(null);
  const [draft, setDraft] = useState<Record<number, boolean>>({});

  const load = useCallback(async () => {
    setError(null);
    try {
      setList(await documentJson<DocumentSetupList>("/api/customer-documents/categories"));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Document setup could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const checked = (document: DocumentSetupItem) =>
    document.id in draft ? draft[document.id]! : document.asksEveryCustomer;

  const confirmSwitch = async () => {
    if (!pending || savingId != null) return;
    const { document, next } = pending;
    setPending(null);
    setSavingId(document.id);
    setDraft((current) => ({ ...current, [document.id]: next }));
    let saved = false;
    try {
      await documentJson(
        `/api/customer-documents/categories/${document.id}/ask-every-customer`,
        jsonBody("PUT", { asksEveryCustomer: next }),
      );
      saved = true;
      toast.success(next ? `Asking every customer for ${document.name}.` : `Stopped asking for ${document.name}.`);
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "The switch could not be saved.");
    }
    if (saved) await load();
    setSavingId(null);
    setDraft((current) => {
      const nextDraft = { ...current };
      delete nextDraft[document.id];
      return nextDraft;
    });
  };

  const columns: DataTableColumn<DocumentSetupItem>[] = [
    {
      key: "name",
      header: "Document",
      render: (document) => <span className="font-extrabold">{document.name}</span>,
    },
    {
      key: "ask",
      header: "Ask every customer",
      className: "w-[1%] whitespace-nowrap",
      render: (document) => (
        <Toggle
          hideLabel
          label={`Ask every customer for ${document.name}`}
          checked={checked(document)}
          loading={savingId === document.id}
          disabled={savingId != null && savingId !== document.id}
          onChange={(next) => {
            if (savingId != null) return;
            setPending({ document, next });
          }}
        />
      ),
    },
    {
      key: "edit",
      header: <span className="sr-only">Edit</span>,
      align: "right",
      className: "w-[1%]",
      render: (document) => (
        <Button
          variant="outline"
          iconOnly
          icon={<IconPencil size={18} />}
          aria-label={`Edit ${document.name}`}
          onClick={() => setEditing(document)}
        />
      ),
    },
  ];

  const documents = list?.documents ?? [];
  const count = list?.nonBlockedCustomerCount ?? 0;

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      <PageHeader
        title="Document setup"
        actions={
          isPhone ? (
            <Button iconOnly icon={<IconPlus size={18} />} aria-label="New document" onClick={() => setEditing("new")} />
          ) : (
            <Button icon={<IconPlus size={16} />} onClick={() => setEditing("new")}>New document</Button>
          )
        }
        className="max-md:flex-row max-md:items-center max-md:justify-between"
      />
      {error && <Notice tone="red" role="alert" title={error} />}
      {loading && !list ? (
        <p className="flex items-center justify-center gap-2 py-16 text-sm text-ink-muted"><Spinner /> Loading documents…</p>
      ) : (
        <DataTable
          columns={columns}
          rows={documents}
          rowKey={(document) => document.id}
          minWidth={320}
          caption="Document setup"
          empty={<EmptyState icon={<IconFile size={26} />} title="No documents yet" message="Use New document to add one." />}
        />
      )}
      <NameDialog
        item={editing}
        onClose={() => setEditing(null)}
        onSaved={async () => { setEditing(null); await load(); }}
      />
      <ConfirmDialog
        open={pending != null}
        onClose={() => { if (savingId == null) setPending(null); }}
        onConfirm={() => void confirmSwitch()}
        title={pending ? (pending.next ? `Ask every customer for ${pending.document.name}?` : `Stop asking for ${pending.document.name}?`) : "Ask every customer?"}
        message={pending
          ? (pending.next
            ? `${pending.document.name} will show as Needed on all ${count} customers.`
            : "It is removed from customers who have not uploaded it.")
          : ""}
        confirmLabel={pending?.next ? "Ask everyone" : "Stop asking"}
      />
    </div>
  );
}

function NameDialog({
  item,
  onClose,
  onSaved,
}: {
  item: DocumentSetupItem | "new" | null;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const toast = useToast();
  const isPhone = useIsPhone();
  const current = item && item !== "new" ? item : null;
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [confirmRemove, setConfirmRemove] = useState(false);

  useEffect(() => {
    if (!item) return;
    setName(current?.name ?? "");
    setError(null);
    setSaving(false);
    setRemoving(false);
    setConfirmRemove(false);
  }, [item, current]);

  const save = async (event: FormEvent) => {
    event.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) {
      setError("Enter a name.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      if (current) {
        await documentJson(`/api/customer-documents/categories/${current.id}`, jsonBody("PUT", { name: trimmed }));
      } else {
        await documentJson("/api/customer-documents/categories", jsonBody("POST", { name: trimmed }));
      }
      toast.success("Document saved.");
      await onSaved();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The document could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  const remove = async () => {
    if (!current || removing) return;
    setRemoving(true);
    try {
      await documentJson(`/api/customer-documents/categories/${current.id}`, { method: "DELETE" });
      toast.success("Document removed.");
      setConfirmRemove(false);
      await onSaved();
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "The document could not be removed.");
      setRemoving(false);
    }
  };

  return (
    <>
      <Modal
        open={item != null}
        onClose={onClose}
        title={current ? "Edit document" : "New document"}
        size="sm"
        phoneLayout="popup"
        busy={saving || removing}
        primaryAction={{ label: "Save", form: FORM_ID, loading: saving, disabled: saving || removing }}
        footerLeading={current ? (
          <Button
            variant="danger"
            size={isPhone ? "lg" : "md"}
            fullWidth={isPhone}
            disabled={saving || removing}
            onClick={() => setConfirmRemove(true)}
          >
            Remove
          </Button>
        ) : undefined}
      >
        <form id={FORM_ID} onSubmit={(event) => void save(event)}>
          <TextField
            label="Name"
            required
            value={name}
            error={error ?? undefined}
            disabled={saving || removing}
            autoFocus
            onChange={(event) => setName(event.target.value)}
          />
        </form>
      </Modal>
      <ConfirmDialog
        open={confirmRemove}
        onClose={() => { if (!removing) setConfirmRemove(false); }}
        onConfirm={() => void remove()}
        title={current ? `Remove ${current.name}?` : "Remove document?"}
        message="Customers who already have a file keep it."
        confirmLabel="Remove"
        danger
        loading={removing}
      />
    </>
  );
}
