import { apiJson, jsonRequest } from "../leads/leadApi.ts";
import type {
  ImportMetaLeadsRequest,
  LeadFormMapping,
  MetaLeadImportResult,
  MetaConnectStart,
  MetaConnection,
  MetaEvent,
  MetaResource,
  MetaResourceGroup,
  MetaSyncResult,
  SaveLeadFormMapping,
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

export function listMetaEvents(connectionId: number, take = 25, status?: MetaEvent["status"]) {
  const query = new URLSearchParams({ take: String(take) });
  if (status) query.set("status", status);
  return apiJson<MetaEvent[]>(`${base}/connections/${connectionId}/events?${query}`);
}

export function retryMetaEvent(connectionId: number, eventId: number) {
  return apiJson<MetaEvent>(
    `${base}/connections/${connectionId}/events/${eventId}/retry`,
    jsonRequest("POST", {}),
  );
}

export function disconnectMetaConnection(connectionId: number) {
  return apiJson<void>(`${base}/connections/${connectionId}/disconnect`, jsonRequest("POST", {}));
}

/** Keyed by the form's Meta id, not a connection, so the mapping survives a reconnect. */
export function getLeadFormMapping(formExternalId: string) {
  return apiJson<LeadFormMapping>(`${base}/lead-forms/${encodeURIComponent(formExternalId)}/mapping`);
}

export function saveLeadFormMapping(formExternalId: string, mapping: SaveLeadFormMapping) {
  return apiJson<LeadFormMapping>(
    `${base}/lead-forms/${encodeURIComponent(formExternalId)}/mapping`,
    jsonRequest("PUT", mapping),
  );
}

/** Recovers leads the webhook missed. Safe to repeat: leads already in DAMS are only counted. */
export function importMetaLeads(connectionId: number, request: ImportMetaLeadsRequest) {
  return apiJson<MetaLeadImportResult>(`${base}/connections/${connectionId}/import`, jsonRequest("POST", request));
}
