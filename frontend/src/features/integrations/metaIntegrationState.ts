import type { MetaConnection, MetaConnectionStatus, MetaResource } from "../leads/types.ts";

/**
 * The presentation logic behind the Integrations panel, kept as pure functions.
 *
 * This project has no component-rendering test setup, so anything worth asserting lives
 * here rather than inside the component, where it would be untestable.
 */

export type CallbackResult =
  | { kind: "none" }
  | { kind: "connected" }
  | { kind: "error"; message: string };

/** Fixed vocabulary the API is allowed to send back. Anything else is treated generically. */
const CALLBACK_REASONS: Record<string, string> = {
  denied: "The Meta authorization was cancelled before it finished.",
  invalid_state: "That connection link was already used or has expired. Start again from Connect Meta.",
  expired_state: "That connection link expired. Start again from Connect Meta.",
  exchange_failed: "Meta accepted the sign-in but DAMS could not complete the connection. Try again.",
  not_configured: "The Meta integration is not configured on this server yet.",
};

/**
 * Reads the outcome Meta's callback redirected back with. The URL only ever carries a
 * success flag or one of the fixed reasons above — never a token or a provider message.
 */
export function readCallbackResult(search: string): CallbackResult {
  const params = new URLSearchParams(search);
  const meta = params.get("meta");

  if (meta === "connected") return { kind: "connected" };
  if (meta !== "error") return { kind: "none" };

  const reason = params.get("reason") ?? "";
  return {
    kind: "error",
    message: CALLBACK_REASONS[reason] ?? "The Meta connection could not be completed.",
  };
}

export function connectionStatusLabel(status: MetaConnectionStatus): string {
  switch (status) {
    case "Connected":
      return "Connected";
    case "NeedsReauthorization":
      return "Needs reconnection";
    case "Disconnected":
      return "Disconnected";
    default:
      return "Error";
  }
}

/** Tailwind classes per status. Anything not healthy is visually distinct at a glance. */
export function connectionStatusTone(status: MetaConnectionStatus): string {
  switch (status) {
    case "Connected":
      return "border-emerald-500/30 bg-emerald-500/10 text-emerald-300";
    case "NeedsReauthorization":
      return "border-amber-500/30 bg-amber-500/10 text-amber-200";
    case "Disconnected":
      return "border-[var(--border)] bg-[var(--bg-muted)] text-[var(--text-muted)]";
    default:
      return "border-red-500/30 bg-red-500/10 text-red-300";
  }
}

export function resourceTypeLabel(resourceType: string): string {
  switch (resourceType) {
    case "facebook_page":
      return "Facebook Page";
    case "instagram_account":
      return "Instagram account";
    case "ad_account":
      return "Ad account";
    case "campaign":
      return "Campaign";
    case "ad_set":
      return "Ad set";
    case "ad":
      return "Ad";
    case "lead_form":
      return "Lead form";
    default:
      return resourceType;
  }
}

/**
 * Only a Facebook Page can be toggled. Everything else is discovered for attribution and
 * reporting; enabling an ad or a campaign would imply a control DAMS does not have.
 */
export function isToggleable(resource: MetaResource): boolean {
  return resource.resourceType === "facebook_page";
}

export function canSync(connection: MetaConnection): boolean {
  return connection.status === "Connected" || connection.status === "Error";
}

/** True while a freshly connected account has not had its first discovery run yet. */
export function isAwaitingFirstSync(connection: MetaConnection): boolean {
  return connection.status === "Connected" && !connection.lastSyncedAt;
}

export function summarizeCounts(connection: MetaConnection): string {
  const parts: string[] = [];
  if (connection.pageCount > 0) parts.push(plural(connection.pageCount, "Page", "Pages"));
  if (connection.instagramCount > 0) parts.push(plural(connection.instagramCount, "Instagram account", "Instagram accounts"));
  if (connection.adAccountCount > 0) parts.push(plural(connection.adAccountCount, "ad account", "ad accounts"));
  if (connection.leadFormCount > 0) parts.push(plural(connection.leadFormCount, "lead form", "lead forms"));

  return parts.length === 0 ? "Nothing discovered yet" : parts.join(" · ");
}

function plural(count: number, singular: string, plural: string): string {
  return `${count} ${count === 1 ? singular : plural}`;
}

/**
 * Explains why lead delivery is or is not happening. A connection with pages but none
 * enabled looks healthy while quietly ingesting nothing, which is the confusing case worth
 * calling out explicitly.
 */
export function deliverySummary(connection: MetaConnection): string {
  if (connection.status === "Disconnected") return "Not receiving leads.";
  if (connection.status === "NeedsReauthorization") return "Paused until this account is reconnected.";
  if (connection.pageCount === 0) return "No Pages discovered yet.";
  if (connection.enabledResourceCount === 0) return "No Pages enabled, so no leads are being received.";

  return `Receiving leads from ${plural(connection.enabledResourceCount, "Page", "Pages")}.`;
}
