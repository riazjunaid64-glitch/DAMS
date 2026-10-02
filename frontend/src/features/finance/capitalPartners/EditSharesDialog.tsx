import { useId, useState, type FormEvent } from "react";
import { Modal, Notice, NumberField, Toggle, useToast } from "../../../components/ui";
import { DialogTitle } from "../../../components/ui/DialogTitle.tsx";
import { capitalApi } from "./api.ts";
import { activeShareTotal, formatShare, shareInRange, sharesAddUp, toNumber } from "./rules.ts";
import type { Partner } from "./types.ts";

type Row = { active: boolean; share: string };

type Props = {
  partners: readonly Partner[];
  onClose: () => void;
  /** Called once the shares are saved, so the list can reload. */
  onSaved: () => void;
};

/**
 * Edit shares: every partner's Active switch and share in one popup. A switched-off partner keeps
 * its saved share (greyed, not editable). Save is off until the active shares add up to 100%, and
 * sends every partner, as the server expects.
 */
export function EditSharesDialog({ partners, onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [rows, setRows] = useState<Record<number, Row>>(() =>
    Object.fromEntries(partners.map((partner) => [partner.id, { active: partner.isActive, share: String(partner.profitSharePercent) }])));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const total = activeShareTotal(partners.map((partner) => ({ isActive: rows[partner.id]!.active, share: toNumber(rows[partner.id]!.share) })));
  // Every share counts, on or off: an out-of-range figure is refused by the server whatever the total.
  const outOfRange = (id: number) => !shareInRange(toNumber(rows[id]!.share));
  const valid = sharesAddUp(total) && !partners.some((partner) => outOfRange(partner.id));
  const change = (id: number, changes: Partial<Row>) => setRows((current) => ({ ...current, [id]: { ...current[id]!, ...changes } }));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !valid) return;
    setSaving(true);
    setError(null);
    try {
      await capitalApi.saveShares(partners.map((partner) => ({
        id: partner.id,
        profitSharePercent: toNumber(rows[partner.id]!.share),
        isActive: rows[partner.id]!.active,
        concurrencyToken: partner.concurrencyToken,
      })));
      toast.success("Shares saved.");
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Shares could not be saved.");
      setSaving(false);
    }
  };

  const totalText = `${formatShare(total)} of 100%`;

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="md"
      phoneLayout="fullscreen"
      title={<DialogTitle title="Edit shares" subtitle="Active partners must total exactly 100%" />}
      primaryAction={{ label: "Save shares", form: formId, loading: saving, disabled: !valid }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        <ul className="m-0 flex list-none flex-col p-0">
          {partners.map((partner) => {
            const row = rows[partner.id]!;
            return (
              <li key={partner.id} className="flex items-center justify-between gap-3 border-t border-line-soft py-3 first:border-t-0 first:pt-0">
                <Toggle
                  checked={row.active}
                  disabled={saving}
                  onChange={(active) => change(partner.id, { active })}
                  label={<span className={row.active ? "font-bold text-ink" : "font-bold text-ink-faint"}>{partner.name}</span>}
                  className="min-w-0 flex-1"
                />
                <NumberField
                  aria-label={`${partner.name} share`}
                  decimals={4}
                  suffix="%"
                  disabled={saving || !row.active}
                  error={outOfRange(partner.id) ? "0 to 100" : undefined}
                  value={row.share}
                  onChange={(share) => change(partner.id, { share })}
                  className="w-32 shrink-0 [&_input]:text-right"
                />
              </li>
            );
          })}
        </ul>
        {valid ? (
          <p className="m-0 flex items-center justify-between gap-3 rounded-field bg-page px-4 py-3 text-sm font-extrabold text-ink">
            <span>Total of active partners</span>
            <span>{totalText}</span>
          </p>
        ) : (
          <Notice
            tone="red"
            role="status"
            title={<span className="flex items-center justify-between gap-3"><span>Total of active partners</span><span>{totalText}</span></span>}
          />
        )}
      </form>
    </Modal>
  );
}
