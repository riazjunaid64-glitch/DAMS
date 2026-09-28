import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "../../api/api.ts";
import { readError } from "../../api/readError.ts";
import {
  Button,
  ConfirmDialog,
  EmptyState,
  IconLayers,
  IconPlus,
  IconTrash,
  Modal,
  NumberField,
  cx,
  useIsPhone,
  useToast,
} from "../ui";
import { bareInputClass, controlBoxClass } from "../ui/styles.ts";
import {
  MAX_BASEMENTS,
  addFloorRow,
  createFloors,
  floorListError,
  floorRowErrors,
  floorsPayload,
  maxFloorsAboveGround,
  nextFreeFloorNumber,
  quickSetupFrom,
  rowsFromFloors,
  sortRows,
  unitCountLabel,
  type FloorRow,
  type FloorRowError,
  type ProjectFloor,
} from "./floorRules.ts";

function parseFloors(raw: unknown): ProjectFloor[] {
  if (!Array.isArray(raw)) return [];
  return raw.flatMap((row) => {
    if (!row || typeof row !== "object") return [];
    const o = row as Record<string, unknown>;
    const number = Number(o.number);
    return Number.isInteger(number)
      ? [{ number, name: String(o.name ?? ""), unitCount: Number(o.unitCount) || 0 }]
      : [];
  });
}

function wholeInRange(value: string, min: number, max: number): number | null {
  if (!/^\d+$/.test(value)) return null;
  const number = Number(value);
  return number >= min && number <= max ? number : null;
}

/** Floors popup (KAN-57): quick setup, then add / rename / renumber / remove floors, saved in one PUT. */
export function ManageFloorsDialog({ projectId, projectName, onClose, onSaved }: {
  projectId: number;
  projectName: string;
  onClose: () => void;
  onSaved: () => void | Promise<void>;
}) {
  const toast = useToast();
  const isPhone = useIsPhone();
  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<FloorRow[]>([]);
  const [basements, setBasements] = useState("");
  const [aboveGround, setAboveGround] = useState("");
  const [confirmReplace, setConfirmReplace] = useState(false);
  const [saving, setSaving] = useState(false);
  // The parent passes a new onClose on every render; the load must run once per project, not per render.
  const close = useRef(onClose);
  useEffect(() => { close.current = onClose; });

  useEffect(() => {
    let current = true;
    void api(`/api/Project/${projectId}/floors`, undefined, false)
      .then(async (response) => {
        if (!current) return;
        if (!response.ok) throw new Error(await readError(response, "The floor list could not be loaded."));
        const floors = parseFloors(await response.json());
        if (!current) return;
        const setup = quickSetupFrom(floors);
        setRows(rowsFromFloors(floors));
        setBasements(setup.basements);
        setAboveGround(setup.aboveGround);
        setLoading(false);
      })
      .catch((caught: unknown) => {
        if (!current) return;
        toast.error(caught instanceof Error ? caught.message : "The floor list could not be loaded.");
        close.current();
      });
    return () => { current = false; };
  }, [projectId, toast]);

  const errors = useMemo(() => floorRowErrors(rows), [rows]);
  const basementCount = basements === "" ? 0 : wholeInRange(basements, 0, MAX_BASEMENTS);
  const aboveGroundLimit = maxFloorsAboveGround(basementCount ?? 0);
  const floorCount = wholeInRange(aboveGround, 1, aboveGroundLimit);
  const listError = floorListError(rows);
  const canCreate = basementCount !== null && floorCount !== null && !saving;
  const canAdd = nextFreeFloorNumber(rows) !== null;
  const canSave = !loading && !saving && rows.length > 0 && !listError && Object.keys(errors).length === 0;

  const create = () => {
    if (basementCount === null || floorCount === null) return;
    setRows((current) => createFloors(current, basementCount, floorCount));
    setConfirmReplace(false);
  };

  const update = (key: string, changes: Partial<Pick<FloorRow, "number" | "name">>) =>
    setRows((current) => current.map((row) => (row.key === key ? { ...row, ...changes } : row)));

  const save = async () => {
    setSaving(true);
    try {
      const response = await api(`/api/Project/${projectId}/floors`, {
        method: "PUT",
        body: JSON.stringify(floorsPayload(rows)),
      });
      if (!response.ok) {
        toast.error(await readError(response, "The floors could not be saved."));
        setSaving(false);
        return;
      }
      toast.success("Floors saved");
      await onSaved();
    } catch {
      toast.error("The floors could not be saved.");
      setSaving(false);
    }
  };

  const subtitle = loading
    ? projectName
    : `${projectName} · ${rows.length === 0 ? "no floors yet" : rows.length === 1 ? "1 floor" : `${rows.length} floors`}`;

  return (
    <>
      <Modal
        open
        onClose={onClose}
        title={<>Floors<span className="block text-small font-normal text-ink-muted">{subtitle}</span></>}
        size="lg"
        phoneLayout="fullscreen"
        busy={saving}
        primaryAction={{ label: "Save floors", onClick: () => void save(), disabled: !canSave, loading: saving }}
      >
        {loading ? (
          <div className="flex flex-col gap-3">
            <div className="h-12 animate-pulse rounded-field bg-track" />
            <div className="h-40 animate-pulse rounded-field bg-track" />
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            <div className="grid grid-cols-2 items-end gap-3 md:grid-cols-[1fr_1fr_auto]">
              <NumberField
                label="Basements"
                decimals={0}
                value={basements}
                onChange={setBasements}
                error={basementCount === null ? `0 to ${MAX_BASEMENTS}` : undefined}
              />
              <NumberField
                label="Floors above ground"
                decimals={0}
                value={aboveGround}
                onChange={setAboveGround}
                error={aboveGround !== "" && floorCount === null ? `1 to ${aboveGroundLimit}` : undefined}
              />
              <Button
                variant="outline"
                size={isPhone ? "lg" : "md"}
                className="col-span-2 md:col-span-1"
                fullWidth={isPhone}
                disabled={!canCreate}
                onClick={() => (rows.length > 0 ? setConfirmReplace(true) : create())}
              >
                Create floors
              </Button>
            </div>

            <div className="border-t border-line-soft" />

            {rows.length === 0 ? (
              <EmptyState
                icon={<IconLayers size={26} />}
                title="No floors yet"
                message="Enter basements and floors above ground, then Create floors."
              />
            ) : (
              <div className="flex flex-col gap-2">
                <div className="flex gap-2 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">
                  <span className="w-16 shrink-0 md:w-[76px]">No.</span>
                  <span className="min-w-0 flex-1">Name</span>
                  <span className="hidden w-20 shrink-0 md:block">Units</span>
                  <span className="w-11 shrink-0" aria-hidden="true" />
                </div>
                {rows.map((row) => (
                  <FloorRowEditor
                    key={row.key}
                    row={row}
                    error={errors[row.key]}
                    onNumber={(number) => update(row.key, { number })}
                    onName={(name) => update(row.key, { name })}
                    onSettle={() => setRows(sortRows)}
                    onRemove={() => setRows((current) => current.filter((item) => item.key !== row.key))}
                  />
                ))}
              </div>
            )}

            {listError && <p role="alert" className="text-small font-bold text-danger">{listError}</p>}

            <Button variant="link" className="self-start" icon={<IconPlus size={16} />} disabled={!canAdd} onClick={() => setRows(addFloorRow)}>
              Add floor
            </Button>
          </div>
        )}
      </Modal>
      <ConfirmDialog
        open={confirmReplace}
        onClose={() => setConfirmReplace(false)}
        onConfirm={create}
        title="Replace the floor list?"
        message="Floors with units are kept."
        confirmLabel="Create floors"
      />
    </>
  );
}

function FloorRowEditor({ row, error, onNumber, onName, onSettle, onRemove }: {
  row: FloorRow;
  error?: FloorRowError;
  onNumber: (value: string) => void;
  onName: (value: string) => void;
  onSettle: () => void;
  onRemove: () => void;
}) {
  const locked = row.unitCount > 0;
  const units = unitCountLabel(row.unitCount);
  const messageId = `${row.key}-msg`;
  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-start gap-2">
        <div className="w-16 shrink-0 md:w-[76px]">
          <div className={controlBoxClass({ error: error?.field === "number", disabled: locked })}>
            <input
              aria-label="Floor number"
              inputMode="numeric"
              value={row.number}
              disabled={locked}
              aria-invalid={error?.field === "number" ? true : undefined}
              aria-describedby={error ? messageId : undefined}
              onChange={(event) => onNumber(event.target.value.replace(/[^\d-]/g, "").replace(/(?!^)-/g, ""))}
              onBlur={onSettle}
              className={cx(bareInputClass, "text-center")}
            />
          </div>
        </div>
        <div className="min-w-0 flex-1">
          <div className={controlBoxClass({ error: error?.field === "name" })}>
            <input
              aria-label="Floor name"
              value={row.name}
              aria-invalid={error?.field === "name" ? true : undefined}
              aria-describedby={error ? messageId : undefined}
              maxLength={40}
              onChange={(event) => onName(event.target.value)}
              className={bareInputClass}
            />
          </div>
          <p className={cx("mt-1 text-small md:hidden", locked ? "text-ink-2" : "text-ink-muted")}>{units}</p>
        </div>
        <span className={cx("hidden h-12 w-20 shrink-0 items-center text-small md:flex", locked ? "text-ink-2" : "text-ink-muted")}>{units}</span>
        <Button
          variant="ghost"
          iconOnly
          aria-label="Remove floor"
          disabled={locked}
          icon={<IconTrash size={16} className={locked ? undefined : "text-danger"} />}
          onClick={onRemove}
        />
      </div>
      {error && <p id={messageId} role="alert" className="text-small font-bold text-danger">{error.message}</p>}
    </div>
  );
}
