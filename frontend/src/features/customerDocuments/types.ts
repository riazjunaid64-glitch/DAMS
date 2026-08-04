export type DocumentStatus =
  | "Missing" | "Requested" | "Received" | "UnderReview" | "Approved"
  | "Rejected" | "ReplacementRequired" | "Postponed" | "Waived"
  | "NotApplicable" | "Expired";

export type AssignmentMode = "None" | "NewCustomersOnly" | "AllActiveCustomers" | "SelectedCustomers";

export interface DocumentSummary {
  requiredTotal: number;
  completedRequired: number;
  missing: number;
  awaitingReview: number;
  replacementRequired: number;
  postponed: number;
  postponedDue: number;
  isComplete: boolean;
  completionPercent: number;
  label: string;
}

export interface DocumentVersion {
  id: number;
  versionNumber: number;
  isCurrent: boolean;
  originalFileName: string;
  contentType: string;
  fileSize: number;
  uploadedByName?: string | null;
  uploadedAt: string;
  reviewStatus: string;
  reviewedByName?: string | null;
  reviewedAt?: string | null;
  reviewReason?: string | null;
}

export interface DocumentRequirement {
  id: number;
  categoryId?: number | null;
  categoryCode?: string | null;
  categoryIsActive: boolean;
  name: string;
  description?: string | null;
  isRequired: boolean;
  displayOrder: number;
  allowedFileTypes: string[];
  maxFileSizeBytes: number;
  status: DocumentStatus;
  dueDate?: string | null;
  postponedUntil?: string | null;
  lastActionByName?: string | null;
  updatedAt: string;
  concurrencyToken: string;
  latestVersion?: DocumentVersion | null;
  versions: DocumentVersion[];
}

export interface DocumentAudit {
  id: number;
  requirementId?: number | null;
  versionId?: number | null;
  documentName?: string | null;
  action: string;
  previousStatus?: DocumentStatus | null;
  newStatus?: DocumentStatus | null;
  notes?: string | null;
  performedByName?: string | null;
  occurredAt: string;
}

export interface DocumentChecklist {
  customerId: number;
  customerName: string;
  summary: DocumentSummary;
  requirements: DocumentRequirement[];
  history: DocumentAudit[];
}

export interface DocumentCategory {
  id: number;
  name: string;
  code: string;
  description?: string | null;
  isRequiredByDefault: boolean;
  displayOrder: number;
  allowedFileTypes: string[];
  maxFileSizeBytes: number;
  isActive: boolean;
  assignToNewCustomers: boolean;
  defaultDueDays?: number | null;
  usageCount: number;
  createdAt: string;
  updatedAt?: string | null;
  concurrencyToken: string;
}
