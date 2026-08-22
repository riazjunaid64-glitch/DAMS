import { apiJson, jsonRequest } from "../leads/leadApi.ts";
import type {
  MetaConnectStart,
  MetaConnection,
  MetaResource,
  MetaResourceGroup,
  MetaSyncResult,
} from "./types.ts";

const base = "/api/integrations/meta";

/** Meta's consent URL. Returned as data because the browser must navigate to it itself. */
export function startMetaConnect(returnPath?: string) {
  return apiJson<MetaConnectStart>(`${base}/connect`, jsonRequest("POST", { returnPath: returnPath ?? null }));
}

export function listMetaConnections() {
  return apiJson<MetaConnection[]>(`${base}/connections`);
}

export function listMetaResources(connectionId: number) {
  return apiJson<MetaResourceGroup[]>(`${base}/connections/${connectionId}/resources`);
}

export function setMetaResourceEnabled(connectionId: number, resourceId: number, isEnabled: boolean) {
  return apiJson<MetaResource>(
    `${base}/connections/${connectionId}/resources/${resourceId}`,
    jsonRequest("PATCH", { isEnabled }),
  );
}

export function syncMetaConnection(connectionId: number) {
  return apiJson<MetaSyncResult>(`${base}/connections/${connectionId}/sync`, jsonRequest("POST", {}));
}

export function disconnectMetaConnection(connectionId: number) {
  return apiJson<void>(`${base}/connections/${connectionId}/disconnect`, jsonRequest("POST", {}));
}
