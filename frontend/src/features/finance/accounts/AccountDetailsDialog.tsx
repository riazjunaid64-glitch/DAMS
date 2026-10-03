import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button, DataTable, EmptyState, IconPencil, Modal, Notice, useIsPhone, type DataTableColumn } from "../../../components/ui";
import { cx } from "../../../components/ui/cx.ts";
import { DialogTitle } from "../../../components/ui/DialogTitle.tsx";
import { showDay } from "../capitalPartners/rules.ts";
import { TAX_TO_FBR_PATH } from "../home/paths.ts";
import { formatRs } from "../whtTypes.ts";
import { accountsApi } from "./api.ts";
import { accountSubtitle, isCashLike, oldestFirst, withRunningBalances, type Account, type Transaction } from "./accountGroups.ts";

type Line =
  | { kind: "opening"; key: string; balance: number }
  | { kind: "row"; key: string; transaction: Transaction; balance: number | null };

type Props = {
  account: Account;
  onClose: () => void;
  onEdit: (account: Account) => void;
  onToggleActive: (account: Account) => void;
};

const money = (value: number, className?: string) => <span className={cx("whitespace-nowrap tabular-nums", className)}>{formatRs(value)}</span>;
const signed = (value: number) => <span className={cx("whitespace-nowrap font-extrabold tabular-nums", value >= 0 ? "text-success" : "text-danger")}>{value >= 0 ? "+" : ""}{formatRs(value)}</span>;

/** An account's figures and its transactions with a running balance; Edit and Deactivate live in the phone footer. */
export function AccountDetailsDialog({ account: listed, onClose, onEdit, onToggleActive }: Props) {
  const isPhone = useIsPhone();
  const navigate = useNavigate();
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<{ attempt: number; account: Account; transactions: Transaction[] | null; error: string | null }>(
    { attempt: -1, account: listed, transactions: null, error: null },
  );

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    Promise.all([accountsApi.details(listed.id, signal), accountsApi.transactions(listed.id, signal)])
      .then(([detail, history]) => {
        if (!signal.aborted) setResult({ attempt, account: detail, transactions: history.items, error: null });
      })
      .catch((failure: unknown) => {
        if (signal.aborted) return;
        setResult((current) => ({ ...current, attempt, transactions: null, error: failure instanceof Error ? failure.message : "The account could not be loaded." }));
      });
    return () => controller.abort();
  }, [listed.id, attempt]);

  const load = () => setAttempt((current) => current + 1);
  const { account } = result;
  const settled = result.attempt === attempt;
  const transactions = settled ? result.transactions : null;
  const error = settled ? result.error : null;

  const held = account.whtWithheld - account.whtDeposited;
  const cashLike = isCashLike(account.type);
  // The running balance needs every transaction; with fewer than the account has, it would be wrong.
  const complete = transactions !== null && transactions.length >= account.transactionCount;
  const lines: Line[] = transactions === null ? [] : [
    // The phone list starts at the first transaction; the opening figure is in the box above.
    ...(complete && !isPhone ? [{ kind: "opening" as const, key: "opening", balance: account.openingBalance }] : []),
    ...(complete
      ? withRunningBalances(oldestFirst(transactions), account.openingBalance).map<Line>(({ transaction, balance }) => ({ kind: "row", key: `${transaction.kind}-${transaction.recordId}`, transaction, balance }))
      : oldestFirst(transactions).map<Line>((transaction) => ({ kind: "row", key: `${transaction.kind}-${transaction.recordId}`, transaction, balance: null }))),
  ];

  const details = (transaction: Transaction) => [showDay(transaction.date), transaction.projectName, transaction.reference].filter(Boolean).join(" · ");
  const withheld = (transaction: Transaction) => transaction.whtAmount > 0 && (
    <span className="block text-small text-gold-text">{formatRs(transaction.grossAmount)} invoiced · {formatRs(transaction.whtAmount)} tax withheld</span>
  );

  const columns: DataTableColumn<Line>[] = [
    {
      key: "transaction",
      header: "Transaction",
      render: (line) => line.kind === "opening"
        ? <span className="font-extrabold">Opening balance</span>
        : (
          <>
            <span className="block font-extrabold">{line.transaction.label}</span>
            <span className="block text-small text-ink-muted">{details(line.transaction)}</span>
            {withheld(line.transaction)}
          </>
        ),
    },
    { key: "amount", header: "Amount", align: "right", render: (line) => line.kind === "row" ? signed(line.transaction.amount) : null },
    { key: "balance", header: "Balance", align: "right", render: (line) => line.balance === null ? null : money(line.balance, "font-extrabold") },
  ];

  const figure = (label: string, value: number, className?: string) => (
    <div className="min-w-0">
      <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-2">{label}</p>
      <p className={cx("m-0 mt-0.5 text-section font-extrabold", className)}><span className="whitespace-nowrap">{formatRs(value)}</span></p>
    </div>
  );

  const phoneFooter = (
    <div className="flex w-full gap-3">
      <Button variant="outline" className="flex-1" icon={<IconPencil size={16} />} onClick={() => onEdit(account)}>Edit</Button>
      {/* An active system account cannot be deactivated, so it has Edit only. */}
      {!(account.isSystemAccount && account.isActive) && (
        <Button variant={account.isActive ? "danger" : "outline"} className="flex-1" onClick={() => onToggleActive(account)}>
          {account.isActive ? "Deactivate" : "Reactivate"}
        </Button>
      )}
    </div>
  );

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      phoneLayout="fullscreen"
      title={<DialogTitle title={account.name} subtitle={accountSubtitle(account)} />}
      cancelLabel="Close"
      footer={isPhone ? phoneFooter : undefined}
    >
      <div className="flex flex-col gap-4">
        <div className="grid grid-cols-2 gap-x-4 gap-y-3 rounded-card border border-line bg-page p-4">
          {figure("Opening balance", account.openingBalance)}
          {figure("Current balance", account.currentBalance)}
          {figure(cashLike ? "Money in" : "Increases", account.revenueReceived, "text-success")}
          {figure(cashLike ? "Money out" : "Decreases", account.expensesPaid, "text-danger")}
        </div>

        {/* Tax withheld from suppliers sits inside the balance but is owed to FBR until a deposit is recorded. */}
        {held > 0 && (
          <Notice
            tone="gold"
            title={`Includes ${formatRs(held)} tax held for FBR`}
            action={<Button variant="outline" onClick={() => navigate(TAX_TO_FBR_PATH)}>Record deposit</Button>}
          />
        )}

        {error ? (
          <Notice tone="red" role="alert" title={error} action={<Button variant="outline" onClick={load}>Try again</Button>} />
        ) : transactions === null ? (
          <div role="status" aria-busy="true" className="flex flex-col gap-2.5">
            <span className="sr-only">Loading</span>
            {[0, 1, 2].map((index) => <span key={index} aria-hidden="true" className="h-12 animate-pulse rounded-card bg-track" />)}
          </div>
        ) : transactions.length === 0 ? (
          <EmptyState title="No transactions assigned to this account." />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={lines}
              rowKey={(line) => line.key}
              dense
              minWidth={0}
              maxHeight="46vh"
              caption="Account transactions"
              phoneCard={(line) => line.kind === "opening" ? null : (
                <div className="border-b border-line-soft py-3">
                  <div className="flex items-baseline justify-between gap-3">
                    <span className="min-w-0 font-extrabold">{line.transaction.label}</span>
                    {signed(line.transaction.amount)}
                  </div>
                  <div className="mt-0.5 flex items-start justify-between gap-3 text-small text-ink-muted">
                    <span className="min-w-0 break-normal">{details(line.transaction)}</span>
                    {line.balance !== null && <span className="shrink-0 whitespace-nowrap">Balance <b className="font-extrabold text-ink">{formatRs(line.balance)}</b></span>}
                  </div>
                  {withheld(line.transaction)}
                </div>
              )}
            />
            {!complete && (
              <p className="m-0 text-center text-small text-ink-muted">Showing the {transactions.length} most recent transactions; running balance hidden.</p>
            )}
          </>
        )}
      </div>
    </Modal>
  );
}
