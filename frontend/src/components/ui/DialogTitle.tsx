import type { ReactNode } from "react";

/** A popup title with the small grey line under it ("BK-000045 · Unit B09"). */
export function DialogTitle({ title, subtitle }: { title: ReactNode; subtitle?: ReactNode }) {
  return (
    <>
      <span className="block">{title}</span>
      {subtitle && <span className="mt-0.5 block text-small font-normal text-ink-muted">{subtitle}</span>}
    </>
  );
}
