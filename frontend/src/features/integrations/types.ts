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
