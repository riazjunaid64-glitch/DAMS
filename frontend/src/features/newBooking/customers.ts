/** A customer as the Existing customer search shows and picks them. */
export interface PickerCustomer {
  id: number;
  fullName: string;
  phone: string;
  cnic: string | null;
}

/** Reads the customer list answer into what the picker needs, dropping anything without an id or name. */
export function parseCustomers(payload: unknown): PickerCustomer[] {
  const items = (payload as { items?: unknown } | null)?.items;
  if (!Array.isArray(items)) return [];
  return items.flatMap((raw) => {
    const row = raw as Record<string, unknown>;
    const id = Number(row.id);
    const fullName = typeof row.fullName === "string" ? row.fullName : "";
    if (!Number.isFinite(id) || !fullName) return [];
    return [{
      id,
      fullName,
      phone: typeof row.phone === "string" ? row.phone : "",
      cnic: typeof row.cnic === "string" && row.cnic ? row.cnic : null,
    }];
  });
}

/** What the search may ask the server: nothing for a box that is empty or holds a single character. */
export const searchIsUsable = (text: string): boolean => text.trim().length >= 2;
