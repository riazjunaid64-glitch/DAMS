import { useRef } from "react";

/**
 * Retry safety for requests that create money.
 *
 * Every money-creating endpoint requires an `Idempotency-Key` header. The server reserves the key,
 * does the work, and remembers the response, so the same key can never record the same receipt,
 * expense or deposit twice. That only helps if the key stays the SAME across retries of one intent
 * and differs for a deliberate second entry — which is why it is created here, in the page, rather
 * than per fetch call: a fresh key per attempt would sail past the check and duplicate the money.
 */
export function newIdempotencyKey(prefix: string): string {
  return `${prefix}-${globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`}`;
}

/**
 * Keys held per operation signature for the life of a screen.
 *
 * The signature describes what is being recorded (`installment:12:50000:2026-08-19`). Retrying after
 * a failure reuses the key, so a request that actually committed before the connection dropped is
 * recognised instead of repeated. Editing the amount changes the signature — that is a different
 * intent and correctly gets its own key. Keys are released once the server has confirmed the save,
 * so recording a genuine second identical payment later is never blocked.
 */
export function useIdempotencyKeys() {
  const keys = useRef(new Map<string, string>());
  return {
    key(signature: string, prefix: string): string {
      const existing = keys.current.get(signature);
      if (existing != null) return existing;
      const created = newIdempotencyKey(prefix);
      keys.current.set(signature, created);
      return created;
    },
    release(signature: string): void {
      keys.current.delete(signature);
    },
  };
}

/** Adds the header to a request. Use for every POST that brings money into existence. */
export function moneyRequest(key: string, init: RequestInit): RequestInit {
  return {
    ...init,
    headers: { ...((init.headers as Record<string, string> | undefined) ?? {}), "Idempotency-Key": key },
  };
}
