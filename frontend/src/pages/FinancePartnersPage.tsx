import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { User } from "../App.tsx";
import { ActionsMenu, Button, IconPlus, Notice, PageHeader, useIsPhone } from "../components/ui";
import { pageAccess } from "../features/access/permissions.ts";
import { EditSharesDialog } from "../features/finance/capitalPartners/EditSharesDialog.tsx";
import { PartnerFormDialog } from "../features/finance/capitalPartners/PartnerFormDialog.tsx";
import { PartnersTable } from "../features/finance/capitalPartners/PartnersTable.tsx";
import { activeShareTotal, formatShare, sharesAddUp } from "../features/finance/capitalPartners/rules.ts";
import { StatementDialog } from "../features/finance/capitalPartners/StatementDialog.tsx";
import { TransactionDialog } from "../features/finance/capitalPartners/TransactionDialog.tsx";
import type { Partner } from "../features/finance/capitalPartners/types.ts";
import { useCapitalData } from "../features/finance/capitalPartners/useCapitalData.ts";
import { usePageTrail } from "../layouts/trail.ts";
import { useIdempotencyKeys } from "../lib/idempotency.ts";

type Dialog =
  | { kind: "shares" }
  | { kind: "partner"; partner: Partner | null }
  | { kind: "transaction"; partner: Partner }
  | { kind: "statement"; partner: Partner };

/** Finance → Capital partners: each partner's capital, the shares in one popup, and the statement. */
export default function FinancePartnersPage({ user }: { user: User | null }) {
  usePageTrail([{ label: "Capital partners" }]);
  const access = pageAccess(user?.role, "finance");
  const allowed = access === "allow";
  const navigate = useNavigate();
  const isPhone = useIsPhone();
  const data = useCapitalData(allowed);
  const keys = useIdempotencyKeys();
  const [dialog, setDialog] = useState<Dialog | null>(null);

  useEffect(() => {
    if (access === "deny") navigate("/");
  }, [access, navigate]);

  if (!allowed) {
    return access === "deny" ? null : (
      <div role="status" aria-busy="true" className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:px-8 md:py-7">
        <span className="sr-only">Loading</span>
        <span aria-hidden="true" className="h-9 w-64 animate-pulse rounded bg-track" />
        <span aria-hidden="true" className="h-64 animate-pulse rounded-card bg-track" />
      </div>
    );
  }

  const { partners } = data;
  // The saved shares of the active partners, as the server will check them.
  const total = activeShareTotal(partners.map((partner) => ({ isActive: partner.isActive, share: partner.profitSharePercent })));
  const sharesOff = partners.length > 0 && !sharesAddUp(total);
  const editShares = () => setDialog({ kind: "shares" });
  const addPartner = () => setDialog({ kind: "partner", partner: null });
  const close = () => setDialog(null);
  const failedEmpty = data.error !== null && partners.length === 0;

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      <PageHeader
        title="Capital partners"
        className="max-md:flex-row max-md:items-start max-md:justify-between"
        actions={isPhone ? (
          <>
            <Button icon={<IconPlus size={16} />} onClick={addPartner}>Add partner</Button>
            <ActionsMenu trigger="dots" aria-label="More for capital partners" items={[{ label: "Edit shares", disabled: partners.length === 0, onSelect: editShares }]} />
          </>
        ) : (
          <>
            <Button variant="outline" disabled={partners.length === 0} onClick={editShares}>Edit shares</Button>
            <Button icon={<IconPlus size={16} />} onClick={addPartner}>Add partner</Button>
          </>
        )}
      />

      {sharesOff && (
        <Notice
          tone="red"
          role="alert"
          title={`Active shares add up to ${formatShare(total)}. They must total 100%.`}
          action={<Button variant="outline" onClick={editShares}>Edit shares</Button>}
        />
      )}
      {data.error && (
        <Notice tone="red" role="alert" title={data.error} action={<Button variant="outline" onClick={data.reload}>Try again</Button>} />
      )}
      {!failedEmpty && (
        <PartnersTable
          partners={partners}
          loading={data.loading}
          onAdd={addPartner}
          onStatement={(partner) => setDialog({ kind: "statement", partner })}
          onTransaction={(partner) => setDialog({ kind: "transaction", partner })}
          onEdit={(partner) => setDialog({ kind: "partner", partner })}
        />
      )}

      {dialog?.kind === "shares" && <EditSharesDialog partners={partners} onClose={close} onSaved={data.reload} />}
      {dialog?.kind === "partner" && (
        <PartnerFormDialog
          key={dialog.partner?.id ?? "new"}
          partner={dialog.partner}
          partners={partners}
          accounts={data.allAccounts}
          accountsError={data.capitalAccountsError}
          onClose={close}
          onSaved={data.reload}
        />
      )}
      {dialog?.kind === "transaction" && (
        <TransactionDialog partner={dialog.partner} cashAccounts={data.cashAccounts} accountsError={data.cashAccountsError} keys={keys} onClose={close} onSaved={data.reload} />
      )}
      {dialog?.kind === "statement" && <StatementDialog partner={dialog.partner} cashAccounts={data.cashAccounts} onClose={close} />}
    </div>
  );
}
