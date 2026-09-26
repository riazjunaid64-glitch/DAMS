export type {
  ExternalFieldAnswer,
  ExternalSubmission,
  MetaConnection,
  MetaConnectionStatus,
  MetaResource,
  MetaResourceGroup,
  MetaSyncResult,
} from "../leads/types.ts";

export interface MetaConnectStart {
  authorizationUrl: string;
  expiresAt: string;
}

export type MetaEventStatus = "Pending" | "Processing" | "Processed" | "Retry" | "Failed" | "Ignored";

export interface MetaEvent {
  id: number;
  eventType: string;
  status: MetaEventStatus;
  attempts: number;
  receivedAt: string;
  processedAt?: string | null;
  leadId?: number | null;
  resourceName?: string | null;
  lastError?: string | null;
  retryCount: number;
  lastRetriedAt?: string | null;
  lastRetriedByName?: string | null;
}

/** The lead fields a lead form's answers can fill. */
export type LeadFormAnswerTarget = "PropertyType" | "PurchaseIntent" | "PaymentPreference";

export interface LeadFormOption {
  key: string;
  /** The option's text as the person saw it. */
  value?: string | null;
}

export interface LeadFormQuestion {
  key: string;
  label?: string | null;
  type?: string | null;
  options: LeadFormOption[];
}

export interface LeadFormOptionMapping {
  optionKey: string;
  optionLabel?: string | null;
  value: string;
}

export interface LeadFormAnswerMapping {
  questionKey: string;
  target: LeadFormAnswerTarget;
  options: LeadFormOptionMapping[];
}

export interface LeadFormMapping {
  formExternalId: string;
  formName?: string | null;
  /** Empty until a sync has read the form's questions. */
  questions: LeadFormQuestion[];
  interestedProjectId?: number | null;
  interestedProjectName?: string | null;
  answers: LeadFormAnswerMapping[];
  version?: string | null;
  updatedAt?: string | null;
}

export interface SaveLeadFormMapping {
  interestedProjectId: number | null;
  answers: LeadFormAnswerMapping[];
  version: string | null;
}

/** An admin's "import leads since…": one Page (every synced form) or one form. */
export interface ImportMetaLeadsRequest {
  resourceId?: number | null;
  formExternalId?: string | null;
  /** A date (YYYY-MM-DD), at most 90 days back. */
  since: string;
}

export interface MetaLeadImportResult {
  found: number;
  new: number;
  alreadyInDams: number;
  failed: number;
  warning?: string | null;
}
