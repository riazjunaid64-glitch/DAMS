import { api } from "../../api/api.ts";
import { apiJson, jsonRequest } from "../leads/leadApi.ts";
import { moneyRequest } from "../../lib/idempotency.ts";
import type {
  ExpenseCategory,
  FinanceSettings,
  Vendor,
  VendorOption,
  WhtCalculation,
  WhtDeposit,
  WhtPayableSummary,
  WhtVendorLine,
} from "./whtTypes.ts";

export const listCategories = (includeInactive = false) =>
  apiJson<ExpenseCategory[]>(`/api/finance/expense-categories?includeInactive=${includeInactive}`);

export const saveCategory = (id: number | null, body: unknown) =>
  apiJson<ExpenseCategory>(
    id ? `/api/finance/expense-categories/${id}` : "/api/finance/expense-categories",
    jsonRequest(id ? "PUT" : "POST", body));

export const deleteCategory = (id: number) =>
  apiJson<{ message: string; category: ExpenseCategory | null }>(
    `/api/finance/expense-categories/${id}`, { method: "DELETE" });

export const listVendors = (search: string, activeOnly = false) => {
  const params = new URLSearchParams({ take: "200", activeOnly: String(activeOnly) });
  if (search) params.set("search", search);
  return apiJson<{ items: Vendor[]; hasMore: boolean }>(`/api/finance/vendors?${params}`);
};

export const vendorOptions = (includeInactive = false) =>
  apiJson<VendorOption[]>(`/api/finance/vendors/options?includeInactive=${includeInactive}`);

export const saveVendor = (id: number | null, body: unknown) =>
  apiJson<Vendor>(id ? `/api/finance/vendors/${id}` : "/api/finance/vendors", jsonRequest(id ? "PUT" : "POST", body));

export const getSettings = () => apiJson<FinanceSettings>("/api/finance/wht/settings");

export const saveSettings = (body: unknown) =>
  apiJson<FinanceSettings>("/api/finance/wht/settings", jsonRequest("PUT", body));

export const payableSummary = (from?: string, to?: string) =>
  apiJson<WhtPayableSummary>(`/api/finance/wht/payable-summary${range(from, to)}`);

export const byVendor = (from?: string, to?: string) =>
  apiJson<WhtVendorLine[]>(`/api/finance/wht/by-vendor${range(from, to)}`);

export const listDeposits = (from?: string, to?: string) =>
  apiJson<WhtDeposit[]>(`/api/finance/wht/deposits${range(from, to)}`);

/**
 * Recording a deposit is a money movement, so a create carries an idempotency key: a retry after a
 * lost response must not hand FBR the same challan twice. An edit is protected by its row version
 * instead — it changes a record that already exists.
 *
 * The key is required rather than defaulted. A default would be generated per CALL, which is a fresh
 * key on every retry — it passes the server's check every time and protects nothing, silently.
 */
export const saveDeposit = (id: number | null, body: unknown, idempotencyKey: string) =>
  apiJson<WhtDeposit>(id ? `/api/finance/wht/deposits/${id}` : "/api/finance/wht/deposits",
    id
      ? jsonRequest("PUT", body)
      : moneyRequest(idempotencyKey, jsonRequest("POST", body)));

/** Deleting a deposit puts the tax back on the payable, so it sends the version it is deleting. */
export const deleteDeposit = (id: number, concurrencyToken: string) =>
  apiJson<{ message: string }>(
    `/api/finance/wht/deposits/${id}?concurrencyToken=${encodeURIComponent(concurrencyToken)}`,
    { method: "DELETE" });

/**
 * Preview only — nothing is saved. Called as the expense form changes so the operator sees the
 * tax and the threshold position before committing.
 */
export const calculateWht = (body: {
  categoryId: number | null;
  vendorId: number | null;
  grossAmount: number;
  date?: string;
  excludeExpenseId?: number | null;
  /** The asset-purchase equivalent. Both exist because expenses and purchases share one annual
   *  allowance but number their rows independently. */
  excludeAssetPurchaseId?: number | null;
}, signal?: AbortSignal) => apiJson<WhtCalculation>(
  "/api/finance/wht/calculate",
  { ...jsonRequest("POST", body), signal });

/** Streams the s.165 statement to a file. Uses the raw fetch wrapper because the response is
 *  CSV rather than JSON, and the blob has to be handed to the browser as a download. */
export async function downloadWhtStatement(from?: string, to?: string): Promise<void> {
  const response = await api(`/api/finance/wht/export${range(from, to)}`);
  if (!response.ok) throw new Error("The withholding statement could not be exported.");
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `wht-statement-${from || "start"}-to-${to || "today"}.csv`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function range(from?: string, to?: string): string {
  const params = new URLSearchParams();
  if (from) params.set("from", from);
  if (to) params.set("to", to);
  const query = params.toString();
  return query ? `?${query}` : "";
}
