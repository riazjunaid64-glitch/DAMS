import { api } from "../../../api/api.ts";
import { financeApiError, openAttachmentAt, openFinanceAttachment } from "../../../api/financeAttachments.ts";
import { moneyRequest } from "../../../lib/idempotency.ts";
import type { CashAccount } from "../loans/types.ts";
import type { Holder, HistoryItem, Overview, Statement } from "./types.ts";

async function read<T>(path: string, fallback: string, signal?: AbortSignal): Promise<T> {
  const response = await api(path, { signal });
  if (!response.ok) throw new Error(await financeApiError(response, fallback));
  return await response.json() as T;
}

async function send<T>(path: string, init: RequestInit, fallback: string): Promise<T> {
  const response = await api(path, init);
  if (!response.ok) throw new Error(await financeApiError(response, fallback));
  return await response.json() as T;
}

const root = "/api/finance/staff-cash";

export const staffCashApi = {
  /** Every person, settled ones included: the totals count them all. */
  overview: (signal?: AbortSignal) => read<Overview>(`${root}?includeSettled=true`, "Could not load staff cash.", signal),
  cashAccounts: (signal?: AbortSignal) =>
    read<CashAccount[]>("/api/finance/accounts/options?includeInactive=true&cashLikeOnly=true", "The cash and bank accounts could not be loaded.", signal),
  /** Newest first. Each page's balances are worked out from its own first row, so any page is right on its own. */
  statement: (id: number, skip: number, take: number, signal?: AbortSignal) =>
    read<Statement>(`${root}/${id}?skip=${skip}&take=${take}`, "Could not load this person's history.", signal),
  addPerson: (personName: string) =>
    send<Holder>(`${root}/holders`, { method: "POST", body: JSON.stringify({ personName }) }, "Could not add this person."),
  /**
   * Always the multipart route, file or no file: the slip and the figures are one save. The
   * money-request key makes a double click or a retry record the movement once.
   */
  recordMovement: (personId: number, body: FormData, idempotencyKey: string) =>
    send<unknown>(`${root}/${personId}/transfers/form`, moneyRequest(idempotencyKey, { method: "POST", body }), "Could not record this movement."),
  /** A correction carries the row version in the form instead of a key. */
  correctMovement: (personId: number, transferId: number, body: FormData) =>
    send<unknown>(`${root}/${personId}/transfers/${transferId}/form`, { method: "PUT", body }, "Could not correct this movement."),
  deleteMovement: (personId: number, row: HistoryItem) =>
    send<unknown>(
      `${root}/${personId}/transfers/${row.recordId}?concurrencyToken=${encodeURIComponent(row.concurrencyToken ?? "")}`,
      { method: "DELETE" },
      "Could not delete this movement.",
    ),
  /**
   * Not a staff-cash record: an ordinary expense with this float as the paying account. That is the
   * whole double entry, which is why it shows here and in the Finance home expenses table alike.
   */
  recordExpense: (body: FormData, idempotencyKey: string) =>
    send<unknown>("/api/Finance/expenses/form", moneyRequest(idempotencyKey, { method: "POST", body }), "Could not record this expense."),
  /** Two stores behind one column: a transfer's slip hangs off the float, an expense's receipt off the expense. */
  openAttachment: (personId: number, row: HistoryItem, download: boolean) => {
    const fileName = row.attachment?.fileName ?? "attachment";
    return row.recordType === "Expense"
      ? openFinanceAttachment("expense", row.recordId, fileName, download)
      : openAttachmentAt(`${root}/${personId}/transfers/${row.recordId}/attachment`, fileName, download);
  },
};
