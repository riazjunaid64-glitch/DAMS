import { formatDay } from "../../lib/dates.ts";
import type { StatusTone } from "../../components/ui";
import type { CustomerDetail } from "./customerForm.ts";

export const isBlocked = (customer: Pick<CustomerDetail, "status">): boolean => customer.status === "Blocked";

/** The badge beside the name: Blocked replaces the document badges, then "N documents needed" or "Documents complete". */
export function customerBadge(customer: Pick<CustomerDetail, "status" | "documentsNeeded">): { text: string; tone: StatusTone } {
  if (isBlocked(customer)) return { text: "Blocked", tone: "red" };
  const needed = customer.documentsNeeded;
  if (needed > 0) return { text: `${needed} ${needed === 1 ? "document" : "documents"} needed`, tone: "orange" };
  return { text: "Documents complete", tone: "green" };
}

/**
 * The header buttons. A blocked customer can be edited and unblocked and cannot get a new booking;
 * on a phone that leaves one extra action, so the "⋯" menu is dropped for the Edit pencil.
 */
export function headerActions(customer: Pick<CustomerDetail, "status">) {
  const blocked = isBlocked(customer);
  return { edit: true, block: !blocked, unblock: blocked, newBooking: !blocked, phoneMenu: !blocked };
}

/** "Cheque bounced twice · Blocked by admin on Sep 30, 2026". */
export function blockedLine(customer: Pick<CustomerDetail, "blockedReason" | "blockedByName" | "blockedAt">): string {
  const by = customer.blockedByName ? ` by ${customer.blockedByName}` : "";
  const on = customer.blockedAt ? ` on ${formatDay(customer.blockedAt)}` : "";
  const reason = customer.blockedReason?.trim();
  return `${reason ? `${reason} · ` : ""}Blocked${by}${on}`;
}
