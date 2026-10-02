import { ConfirmDialog } from "../../../components/ui";
import type { PendingDelete } from "./types.ts";

const COPY: Record<PendingDelete["kind"], { title: string; message: string }> = {
  revenue: { title: "Delete this manual revenue entry?", message: "" },
  expense: { title: "Delete this expense?", message: "" },
  assetPurchase: {
    title: "Delete this fixed asset purchase?",
    message: "The bank balance and the asset account both move back.",
  },
};

export function FinanceDeleteDialog({
  pending,
  deleting,
  onClose,
  onConfirm,
}: {
  pending: PendingDelete | null;
  deleting: boolean;
  onClose: () => void;
  onConfirm: () => void;
}) {
  const copy = pending ? COPY[pending.kind] : COPY.revenue;
  return (
    <ConfirmDialog
      open={pending != null}
      title={copy.title}
      message={copy.message}
      confirmLabel="Delete"
      danger
      loading={deleting}
      onClose={onClose}
      onConfirm={onConfirm}
    />
  );
}
