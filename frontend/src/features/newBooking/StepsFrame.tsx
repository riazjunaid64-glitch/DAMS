import type { ReactNode } from "react";
import { Button, IconCheck, IconClose, cx, useIsPhone } from "../../components/ui";
import { STEPS, type StepIndex } from "./draft.ts";

type Props = {
  step: StepIndex;
  /** Shown in the Summary card; hidden on Review. */
  summary: { label: string; value: string }[];
  /** "Cancel" on the first step, "Back" after it. */
  onBack: () => void;
  onContinue: () => void;
  /** Close (×) on a phone. */
  onClose: () => void;
  continueLabel?: string;
  continueDisabled?: boolean;
  saving?: boolean;
  children: ReactNode;
};

/** The steps of New booking: a list with ticks on desktop, a progress bar on a phone. */
function StepsList({ step }: { step: StepIndex }) {
  return (
    <nav aria-label="Steps" className="rounded-card border border-line bg-card p-2 font-ui">
      <ol className="m-0 flex list-none flex-col gap-0.5 p-0">
        {STEPS.map((name, index) => {
          const done = index < step;
          const current = index === step;
          return (
            <li key={name} aria-current={current ? "step" : undefined} className={cx("flex items-center gap-3 rounded-field px-3 py-2.5", current && "bg-page")}>
              <span
                aria-hidden="true"
                className={cx(
                  "flex size-7 shrink-0 items-center justify-center rounded-full border text-small font-extrabold",
                  done ? "border-success bg-success text-white" : current ? "border-primary bg-primary text-white" : "border-line-input bg-card text-ink-faint",
                )}
              >
                {done ? <IconCheck size={14} /> : index + 1}
              </span>
              <span className={cx("text-body font-extrabold", current || done ? "text-ink" : "text-ink-faint")}>{name}</span>
              {done && <span className="sr-only">done</span>}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

function SummaryCard({ rows }: { rows: Props["summary"] }) {
  return (
    <aside aria-label="Summary" className="rounded-card border border-line bg-card p-[18px] font-ui">
      <h2 className="m-0 mb-2 text-section font-extrabold text-ink">Summary</h2>
      <dl className="m-0 flex flex-col gap-1.5">
        {rows.map((row) => (
          <div key={row.label} className="flex items-baseline justify-between gap-4">
            <dt className="text-small font-bold text-ink-2">{row.label}</dt>
            <dd className="m-0 text-right text-small font-extrabold text-ink">{row.value}</dd>
          </div>
        ))}
      </dl>
    </aside>
  );
}

/**
 * The frame every New booking step sits in. Desktop: the steps on the left, the form card in the
 * middle with Back / Continue under it, the Summary on the right. Phone: a full-screen form with its
 * own header, the step name with a progress bar, and Back / Continue fixed at the bottom.
 */
export function StepsFrame({ step, summary, onBack, onContinue, onClose, continueLabel = "Continue", continueDisabled = false, saving = false, children }: Props) {
  const isPhone = useIsPhone();
  const name = STEPS[step];
  const backLabel = step === 0 ? "Cancel" : "Back";
  const buttons = (
    <>
      <Button variant="outline" size={isPhone ? "lg" : "md"} disabled={saving} onClick={onBack} className={isPhone ? "flex-1" : undefined}>{backLabel}</Button>
      <Button size={isPhone ? "lg" : "md"} loading={saving} disabled={continueDisabled} onClick={onContinue} className={isPhone ? "flex-[2]" : undefined}>{continueLabel}</Button>
    </>
  );

  if (isPhone) {
    return (
      <div className="fixed inset-0 z-40 flex flex-col bg-page font-ui" role="region" aria-label="New booking">
        <header className="flex items-center justify-between border-b border-line bg-card px-4 py-3">
          <h1 className="m-0 text-section font-extrabold text-ink">New booking</h1>
          <Button iconOnly variant="ghost" icon={<IconClose size={18} />} aria-label="Close" onClick={onClose} />
        </header>
        <div className="border-b border-line bg-card px-4 pb-3 pt-2.5">
          <div className="flex items-baseline justify-between">
            <span className="text-body font-extrabold text-ink">{name}</span>
            <span className="text-small font-extrabold text-ink-2">Step {step + 1} of {STEPS.length}</span>
          </div>
          <div role="progressbar" aria-label="Progress" aria-valuemin={1} aria-valuemax={STEPS.length} aria-valuenow={step + 1} className="mt-2 h-1 overflow-hidden rounded-full bg-track">
            <div className="h-full rounded-full bg-primary" style={{ width: `${((step + 1) / STEPS.length) * 100}%` }} />
          </div>
        </div>
        <div className="flex-1 overflow-y-auto px-4 py-4">{children}</div>
        <footer className="flex gap-3 border-t border-line bg-card px-4 py-3 pb-[max(12px,env(safe-area-inset-bottom))]">{buttons}</footer>
      </div>
    );
  }

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-5 px-8 py-7 font-ui">
      <h1 className="m-0 text-page-title font-extrabold text-ink">New booking</h1>
      <div className={cx("grid items-start gap-5", step === 4 ? "grid-cols-[220px_minmax(0,1fr)]" : "grid-cols-[220px_minmax(0,1fr)_280px]")}>
        <StepsList step={step} />
        <section className="overflow-hidden rounded-card border border-line bg-card" aria-label={name}>
          <header className="border-b border-line-soft px-5 py-4">
            <p className="m-0 text-caption font-extrabold uppercase tracking-[0.4px] text-ink-muted">Step {step + 1} of {STEPS.length}</p>
            <h2 className="m-0 mt-0.5 text-section font-extrabold text-ink">{name}</h2>
          </header>
          <div className="px-5 py-5">{children}</div>
          <footer className="flex items-center justify-between border-t border-line-soft px-5 py-4">{buttons}</footer>
        </section>
        {step !== 4 && <SummaryCard rows={summary} />}
      </div>
    </div>
  );
}
