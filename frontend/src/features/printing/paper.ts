import { formatDay } from "../../lib/dates.ts";
import { formatPkr } from "../../utils/currency.ts";

/** Which of the three boxes under "Mode of payment" is ticked: Cash, Cheque / Pay Order, Bank / Online Transfer. */
export function modeOfPayment(method: string): "Cash" | "Cheque" | "Transfer" {
  switch (method) {
    case "Cash":
      return "Cash";
    case "Cheque":
      return "Cheque";
    default:
      return "Transfer"; // BankTransfer | Online
  }
}

/** What the "Through" line on the application form says for the real payment method. */
export function paymentMethodLabel(method: string | null | undefined): string {
  switch (method) {
    case "Cash":
      return "Cash";
    case "Cheque":
      return "Cheque";
    case "BankTransfer":
      return "Bank transfer";
    case "Online":
      return "Online";
    default:
      return "";
  }
}

/** "Installment 4 · Rs 918,000 · Aug 26, 2026" — what the receipt page says under its title. */
export function receiptSubtitle(receipt: {
  type: string;
  installmentSequence?: number | null;
  installmentType?: string | null;
  amount: number;
  paidAt: string;
}): string {
  const what =
    receipt.type === "BookingAmount"
      ? "Booking amount"
      : receipt.installmentType === "Possession"
        ? "Possession payment"
        : receipt.installmentSequence != null
          ? `Installment ${receipt.installmentSequence}`
          : "Installment";
  return `${what} · ${formatPkr(receipt.amount)} · ${formatDay(receipt.paidAt)}`;
}
