import { api } from "../../../api/api.ts";
import { financeApiError, openAttachmentAt } from "../../../api/financeAttachments.ts";
import { moneyRequest } from "../../../lib/idempotency.ts";
import type { CashAccount, Loan, LoanAccount, LoanTransaction, Statement } from "./types.ts";

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

const root = "/api/finance/loans";

export type LoanBody = { name: string; lenderName: string | null; financeAccountId: number; isActive: boolean; concurrencyToken: string };

/** The server's own cap on one statement page. */
export const STATEMENT_MAX_TAKE = 200;

export const loansApi = {
  /** Every loan, active or closed: the list filters them on screen. */
  loans: (signal?: AbortSignal) => read<Loan[]>(`${root}?includeInactive=true`, "Loans could not be loaded.", signal),
  loanAccounts: (signal?: AbortSignal) => read<LoanAccount[]>(`${root}/account-options?includeInactive=true`, "The loan accounts could not be loaded.", signal),
  cashAccounts: (signal?: AbortSignal) =>
    read<CashAccount[]>("/api/finance/accounts/options?includeInactive=true&cashLikeOnly=true", "The cash and bank accounts could not be loaded.", signal),
  /** Newest first. Each row's outstanding figure is worked out over the whole history, so any page is right on its own. */
  statement: (id: number, skip: number, take: number, signal?: AbortSignal) =>
    read<Statement>(`${root}/${id}/statement?skip=${skip}&take=${take}`, "Loan activity could not be loaded.", signal),
  saveLoan: (body: LoanBody, id: number | null) =>
    send<Loan>(id === null ? root : `${root}/${id}`, { method: id === null ? "POST" : "PUT", body: JSON.stringify(body) }, "The loan could not be saved."),
  /**
   * One multipart request, with or without a file, so the evidence is saved with the figures it
   * supports. The money-request key makes a double click or a retry record the movement once.
   */
  recordMovement: (loanId: number, body: FormData, idempotencyKey: string) =>
    send<unknown>(`${root}/${loanId}/transactions/form`, moneyRequest(idempotencyKey, { method: "POST", body }), "The loan movement could not be saved."),
  /** A correction carries the row version in the form instead of a key. */
  correctMovement: (loanId: number, movementId: number, body: FormData) =>
    send<unknown>(`${root}/${loanId}/transactions/${movementId}/form`, { method: "PUT", body }, "The correction could not be saved."),
  deleteMovement: (loanId: number, movement: LoanTransaction) =>
    send<unknown>(
      `${root}/${loanId}/transactions/${movement.id}?concurrencyToken=${encodeURIComponent(movement.concurrencyToken)}`,
      { method: "DELETE" },
      "The loan movement could not be deleted.",
    ),
  openAttachment: (loanId: number, movement: LoanTransaction, download: boolean) =>
    openAttachmentAt(`${root}/${loanId}/transactions/${movement.id}/attachment`, movement.attachment?.fileName ?? "attachment", download),
  /** Every movement of a loan, page by page, for the export. */
  async allMovements(loanId: number): Promise<LoanTransaction[]> {
    const rows: LoanTransaction[] = [];
    for (;;) {
      const page = await loansApi.statement(loanId, rows.length, STATEMENT_MAX_TAKE);
      rows.push(...page.items);
      if (!page.hasMore || page.items.length === 0) return rows;
    }
  },
};
