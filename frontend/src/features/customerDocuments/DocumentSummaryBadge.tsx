import type { DocumentSummary } from "./types.ts";

export default function DocumentSummaryBadge({ summary, compact = false }: { summary: DocumentSummary; compact?: boolean }) {
  const tone = summary.isComplete ? "border-emerald-500/25 bg-emerald-500/10 text-emerald-300"
    : summary.replacementRequired > 0 ? "border-rose-500/25 bg-rose-500/10 text-rose-300"
    : summary.postponedDue > 0 ? "border-rose-500/25 bg-rose-500/10 text-rose-300"
    : "border-amber-500/25 bg-amber-500/10 text-amber-300";
  return (
    <span className={`inline-flex items-center gap-2 rounded-full border px-2.5 py-1 text-xs font-medium ${tone}`}
      aria-label={`Document completion: ${summary.label}`}>
      <span className="h-1.5 w-1.5 rounded-full bg-current" />
      {compact ? summary.label : `${summary.completedRequired} of ${summary.requiredTotal} complete · ${summary.label}`}
    </span>
  );
}
