import { api } from "../../../api/api.ts";
import { financeApiError } from "../../../api/financeAttachments.ts";
import { moneyRequest } from "../../../lib/idempotency.ts";
import type { AccountOption, Partner, Statement } from "./types.ts";

async function read<T>(path: string, fallback: string, signal?: AbortSignal): Promise<T> {
  const response = await api(path, { signal });
  if (!response.ok) throw new Error(await financeApiError(response, fallback));
  return await response.json() as T;
}

async function send(path: string, init: RequestInit, fallback: string): Promise<void> {
  const response = await api(path, init);
  if (!response.ok) throw new Error(await financeApiError(response, fallback));
}

const root = "/api/finance/partners";

export type PartnerBody = {
  name: string;
  cnic: string | null;
  ntn: string | null;
  profitSharePercent: number;
  financeAccountId: number | null;
  isActive: boolean;
  joinedDate: string | null;
  exitedDate: string | null;
  concurrencyToken: string | null;
};

export type ShareBody = { id: number; profitSharePercent: number; isActive: boolean; concurrencyToken: string };

export const capitalApi = {
  /** Every partner, active or not: the list and Edit shares both need them all. */
  partners: (signal?: AbortSignal) => read<Partner[]>(`${root}?includeInactive=true`, "Partners could not be loaded.", signal),
  accounts: (cashLikeOnly: boolean, signal?: AbortSignal) =>
    read<AccountOption[]>(`/api/finance/accounts/options?includeInactive=true&cashLikeOnly=${cashLikeOnly}`, "The accounts could not be loaded.", signal),
  saveShares: (partners: ShareBody[]) => send(`${root}/shares`, { method: "PUT", body: JSON.stringify({ partners }) }, "Shares could not be saved."),
  savePartner: (body: PartnerBody, id: number | null) =>
    send(id === null ? root : `${root}/${id}`, { method: id === null ? "POST" : "PUT", body: JSON.stringify(body) }, "The partner could not be saved."),
  statement: (id: number, from: string, to: string, signal?: AbortSignal) => {
    const query = new URLSearchParams();
    if (from) query.set("from", from);
    if (to) query.set("to", to);
    const suffix = query.toString();
    return read<Statement>(`${root}/${id}/statement${suffix ? `?${suffix}` : ""}`, "The statement could not be loaded.", signal);
  },
  /** The money-request key makes a double click or a retry record the movement once. */
  recordTransaction: (id: number, body: FormData, idempotencyKey: string) =>
    send(`${root}/${id}/transactions/form`, moneyRequest(idempotencyKey, { method: "POST", body }), "The transaction could not be saved."),
};
