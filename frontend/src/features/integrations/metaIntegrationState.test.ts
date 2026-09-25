import { describe, expect, it } from "vitest";
import type { MetaConnection, MetaResource } from "../leads/types.ts";
import {
  canSync,
  connectionStatusLabel,
  createLatestRequestGuard,
  emptyEventsMessage,
  eventListLimit,
  eventListLimitNote,
  deliverySummary,
  isAwaitingFirstSync,
  isToggleable,
  readCallbackResult,
  resourceTypeLabel,
  summarizeCounts,
} from "./metaIntegrationState.ts";

const connection = (overrides: Partial<MetaConnection> = {}): MetaConnection => ({
  id: 1,
  provider: "meta",
  displayName: "Acme Marketing",
  status: "Connected",
  connectedAt: "2026-08-16T10:00:00Z",
  grantedScopes: [],
  pageCount: 0,
  instagramCount: 0,
  adAccountCount: 0,
  leadFormCount: 0,
  enabledResourceCount: 0,
  pendingEventCount: 0,
  failedEventCount: 0,
  ...overrides,
});

const resource = (overrides: Partial<MetaResource> = {}): MetaResource => ({
  id: 1,
  resourceType: "facebook_page",
  externalId: "111",
  isEnabled: false,
  isActive: true,
  isSubscribed: false,
  ...overrides,
});

describe("readCallbackResult", () => {
  it("reports nothing when the URL carries no outcome", () => {
    expect(readCallbackResult("?tab=integrations")).toEqual({ kind: "none" });
  });

  it("recognises a successful connection", () => {
    expect(readCallbackResult("?meta=connected")).toEqual({ kind: "connected" });
  });

  it("explains each known failure reason in plain language", () => {
    const result = readCallbackResult("?meta=error&reason=invalid_state");
    expect(result.kind).toBe("error");
    expect(result.kind === "error" && result.message).toContain("already used or has expired");
  });

  it("falls back to a generic message for an unrecognised reason", () => {
    const result = readCallbackResult("?meta=error&reason=something_new");
    expect(result.kind === "error" && result.message).toBe("The Meta connection could not be completed.");
  });

  it("never surfaces a raw value from the URL", () => {
    // The API only ever sends fixed reason tokens, so anything else must not be echoed to
    // the user — that is what would turn a crafted link into a phishing message.
    const result = readCallbackResult("?meta=error&reason=" + encodeURIComponent("Your token EAAB123 expired"));
    expect(result.kind === "error" && result.message).toBe("The Meta connection could not be completed.");
  });
});

describe("connection presentation", () => {
  it("labels a connection needing reauthorization in words an admin can act on", () => {
    expect(connectionStatusLabel("NeedsReauthorization")).toBe("Needs reconnection");
  });

  it("allows syncing only when the connection could actually talk to Meta", () => {
    expect(canSync(connection({ status: "Connected" }))).toBe(true);
    expect(canSync(connection({ status: "Error" }))).toBe(true);
    expect(canSync(connection({ status: "NeedsReauthorization" }))).toBe(false);
    expect(canSync(connection({ status: "Disconnected" }))).toBe(false);
  });

  it("treats a never-synced connection as still discovering", () => {
    expect(isAwaitingFirstSync(connection())).toBe(true);
    expect(isAwaitingFirstSync(connection({ lastSyncedAt: "2026-08-16T10:05:00Z" }))).toBe(false);
    expect(isAwaitingFirstSync(connection({ status: "Disconnected" }))).toBe(false);
  });

  it("summarizes what was discovered, singular and plural", () => {
    expect(summarizeCounts(connection())).toBe("Nothing discovered yet");
    expect(summarizeCounts(connection({ pageCount: 1, leadFormCount: 3 })))
      .toBe("1 Page · 3 lead forms");
  });
});

describe("deliverySummary", () => {
  it("calls out the silent case where pages exist but none are enabled", () => {
    // A connection can look perfectly healthy while receiving nothing at all. Saying so is
    // the whole reason this string exists.
    expect(deliverySummary(connection({ pageCount: 2, enabledResourceCount: 0 })))
      .toBe("No Pages enabled, so no leads are being received.");
  });

  it("confirms delivery when at least one page is enabled", () => {
    expect(deliverySummary(connection({ pageCount: 2, enabledResourceCount: 1 })))
      .toBe("Receiving leads from 1 Page.");
  });

  it("explains a paused connection rather than implying it works", () => {
    expect(deliverySummary(connection({ status: "NeedsReauthorization", pageCount: 2, enabledResourceCount: 1 })))
      .toBe("Paused until this account is reconnected.");
  });
});

describe("resource presentation", () => {
  it("only offers a toggle for Facebook Pages", () => {
    // Enabling a page is what subscribes it for webhooks. Ads and campaigns are discovered
    // for attribution only, so offering a switch would imply control DAMS does not have.
    expect(isToggleable(resource())).toBe(true);
    expect(isToggleable(resource({ resourceType: "ad" }))).toBe(false);
    expect(isToggleable(resource({ resourceType: "lead_form" }))).toBe(false);
  });

  it("gives every known resource type a readable label", () => {
    expect(resourceTypeLabel("instagram_account")).toBe("Instagram account");
    expect(resourceTypeLabel("ad_set")).toBe("Ad set");
    expect(resourceTypeLabel("something_new")).toBe("something_new");
  });
});

describe("event list", () => {
  it("says an empty filtered list has none in that status, not that nothing ever arrived", () => {
    expect(emptyEventsMessage("All")).toBe("No webhook events recorded for this connection.");
    expect(emptyEventsMessage("Failed")).toBe("No failed events for this connection.");
    expect(emptyEventsMessage("Retry")).toBe("No retrying events for this connection.");
  });

  it("says when a list is cut off at its limit", () => {
    expect(eventListLimit("All")).toBe(25);
    expect(eventListLimit("Failed")).toBe(200);
    expect(eventListLimitNote("Failed", 199)).toBeNull();
    expect(eventListLimitNote("Failed", 200)).toBe(
      "Showing the 200 most recent failed events; there may be more.",
    );
    expect(eventListLimitNote("All", 25)).toBe("Showing the 25 most recent events.");
  });

  it("lets only the newest of overlapping loads apply", () => {
    const guard = createLatestRequestGuard();
    const first = guard.begin();
    const second = guard.begin();
    // The first request answering last must be ignored.
    expect(second()).toBe(true);
    expect(first()).toBe(false);
  });
});
