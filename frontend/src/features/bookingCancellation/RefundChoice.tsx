import { useId } from "react";
import { cx, FieldShell } from "../../components/ui";
import type { CancellationRefundDecision } from "./types";

const OPTIONS: { value: CancellationRefundDecision; label: string }[] = [
  { value: "None", label: "No refund" },
  { value: "PayNow", label: "Pay now" },
  { value: "PayLater", label: "Pay later" },
];

type Props = {
  value: CancellationRefundDecision | "";
  onChange: (value: CancellationRefundDecision) => void;
  disabled?: boolean;
  error?: string;
};

/**
 * The refund question as three radio cards: one row on desktop, stacked on a phone. Native radios
 * inside labels, so the keyboard and screen readers work as they do for any radio group.
 */
export function RefundChoice({ value, onChange, disabled = false, error }: Props) {
  const name = useId();
  return (
    <FieldShell as="fieldset" label="Refund" required error={error}>
      <div role="radiogroup" aria-label="Refund" className="grid gap-2.5 md:grid-cols-3">
        {OPTIONS.map((option) => (
          <label
            key={option.value}
            className={cx(
              "flex min-h-12 cursor-pointer items-center gap-2.5 rounded-field border bg-card px-3.5 text-body font-extrabold text-ink transition-colors",
              "has-[:checked]:border-primary has-[:checked]:ring-1 has-[:checked]:ring-primary has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-primary",
              "has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50",
              error ? "border-danger" : "border-line-input",
            )}
          >
            <input
              type="radio"
              name={name}
              value={option.value}
              checked={value === option.value}
              disabled={disabled}
              onChange={() => onChange(option.value)}
              className="size-[18px] shrink-0 cursor-pointer accent-primary disabled:cursor-not-allowed"
            />
            {option.label}
          </label>
        ))}
      </div>
    </FieldShell>
  );
}
