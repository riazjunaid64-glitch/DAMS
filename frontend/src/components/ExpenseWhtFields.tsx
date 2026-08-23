import { useEffect, useRef, useState } from "react";
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
 * The withholding block, shared by the expense form and the fixed-asset purchase form.
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

  if (!categoryId) {
    return (
      <p className="rounded-xl border border-dashed border-[var(--border)] px-4 py-3 text-xs text-[var(--text-muted)]">
        Choose a managed category to work out withholding tax.
      </p>
    );
  }

  if (preview && !preview.isWhtApplicable) {
    return (
      <p className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] px-4 py-3 text-xs text-[var(--text-muted)]">
        {preview.notice ?? "No withholding tax applies to this category."}
      </p>
    );
  }

  const netPaid = deriveNetPaid(grossAmount, value, preview);
  const overridden = !asFiled && isOverridden(value, preview);
  const differsFromToday = asFiled && isOverridden(value, preview);

  return (
    <div className="space-y-3 rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
      <div className="flex items-center justify-between">
        <p className="text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">
          Withholding tax
          {preview?.taxSection && <span className="ml-2 font-normal normal-case">s.{preview.taxSection}</span>}
        </p>
        {loading && <span className="text-[11px] text-[var(--text-muted)]">Calculating…</span>}
      </div>

      {failed && (
        <p className="rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs text-amber-300">
          The tax could not be calculated. Enter the rate and amount by hand, or reopen the form to
          retry — the server recalculates and validates on save either way.
        </p>
      )}

      <div className="grid grid-cols-2 gap-3">
        <label className="flex flex-col gap-1.5">
          <span className="text-xs font-medium text-[var(--text-secondary)]">Rate (%)</span>
          <input
            type="number" step="0.0001" min="0" max="100" inputMode="decimal"
            value={value.rate}
            disabled={disabled}
            onChange={(e) => onChange(fromRate(value, e.target.value, grossAmount))}
            className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2.5 text-sm text-[var(--text-primary)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
          />
        </label>
        <label className="flex flex-col gap-1.5">
          <span className="text-xs font-medium text-[var(--text-secondary)]">Tax withheld (Rs)</span>
          <input
            type="number" step="0.01" min="0" inputMode="decimal"
            value={value.amount}
            disabled={disabled}
            onChange={(e) => onChange(fromAmount(value, e.target.value, grossAmount))}
            className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2.5 text-sm text-[var(--text-primary)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
          />
        </label>
      </div>

      <div className="flex items-baseline justify-between rounded-lg border border-[var(--border)] px-3 py-2.5">
        <span className="text-xs font-medium text-[var(--text-secondary)]">
          Net paid to {capitalised ? "supplier" : "vendor"}
        </span>
        <span className="text-sm font-bold text-[var(--text-heading)]">{formatRs(netPaid)}</span>
      </div>
      <p className="text-[11px] text-[var(--text-muted)]">
        {capitalised
          ? `The asset is recorded at the full ${formatRs(Number.isFinite(gross) ? gross : 0)}; only the net leaves the account. The rest is owed to FBR.`
          : `The expense is recorded at the full ${formatRs(Number.isFinite(gross) ? gross : 0)}; only the net leaves the account. The rest is owed to FBR.`}
      </p>

      {overridden && (
        <div className="space-y-2 rounded-lg border border-amber-500/25 bg-amber-500/10 p-3">
          <p className="text-xs text-amber-300">
            Changed from the calculated {formatRs(preview!.whtAmount)}
            {preview!.rate > 0 && ` (${Number(preview!.rate.toFixed(4))}%)`} — a reason is required.
          </p>
          <input
            value={value.overrideReason}
            disabled={disabled}
            placeholder="Why does this differ? e.g. figure taken from the vendor invoice"
            onChange={(e) => onChange({ ...value, overrideReason: e.target.value })}
            className="w-full rounded-lg border border-amber-500/25 bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] focus:outline-none"
          />
        </div>
      )}

      {differsFromToday && (
        <p className="rounded-lg border border-[var(--border)] px-3 py-2 text-[11px] text-[var(--text-muted)]">
          This is the tax as originally withheld, and it is kept on save. Today's rules would give{" "}
          {formatRs(preview!.whtAmount)} — filed periods are not restated. Change the amount,
          category, vendor or date to recalculate.
        </p>
      )}

      {preview?.notice && (
        <p className={`text-[11px] ${preview.belowThreshold ? "text-sky-300" : "text-[var(--text-muted)]"}`}>
          {preview.belowThreshold ? "ℹ " : ""}{preview.notice}
        </p>
      )}
    </div>
  );
}
