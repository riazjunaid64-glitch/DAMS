export type CalculationType = "Percentage" | "FixedAmount";
export type CalculationBasis = "AgreedSalePrice" | "NetSalePriceAfterDiscount" | "BookingAmountReceived" | "AmountActuallyCollected" | "ManuallyApprovedAmount";
export type CommissionStatus = "Draft" | "PendingApproval" | "Approved" | "Earned" | "Payable" | "PartiallyPaid" | "Paid" | "Rejected" | "Cancelled" | "ReversalRequired" | "Reversed";
export type RebateStatus = "Draft" | "PendingApproval" | "Approved" | "PartiallyApplied" | "Applied" | "Paid" | "Rejected" | "Cancelled" | "Reversed" | "ReversalRequired";
export type RebateMethod = "OutstandingBalanceReduction" | "InstallmentAdjustment" | "CashOrBankPayment" | "CreditNote" | "Other";
export type EarningCondition = "ManualMilestone" | "BookingAmountFullyReceived" | "MinimumCollectionPercentage" | "FirstInstallmentReceived" | "SaleCompleted";

export interface PagedResult<T> { items:T[]; hasMore:boolean; }

export interface CommissionRebateSummary {
  accruedCommission: number; payableCommission: number; commissionPaid: number;
  commissionReversalRequired: number; approvedRebates: number; rebatesAppliedOrPaid: number;
  rebateReversalRequired: number; activePartners: number; pendingRecords: number;
}

export interface Partner {
  id:number; name:string; partnerType:string; contactPerson:string|null; phone:string|null; email:string|null;
  address:string|null; cnic:string|null; ntn:string|null; registrationNumber:string|null; internalCode:string;
  bankName:string|null; accountTitle:string|null; accountNumber:string|null; iban:string|null; notes:string|null;
  isActive:boolean; attributionCount:number; commissionCount:number; createdAt:string; updatedAt:string|null; concurrencyToken:string;
}

export interface Attribution {
  id:number; partnerId:number; partnerName:string; leadId:number|null; customerId:number|null; bookingId:number|null;
  relationshipType:string; introducedAt:string|null; sourceDetails:string|null; notes:string|null;
  isPrimary:boolean; allocationPercent:number; assignedAt:string; concurrencyToken:string;
}

export interface CommissionRule {
  id:number; name:string; description:string|null; isActive:boolean; effectiveFrom:string; effectiveTo:string|null;
  partnerId:number|null; partnerName:string|null; partnerType:string|null; projectId:number|null; projectName:string|null;
  unitCategory:string|null; bookingSource:string|null; bookingId:number|null; calculationType:CalculationType;
  percentageRate:number|null; fixedAmount:number|null; calculationBasis:CalculationBasis; minimumCommission:number|null;
  maximumCommission:number|null; eligibilityCondition:string|null; earningCondition:EarningCondition;
  minimumCollectionPercent:number|null; priority:number; requiresApproval:boolean; notes:string|null; concurrencyToken:string;
  currentRevisionNumber:number;
}

export interface MoneyMovement {
  id:number; financeAccountId:number|null; financeAccountName:string|null; installmentId:number|null;
  rebateMethod:RebateMethod|null; amount:number; reversedAmount:number; date:string; paymentMethod:string|null;
  reference:string|null; notes:string|null; concurrencyToken:string;
  evidence:Evidence[];
}

export interface Evidence { id:number; originalFileName:string; contentType:string; fileSize:number; uploadedByName:string|null; uploadedAt:string }
export interface AuditEntry {
  id:number; action:string; previousCommissionStatus:CommissionStatus|null; newCommissionStatus:CommissionStatus|null;
  previousRebateStatus:RebateStatus|null; newRebateStatus:RebateStatus|null; previousAmount:number|null;
  newAmount:number|null; reason:string|null; performedByName:string|null; occurredAt:string;
}

export interface Commission {
  id:number; bookingId:number; bookingReference:string; partnerId:number; partnerName:string; attributionId:number|null;
  ruleId:number|null; ruleRevisionId:number|null; ruleRevisionNumber:number|null; ruleNameSnapshot:string|null; rulePriority:number|null; isManual:boolean; manualReason:string|null;
  allocationPercent:number;
  calculationType:CalculationType; percentageRate:number|null; fixedAmount:number|null; calculationBasis:CalculationBasis;
  basisAmount:number; calculatedAmount:number; adjustmentAmount:number; adjustmentReason:string|null; finalAmount:number;
  approvedAmount:number|null; paidAmount:number; outstandingAmount:number; recoveryRequiredAmount:number; earningCondition:EarningCondition;
  minimumCollectionPercent:number|null; status:CommissionStatus; createdAt:string; submittedByName:string|null;
  submittedAt:string|null; decisionByName:string|null; decisionAt:string|null; decisionReason:string|null;
  earnedAt:string|null; payableAt:string|null; cancellationOrReversalReason:string|null; payouts:MoneyMovement[];
  evidence:Evidence[]; concurrencyToken:string;
}

export interface Rebate {
  id:number; bookingId:number; bookingReference:string; customerId:number; customerName:string; calculationType:CalculationType;
  percentageRate:number|null; fixedAmount:number|null; calculationBasis:CalculationBasis; basisAmount:number;
  calculatedAmount:number; adjustmentAmount:number; adjustmentReason:string|null; finalAmount:number; approvedAmount:number|null;
  appliedOrPaidAmount:number; outstandingAmount:number; recoveryRequiredAmount:number; reason:string; method:RebateMethod; status:RebateStatus;
  notes:string|null; createdAt:string; submittedByName:string|null; submittedAt:string|null; decisionByName:string|null;
  decisionAt:string|null; decisionReason:string|null; cancellationOrReversalReason:string|null;
  disbursements:MoneyMovement[]; evidence:Evidence[]; concurrencyToken:string;
}

export interface BookingWorkspace {
  bookingId:number; bookingReference:string; customerName:string; projectName:string; unitNumber:string; bookingStatus:string;
  agreedSalePrice:number; netSalePrice:number; amountCollected:number; rebateCredits:number;
  attributions:Attribution[]; commissions:Commission[]; rebates:Rebate[]; audit:AuditEntry[]; hasMoreAudit:boolean;
}

export interface FinanceAccountOption { id:number; name:string; accountHolderName:string; isActive:boolean }
export interface InstallmentOption { id:number; sequenceNumber:number; type:string; remainingBalance:number; status:string }
