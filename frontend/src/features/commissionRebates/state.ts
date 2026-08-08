import type { CommissionStatus, RebateStatus } from "./types";

export const commissionActions = (status:CommissionStatus) => ({
  canSubmit: status === "Draft", canApprove: status === "PendingApproval", canReject: status === "PendingApproval",
  canReturn: status === "PendingApproval", canEarn: status === "Approved", canMakePayable: status === "Earned",
  canPay: status === "Payable" || status === "PartiallyPaid",
  canCancel: ["Draft","Approved","Earned","Payable","Rejected"].includes(status),
  canReverse: status === "Paid" || status === "PartiallyPaid" || status === "ReversalRequired",
});

export const rebateActions = (status:RebateStatus) => ({
  canSubmit: status === "Draft", canApprove: status === "PendingApproval", canReject: status === "PendingApproval",
  canReturn: status === "PendingApproval", canDisburse: status === "Approved" || status === "PartiallyApplied",
  canCancel: ["Draft","Approved","Rejected"].includes(status),
  canReverse: status === "Applied" || status === "Paid" || status === "PartiallyApplied" || status === "ReversalRequired",
});

export const prettyEnum = (value:string) => value.replace(/([a-z])([A-Z])/g,"$1 $2");
export const money = (value:number) => `Rs ${value.toLocaleString("en-PK",{minimumFractionDigits:2,maximumFractionDigits:2})}`;

export function idempotencyKey(prefix:string) {
  return `${prefix}-${globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`}`;
}
