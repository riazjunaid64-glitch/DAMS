export type DocumentStatus = "Needed" | "Uploaded" | "NotNeeded";

export interface DocumentVersion {
  id: number;
  versionNumber: number;
  isCurrent: boolean;
  originalFileName: string;
  contentType: string;
  fileSize: number;
  uploadedByName?: string | null;
  uploadedAt: string;
}

export interface DocumentRequirement {
  id: number;
  categoryId?: number | null;
  name: string;
  description?: string | null;
  isRequired: boolean;
  displayOrder: number;
  status: DocumentStatus;
  notNeededReason?: string | null;
  notNeededByName?: string | null;
  notNeededAt?: string | null;
  lastActionByName?: string | null;
  updatedAt: string;
  concurrencyToken: string;
  latestVersion?: DocumentVersion | null;
  /** Newest first, the current file included. */
  versions: DocumentVersion[];
  hasMoreVersions: boolean;
}

export interface DocumentTypeOption {
  categoryId: number;
  name: string;
}

export interface DocumentChecklist {
  customerId: number;
  customerName: string;
  /** Asked from this customer and no file yet — the number in the page header. */
  stillNeeded: number;
  /** Uploaded or not needed. */
  done: number;
  requirements: DocumentRequirement[];
  /** What the Add document popup offers, besides Other. */
  availableTypes: DocumentTypeOption[];
}

export interface DocumentSetupItem {
  id: number;
  name: string;
  asksEveryCustomer: boolean;
}

export interface DocumentSetupList {
  nonBlockedCustomerCount: number;
  documents: DocumentSetupItem[];
}
