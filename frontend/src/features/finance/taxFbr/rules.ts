import { formatPickerDate, parseIso } from "../../../components/ui/pickerFormat.ts";
import { countOf } from "../lists.ts";
import { formatRs, type WhtDeposit, type WhtVendorLine } from "../whtTypes.ts";

/** "1 supplier", "6 suppliers": the period line under the cards. */
export const suppliersText = (count: number) => countOf(count, "supplier", "suppliers");

const day = (value: string) => formatPickerDate(value.slice(0, 10));

/** "Sep 15, 2026" from the server's "2026-09-15T00:00:00". */
export const depositDate = (value: string) => day(value) || value.slice(0, 10);

/**
 * "Aug 1 – Aug 31, 2026" when both ends are known, "Dec 1, 2025 – Jan 31, 2026" across a year; "—"
 * when either is missing, because half a period says nothing about what the challan covered.
 */
export function periodCovered(deposit: Pick<WhtDeposit, "periodFrom" | "periodTo">): string {
  if (!deposit.periodFrom || !deposit.periodTo) return "—";
  const from = parseIso(deposit.periodFrom.slice(0, 10));
  const to = parseIso(deposit.periodTo.slice(0, 10));
  if (!from || !to) return "—";
  const start = formatPickerDate(deposit.periodFrom.slice(0, 10));
  const end = formatPickerDate(deposit.periodTo.slice(0, 10));
  return from.year === to.year ? `${start.replace(/, \d{4}$/, "")} – ${end}` : `${start} – ${end}`;
}

/** "Delete deposit CPR-0923-118?", or "Delete this deposit?" when it has no challan number. */
export const deleteTitle = (deposit: Pick<WhtDeposit, "challanNumber">) =>
  deposit.challanNumber ? `Delete deposit ${deposit.challanNumber}?` : "Delete this deposit?";

export const deleteMessage = (deposit: Pick<WhtDeposit, "amount">) => `${formatRs(deposit.amount)} goes back to being owed to FBR.`;

/** The tax number the supplier is known by: NTN, else CNIC, else a dash. */
export const supplierTaxId = (line: Pick<WhtVendorLine, "ntn" | "cnic">) => line.ntn || line.cnic || "—";

/** A supplier can have more than one row (a section or filer status that changed), so the key says which. */
export const supplierKey = (line: WhtVendorLine, index: number) =>
  `${line.vendorId ?? `text:${line.vendorName}`}|${line.taxSection ?? "none"}|${line.filerStatus}|${index}`;

export type DepositFields = {
  accountId: string;
  amount: string;
  date: string;
  challan: string;
  periodFrom: string;
  periodTo: string;
  notes: string;
};

/** A new deposit opens on what is owed (when anything is) and today; an edit opens on what was saved. */
export function depositFields(deposit: WhtDeposit | null, owed: number, today: string): DepositFields {
  if (deposit) {
    return {
      accountId: String(deposit.financeAccountId),
      amount: String(deposit.amount),
      date: deposit.depositDate.slice(0, 10),
      challan: deposit.challanNumber ?? "",
      periodFrom: deposit.periodFrom?.slice(0, 10) ?? "",
      periodTo: deposit.periodTo?.slice(0, 10) ?? "",
      notes: deposit.notes ?? "",
    };
  }
  return { accountId: "", amount: owed > 0 ? String(owed) : "", date: today, challan: "", periodFrom: "", periodTo: "", notes: "" };
}

export type DepositErrors = { date?: string; periodTo?: string };

/** The two dates that can be wrong on their own; the server still has the last word on the rest. */
export function depositErrors(fields: Pick<DepositFields, "date" | "periodFrom" | "periodTo">, today: string): DepositErrors {
  return {
    date: fields.date > today ? "The date cannot be in the future." : undefined,
    periodTo: fields.periodFrom && fields.periodTo && fields.periodTo < fields.periodFrom ? "The period end cannot be before its start." : undefined,
  };
}

/** Save stays off until the account, a positive amount and the date are there and the dates make sense. */
export function depositReady(fields: DepositFields, today: string): boolean {
  const amount = Number(fields.amount);
  const errors = depositErrors(fields, today);
  return fields.accountId !== "" && fields.amount !== "" && Number.isFinite(amount) && amount > 0 && fields.date !== "" && !errors.date && !errors.periodTo;
}

/** What is sent. Blank optional fields go as null, not as empty strings the server would store. */
export function depositBody(fields: DepositFields, concurrencyToken: string | null) {
  const blankToNull = (value: string) => value.trim() || null;
  return {
    financeAccountId: Number(fields.accountId),
    amount: Number(fields.amount),
    depositDate: fields.date,
    challanNumber: blankToNull(fields.challan),
    periodFrom: fields.periodFrom || null,
    periodTo: fields.periodTo || null,
    notes: blankToNull(fields.notes),
    concurrencyToken,
  };
}

/** "s.153(1)(a)" on a phone card, where the column heading is not there to say what the number is. */
export const sectionLabel = (section: string | null) => (section ? (/^s\./i.test(section) ? section : `s.${section}`) : "—");
