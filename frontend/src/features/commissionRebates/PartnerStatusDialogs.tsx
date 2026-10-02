import { useState } from "react";
import { ConfirmDialog, useToast } from "../../components/ui";
import { commissionRebateApi } from "./api.ts";
import { ReasonDialog } from "./ReasonDialog.tsx";
import type { Partner } from "./types.ts";

/** What the status call sends when a partner is switched back on. */
const REACTIVATE_REASON = "Reactivated for new business";

type Props = {
  /** The partner being switched off or on, and which. */
  action: { partner: Partner; active: boolean } | null;
  onClose: () => void;
  /** Called after the status changed, so the list and the cards can reload. */
  onChanged: () => void;
};

/**
 * Deactivate asks for a reason in the shared reason popup and keeps it open with the server's
 * message when refused. Reactivate asks first, and a refusal shows as a toast.
 */
export function PartnerStatusDialogs({ action, onClose, onChanged }: Props) {
  const toast = useToast();
  const [reactivating, setReactivating] = useState(false);
  if (!action) return null;
  const { partner } = action;

  if (!action.active) {
    return (
      <ReasonDialog
        title={`Deactivate ${partner.name}?`}
        message="They can't get new commissions, and their pending commissions can't be paid or changed until they are reactivated."
        confirmLabel="Deactivate"
        keyPrefix={`partner-deactivate-${partner.id}`}
        onConfirm={async (reason) => {
          await commissionRebateApi.partnerStatus(partner.id, { isActive: false, reason, concurrencyToken: partner.concurrencyToken });
          toast.success(`${partner.name} deactivated.`);
          onChanged();
        }}
        onClose={onClose}
      />
    );
  }

  const reactivate = async () => {
    setReactivating(true);
    try {
      await commissionRebateApi.partnerStatus(partner.id, { isActive: true, reason: REACTIVATE_REASON, concurrencyToken: partner.concurrencyToken });
      toast.success(`${partner.name} reactivated.`);
      onChanged();
      onClose();
    } catch (failure) {
      toast.error(failure instanceof Error ? failure.message : "The partner could not be reactivated.");
    } finally {
      setReactivating(false);
    }
  };

  return (
    <ConfirmDialog
      open
      onClose={onClose}
      onConfirm={() => void reactivate()}
      title={`Reactivate ${partner.name}?`}
      message="They can get new commissions and be paid again."
      confirmLabel="Reactivate"
      loading={reactivating}
    />
  );
}
