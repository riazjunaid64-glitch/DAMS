import { apiJson, jsonRequest } from "../leads/leadApi.ts";

export interface RevenueCategory {
  id: number;
  name: string;
  code: string;
  description: string | null;
  displayOrder: number;
  isActive: boolean;
  /** How many revenue rows are filed under this head — what decides retire versus delete. */
  revenueCount: number;
  createdAt: string;
  updatedAt: string | null;
  concurrencyToken: string;
}

export interface SaveRevenueCategory {
  name: string;
  code: string;
  description: string | null;
  displayOrder: number;
  isActive: boolean;
  concurrencyToken: string | null;
}

export const listRevenueCategories = (includeInactive = false) =>
  apiJson<RevenueCategory[]>(`/api/finance/revenue-categories?includeInactive=${includeInactive}`);

export const saveRevenueCategory = (id: number | null, body: SaveRevenueCategory) =>
  apiJson<RevenueCategory>(
    id ? `/api/finance/revenue-categories/${id}` : "/api/finance/revenue-categories",
    jsonRequest(id ? "PUT" : "POST", body));

/**
 * Retires the category when revenue already refers to it, and deletes it when nothing does — the
 * server decides which, and says so in the message. `category` is null when the row was removed.
 */
export const deleteRevenueCategory = (id: number) =>
  apiJson<{ message: string; category: RevenueCategory | null }>(
    `/api/finance/revenue-categories/${id}`, { method: "DELETE" });
