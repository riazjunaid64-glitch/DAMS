import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ACCESS_CHANGED_EVENT, api, setAccessToken } from "./api";

function installWindow() {
  const listeners = new Map<string, Set<EventListener>>();
  vi.stubGlobal("window", {
    addEventListener(type: string, listener: EventListener) {
      const set = listeners.get(type) ?? new Set<EventListener>();
      set.add(listener);
      listeners.set(type, set);
    },
    removeEventListener(type: string, listener: EventListener) {
      listeners.get(type)?.delete(listener);
    },
    dispatchEvent(event: Event) {
      for (const listener of listeners.get(event.type) ?? []) listener(event);
      return true;
    },
  });
}

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("a rejected session", () => {
  beforeEach(() => installWindow());

  afterEach(() => {
    setAccessToken(null);
    vi.unstubAllGlobals();
  });

  it("asks the user to sign in again when refresh cannot replace it", async () => {
    setAccessToken("old-access");
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/api/Auth/refresh")) return new Response("", { status: 401 });
      return new Response("", { status: 401 });
    }));

    let announced = false;
    const heard = new Promise<void>((resolve) => {
      window.addEventListener(ACCESS_CHANGED_EVENT, () => {
        announced = true;
        resolve();
      }, { once: true });
    });

    const response = await api("/api/leads");
    await heard;

    expect(response.status).toBe(401);
    expect(announced).toBe(true);
  });

  it("stays quiet when the refresh cookie still signs them in", async () => {
    setAccessToken("old-access");
    let leads = 0;
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/api/Auth/refresh")) return json(200, { accessToken: "renewed" });
      leads += 1;
      return new Response("[]", { status: leads === 1 ? 401 : 200 });
    }));

    let announced = false;
    window.addEventListener(ACCESS_CHANGED_EVENT, () => {
      announced = true;
    });

    const response = await api("/api/leads");
    expect(response.status).toBe(200);
    expect(leads).toBe(2);
    expect(announced).toBe(false);
  });

  it("does not announce a request that was never signed in", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("", { status: 401 })));
    let announced = false;
    window.addEventListener(ACCESS_CHANGED_EVENT, () => {
      announced = true;
    });

    await api("/api/leads");
    expect(announced).toBe(false);
  });
});
