import { api } from "../../../api/api.ts";
import { financeApiError } from "../../../api/financeAttachments.ts";
import type { Account, Transaction } from "./accountGroups.ts";

async function read<T>(path: string, fallback: string, signal?: AbortSignal): Promise<T> {
  const response = await api(path, { signal });
  if (!response.ok) throw new Error(await financeApiError(response, fallback));
  return await response.json() as T;
}

async function send(path: string, init: RequestInit, fallback: string): Promise<void> {
  const response = await api(path, init);
  if (!response.ok) throw new Error(await financeApiError(response, fallback));
}

export type AccountBody = {
  name: string;
  type: number;
  accountHolderName: string;
  openingBalance: number;
  ledgerCode: string | null;
  displayOrder: number;
  bankOrWalletName: string | null;
  description: string | null;
  concurrencyToken: string;
};

export const accountsApi = {
  details: (id: number, signal?: AbortSignal) => read<Account>(`/api/finance/accounts/${id}`, "The account could not be loaded.", signal),
  /** The server's maximum is 200 transactions per call. */
  transactions: (id: number, signal?: AbortSignal) =>
    read<{ items: Transaction[] }>(`/api/finance/accounts/${id}/transactions?take=200`, "The transactions could not be loaded.", signal),
  save: (body: AccountBody, id: number | null) =>
    send(id === null ? "/api/finance/accounts" : `/api/finance/accounts/${id}`, { method: id === null ? "POST" : "PUT", body: JSON.stringify(body) }, "The account could not be saved."),
  setActive: (id: number, isActive: boolean, concurrencyToken: string) =>
    send(`/api/finance/accounts/${id}/status`, { method: "PATCH", body: JSON.stringify({ isActive, concurrencyToken }) }, "The status could not be changed."),
};
