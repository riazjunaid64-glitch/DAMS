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
