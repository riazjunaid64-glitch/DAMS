import { formatPickerDate } from "../../../components/ui/pickerFormat.ts";
import type { CashAccount, Loan, LoanAccount, LoanTransaction, MovementType } from "./types.ts";

/** "5,000,000": table figures carry their unit in the column heading. Decimals only when there are some. */
export const figure = (value: number) => value.toLocaleString("en-PK", { maximumFractionDigits: 2 });

/** "Rs 3,000,000", keeping a minus if a balance ever comes back negative. */
export const rupees = (value: number) => `${value < 0 ? "−" : ""}Rs ${figure(Math.abs(value))}`;

/** "Sep 5, 2026" from the server's "2026-09-05T00:00:00". */
export const movementDate = (value: string) => formatPickerDate(value.slice(0, 10)) || value.slice(0, 10);

/** A drawdown brings cash in and adds to what is owed; a repayment does the opposite. */
export const moneyIn = (row: Pick<LoanTransaction, "type">) => row.type === "Drawdown";

/** The principal and cash columns are signed by the movement; the API sends both unsigned. */
export const signedPrincipal = (row: LoanTransaction) => (moneyIn(row) ? 1 : -1) * row.principalAmount;
export const signedCash = (row: LoanTransaction) => (moneyIn(row) ? 1 : -1) * row.totalCashMovement;

/** "−400,000" or "5,000,000"; null for zero, so the cell shows a dash. */
export function signedFigure(value: number): string | null {
  if (value === 0) return null;
  return `${value < 0 ? "−" : ""}${figure(Math.abs(value))}`;
}

export const movementTitle = (type: MovementType) => (type === "Drawdown" ? "Funds received" : "Repayment");

/** What a repayment was made of, so the row says it rather than assuming both halves. */
export function movementCaption(row: Pick<LoanTransaction, "type" | "principalAmount" | "interestAmount">): string {
  if (row.type === "Drawdown") return "Drawdown";
  if (row.principalAmount > 0 && row.interestAmount > 0) return "Principal + interest";
  return row.interestAmount > 0 ? "Interest only" : "Principal only";
}

/** "Drawdown · BAL-DD-01": the caption, then the reference when there is one. */
export const movementDetail = (row: LoanTransaction) => [movementCaption(row), row.reference].filter(Boolean).join(" · ");

/** "Lender: Bank Alfalah · Account: Bank Alfalah loan", naming the lender only when there is one. */
export function loanLine(loan: Loan, accountWord = "Account"): string {
  return [loan.lenderName ? `Lender: ${loan.lenderName}` : null, `${accountWord}: ${loan.financeAccountName}`].filter(Boolean).join(" · ");
}

export const loanStatus = (loan: Pick<Loan, "isActive">) => (loan.isActive ? "Active" : "Closed");

export type StatusFilter = "all" | "active" | "closed";

/** Search and status narrow the list only; they never change which loan is open. */
export function filterLoans(loans: readonly Loan[], search: string, status: StatusFilter): Loan[] {
  const term = search.trim().toLowerCase();
  return loans.filter((loan) =>
    (status === "all" || (status === "active") === loan.isActive) &&
    (!term ||
      loan.name.toLowerCase().includes(term) ||
      (loan.lenderName ?? "").toLowerCase().includes(term) ||
      loan.financeAccountName.toLowerCase().includes(term)));
}

const accountLabel = (account: { name: string; accountHolderName: string; isActive: boolean }) =>
  `${account.name} · ${account.accountHolderName}${account.isActive ? "" : " (Inactive)"}`;

/**
 * The Liability accounts a loan can use: active ones no other loan holds, plus the account the loan
 * already has even if it was deactivated since, so an edit never silently drops it.
 */
export function loanAccountChoices(accounts: readonly LoanAccount[], loanId: number | null, currentId: string) {
  return accounts
    .filter((account) => (account.isActive || String(account.id) === currentId) && (account.linkedLoanId === null || account.linkedLoanId === loanId))
    .map((account) => ({ value: String(account.id), label: accountLabel(account) }));
}

/** Active cash and bank accounts, plus the one a correction already names. */
export function cashAccountChoices(accounts: readonly CashAccount[], currentId: string) {
  return accounts
    .filter((account) => account.isActive || String(account.id) === currentId)
    .map((account) => ({ value: String(account.id), label: accountLabel(account) }));
}

/** Makes Excel read the file as UTF-8 rather than the local code page. */
const BOM = String.fromCharCode(0xfeff);

/** A cell a spreadsheet cannot read as a formula: typed text starting with =, +, - or @ is prefixed. */
function csvText(value: string): string {
  const guarded = /^[=+\-@]/.test(value) ? `'${value}` : value;
  return `"${guarded.replace(/"/g, '""')}"`;
}

/** The loan's activity as CSV, with the same figures and signs as the screen. */
export function activityCsv(rows: readonly LoanTransaction[]): string {
  const header = ["Date", "Transaction", "Detail", "Account", "Reference", "Note", "Principal (Rs)", "Interest (Rs)", "Cash impact (Rs)", "Outstanding principal (Rs)"];
  const lines = rows.map((row) => [
    csvText(row.date.slice(0, 10)),
    csvText(movementTitle(row.type)),
    csvText(movementCaption(row)),
    csvText(row.financeAccountName),
    csvText(row.reference ?? ""),
    csvText(row.note ?? ""),
    signedPrincipal(row).toFixed(2),
    row.interestAmount.toFixed(2),
    signedCash(row).toFixed(2),
    row.runningBalance.toFixed(2),
  ].join(","));
  return `${BOM}${[header.map(csvText).join(","), ...lines].join("\r\n")}\r\n`;
}

/** "bank-alfalah-loan-activity.csv". */
export const activityFileName = (loanName: string) =>
  `${loanName.replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "").toLowerCase() || "loan"}-activity.csv`;

/** Hands the browser a CSV to save. */
export function saveCsv(csv: string, fileName: string) {
  const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
