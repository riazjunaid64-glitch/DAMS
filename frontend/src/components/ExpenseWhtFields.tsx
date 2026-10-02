import { useEffect, useRef, useState } from "react";
import { NumberField, TextArea } from "./ui";
import { calculateWht } from "../features/finance/whtApi.ts";
import {
  calculationKey,
  fromAmount,
  fromPreview,
  fromRate,
  isOverridden,
  netPaid as deriveNetPaid,
  seededKey,
  showsFiledFigures,
} from "../features/finance/whtFormState.ts";
import { formatRs, type WhtCalculation, type WhtFormValue } from "../features/finance/whtTypes.ts";

/**
 * The withholding block, shared by the expense form, the fixed-asset purchase form and Cash with staff.
 *
 * Rate and tax are both editable and kept consistent with each other: editing the rate recomputes
 * the tax, editing the tax back-computes the rate. Net paid is always derived and never editable,
 * because gross must equal tax plus net or the account balance stops reconciling.
 *
 * Both forms use it unchanged because the tax is genuinely the same calculation — the same rate
 * table, the same filer status, and the same annual allowance shared across both. Only the wording
 * differs, since an asset is capitalised at the gross figure rather than expensed at it.
 */
export default function ExpenseWhtFields({
  categoryId,
  vendorId,
  grossAmount,
  date,
  excludeExpenseId,
  excludeAssetPurchaseId = null,
  capitalised = false,
  value,
  onChange,
  disabled,
  onReasonRequired,
}: {
  categoryId: string;
  vendorId: string;
  grossAmount: string;
  date: string;
  excludeExpenseId: number | null;
  excludeAssetPurchaseId?: number | null;
  /** True on the asset-purchase form: the gross is what the asset is carried at, not a cost. */
  capitalised?: boolean;
  value: WhtFormValue;
  onChange: (next: WhtFormValue) => void;
  disabled?: boolean;
  /** True while a changed tax has no reason, so the parent can keep Save off. */
  onReasonRequired?: (required: boolean) => void;
}) {
  const [preview, setPreview] = useState<WhtCalculation | null>(null);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);

  const gross = Number(grossAmount);
  const key = calculationKey({ categoryId, vendorId, grossAmount, date });

  // Which inputs the figures on screen already belong to. Seeded on mount from the values the
  // form opened with, so editing an existing expense does not have its recorded tax — possibly a
  // deliberate override — replaced the moment the first preview lands. Prefilling then only
  // happens when the calculation basis actually moves.
  const appliedKey = useRef<string | null>(seededKey(value, key));

  // The key the form opened on, kept fixed. While the basis has not moved off it, what is on
  // screen is the filed snapshot — the server keeps it as-is and asks for no reason, so the form
  // must not demand one either.
  const openedKey = useRef<string | null>(seededKey(value, key));
  const asFiled = showsFiledFigures(openedKey.current, key);
  const overridden = Boolean(categoryId) && !asFiled && isOverridden(value, preview);
  const reasonRequired = overridden && value.overrideReason.trim() === "";

  useEffect(() => {
    onReasonRequired?.(reasonRequired);
  }, [reasonRequired, onReasonRequired]);

  useEffect(() => {
    if (!categoryId || !Number.isFinite(gross) || gross <= 0) {
      setPreview(null);
      setFailed(false);
      return;
    }
    const controller = new AbortController();
    setLoading(true);
    const timer = window.setTimeout(() => {
      calculateWht({
        categoryId: Number(categoryId),
        vendorId: vendorId ? Number(vendorId) : null,
        grossAmount: gross,
        date: date || undefined,
        excludeExpenseId,
        excludeAssetPurchaseId,
      }, controller.signal)
        .then((result) => {
          if (controller.signal.aborted) return;
          setPreview(result);
          setFailed(false);
          // Prefill only when the underlying calculation moved. A deliberate override survives
          // unrelated edits (a typo in the description, say) instead of being silently reset.
          if (appliedKey.current !== key) {
            appliedKey.current = key;
            onChange(fromPreview(result));
          }
        })
        .catch(() => { if (!controller.signal.aborted) { setPreview(null); setFailed(true); } })
        .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    }, 300);
    return () => { controller.abort(); window.clearTimeout(timer); };
    // onChange is intentionally excluded: it is recreated every render by the parent, and
    // including it would refire the preview on every keystroke anywhere in the form.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [categoryId, vendorId, gross, date, excludeExpenseId, excludeAssetPurchaseId, key]);

  if (!categoryId) return null;

  if (preview && !preview.isWhtApplicable) {
    return (
      <p className="m-0 text-small text-ink-muted">
        {preview.notice ?? "No withholding tax applies to this category."}
      </p>
    );
  }

  const netPaid = deriveNetPaid(grossAmount, value, preview);
  const differsFromToday = asFiled && isOverridden(value, preview);
  const section = preview?.taxSection ? `s.${preview.taxSection}` : null;

  return (
    <div className="space-y-3 rounded-card border border-line bg-page p-4">
      <div className="flex items-center justify-between gap-3">
        <p className="m-0 text-sm font-extrabold text-ink">
          Withholding tax
          {section && <span className="ml-2 font-bold text-ink-muted">{section}</span>}
        </p>
        {loading && <span className="text-small text-ink-muted">Calculating…</span>}
      </div>

      {failed && (
        <p className="m-0 text-small text-warning">
          The tax could not be calculated. Enter the rate and amount by hand, or reopen the form to
          retry — the server recalculates and validates on save either way.
        </p>
      )}

      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <NumberField
          label="Rate (%)"
          decimals={4}
          value={value.rate}
          disabled={disabled}
          onChange={(rate) => {
            if (rate !== "" && Number(rate) > 100) return;
            onChange(fromRate(value, rate, grossAmount));
          }}
        />
        <NumberField
          label="Tax withheld"
          prefix="Rs"
          decimals={2}
          value={value.amount}
          disabled={disabled}
          onChange={(amount) => onChange(fromAmount(value, amount, grossAmount))}
        />
      </div>

      <div className="flex items-baseline justify-between gap-3">
        <span className="text-small font-bold text-ink-2">
          Net paid to {capitalised ? "supplier" : "vendor"}
        </span>
        <span className="text-sm font-extrabold text-ink">{formatRs(netPaid)}</span>
      </div>

      {overridden && preview && (
        <div className="space-y-2 rounded-card border border-gold-line bg-gold-soft p-3">
          <p className="m-0 text-small font-bold text-ink">Worked-out tax was {formatRs(preview.whtAmount)}</p>
          <TextArea
            label="Reason for changing the tax"
            required
            rows={2}
            value={value.overrideReason}
            disabled={disabled}
            onChange={(event) => onChange({ ...value, overrideReason: event.target.value })}
          />
        </div>
      )}

      {differsFromToday && preview && (
        <p className="m-0 text-small text-ink-muted">
          This is the tax as originally withheld, and it is kept on save. Today&apos;s rules would give{" "}
          {formatRs(preview.whtAmount)} — filed periods are not restated. Change the amount,
          category, vendor or date to recalculate.
        </p>
      )}

      {preview?.notice && (
        <p className={`m-0 text-small ${preview.belowThreshold ? "text-info" : "text-ink-muted"}`}>
          {preview.notice}
        </p>
      )}
    </div>
  );
}
