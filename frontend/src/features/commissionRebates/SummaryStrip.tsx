import type { ReactNode } from "react";

/** The gold strip inside a popup that shows one worked-out figure ("Commission Rs 256,000"). */
export function SummaryStrip({ label, value, note }: { label: string; value: ReactNode; note?: ReactNode }) {
  return (
    <div className="rounded-card border border-gold-line bg-gold-soft px-4 py-3 font-ui">
      <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-gold-text">{label}</p>
      <p className="m-0 mt-0.5 text-section font-extrabold tabular-nums text-ink">{value}</p>
      {note && <p className="m-0 mt-1 text-small text-ink-2">{note}</p>}
    </div>
  );
}
