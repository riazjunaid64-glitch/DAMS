import type { ReactNode } from "react";
import { Checkbox, ChoiceChips, DateField, FieldShell, TextField, TimeField } from "../../components/ui";
import { karachiDateInput } from "../../lib/dates.ts";
import { QUICK_DAYS, quickDayOf, visitDayChoices } from "./leadPage.ts";

export type When = { date: string; time: string };

/** The grey box at the top of a popup naming what it acts on: "Hamza Iqbal" over "In progress · Sana Malik". */
export function DialogSummary({ title, detail }: { title: ReactNode; detail?: ReactNode }) {
  return (
    <div className="rounded-field border border-line bg-page px-4 py-3">
      <p className="m-0 text-body font-extrabold text-ink">{title}</p>
      {detail && <p className="m-0 mt-0.5 text-small text-ink-muted">{detail}</p>}
    </div>
  );
}

/**
 * A day and a time on the Karachi clock, with quick picks: Tomorrow / In 3 days / Next week, or
 * for a site visit tomorrow and the coming weekend. A quick pick keeps the time already set.
 */
export function WhenPicker({ label, required, value, onChange, quick = "days", error }: {
  label?: ReactNode;
  required?: boolean;
  value: When;
  onChange: (value: When) => void;
  quick?: "days" | "visit";
  error?: ReactNode;
}) {
  const choices = quick === "visit" ? visitDayChoices() : QUICK_DAYS;
  const chosen = quick === "visit" ? value.date : quickDayOf(value.date);
  const pick = (choice: string) => onChange({ ...value, date: quick === "visit" ? choice : karachiDateInput(Number(choice)) });
  return (
    <FieldShell as="fieldset" label={label} required={required} error={error}>
      <div className="flex flex-col gap-2.5">
        <ChoiceChips aria-label="Quick pick" options={choices} value={chosen} onChange={pick} />
        <div className="grid grid-cols-2 gap-2.5">
          <DateField aria-label="Date" min={karachiDateInput(0)} value={value.date} onChange={(e) => onChange({ ...value, date: e.target.value })} />
          <TimeField aria-label="Time" value={value.time} onChange={(e) => onChange({ ...value, time: e.target.value })} />
        </div>
      </div>
    </FieldShell>
  );
}

/** "Schedule next follow-up": a what and a when, shown once ticked. */
export function ScheduleNext({ checked, onCheck, title, onTitle, when, onWhen, placeholder }: {
  checked: boolean;
  onCheck: (checked: boolean) => void;
  title: string;
  onTitle: (title: string) => void;
  when: When;
  onWhen: (when: When) => void;
  placeholder: string;
}) {
  return (
    <div className="flex flex-col gap-3 rounded-card border border-gold-line bg-gold-soft px-4 py-3.5">
      <Checkbox label="Schedule next follow-up" checked={checked} onChange={onCheck} />
      {checked && (
        <>
          <TextField aria-label="Next follow-up" required placeholder={placeholder} value={title} onChange={(e) => onTitle(e.target.value)} />
          <WhenPicker value={when} onChange={onWhen} />
        </>
      )}
    </div>
  );
}

/** A titled group of fields in a long form (Contact, Requirement). */
export function FormSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-4">
      <h3 className="m-0 border-b border-line-soft pb-2 text-small font-extrabold text-ink">{title}</h3>
      <div className="grid gap-4 sm:grid-cols-2">{children}</div>
    </section>
  );
}
