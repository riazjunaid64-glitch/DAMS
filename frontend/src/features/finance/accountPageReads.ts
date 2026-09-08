type AccountFilters = {
  search: string;
  status: string;
  typeFilter: string;
  holderFilter: string;
};

export async function readFinanceAccountsPage<TAccount, TOverview>(
  filters: AccountFilters,
  includeOverview: boolean,
  request: (path: string) => Promise<Response>,
): Promise<{ items: TAccount[]; hasMore: boolean; overview?: TOverview }> {
  const params = new URLSearchParams({ take: "200" });
  if (filters.search) params.set("search", filters.search);
  if (filters.status !== "all") params.set("isActive", String(filters.status === "active"));
  if (filters.typeFilter) params.set("type", filters.typeFilter);
  if (filters.holderFilter) params.set("holder", filters.holderFilter);
  const endpoint = includeOverview ? "/api/finance/accounts/page-with-overview" : "/api/finance/accounts";
  const response = await request(`${endpoint}?${params}`);
  if (!response.ok) throw new Error("Accounts could not be loaded.");
  return await response.json();
}
