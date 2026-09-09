import { describe, expect, it } from "vitest";
import {
  buildVerificationPayload,
  describeVerificationFailure,
  hasValidationErrors,
  readVerificationToken,
  stripTokenFromUrl,
  validateVerification,
} from "./emailVerificationState.ts";

describe("readVerificationToken", () => {
  it("prefers the fragment, which never reaches a server", () => {
    expect(readVerificationToken("?token=from-query", "#token=from-fragment")).toBe("from-fragment");
  });

  it("still reads a query string, so links already in an inbox keep working", () => {
    expect(readVerificationToken("?token=from-query", "")).toBe("from-query");
  });

  it("treats a missing or empty token as no token at all", () => {
    expect(readVerificationToken("", "")).toBeNull();
    expect(readVerificationToken("?token=", "#token=")).toBeNull();
  });

  it("leaves the token exactly as it arrived", () => {
    // Opaque credential: no trimming, no case change, no format check. Validating the shape here
    // would break the day the backend issued a different one.
    expect(readVerificationToken("", "#token=%20AbC-_9%20")).toBe(" AbC-_9 ");
  });
});

describe("stripTokenFromUrl", () => {
  it("removes the token from the address bar without disturbing anything else", () => {
    expect(stripTokenFromUrl("/verify-email#token=secret")).toBe("/verify-email");
    expect(stripTokenFromUrl("/verify-email?token=secret&ref=email")).toBe("/verify-email?ref=email");
    expect(stripTokenFromUrl("/verify-email?ref=email#token=secret")).toBe("/verify-email?ref=email");
  });

  it("never leaves the secret anywhere in the result", () => {
    expect(stripTokenFromUrl("/verify-email?token=secret&ref=email#setup")).not.toContain("secret");
    expect(stripTokenFromUrl("/verify-email#ref=email&token=secret")).not.toContain("secret");
  });
});

describe("validateVerification", () => {
  it("accepts a password the backend will accept", () => {
    expect(hasValidationErrors(validateVerification("long-enough-1", "long-enough-1"))).toBe(false);
  });

  it("names the floor rather than silently failing at the server", () => {
    expect(validateVerification("short", "short").password).toContain("8");
  });

  it("measures the ceiling in bytes, as bcrypt does", () => {
    // 24 three-byte characters: well under 72 characters, and over 72 bytes. A character count
    // here would accept a password the backend then rejects.
    const overLong = "پ".repeat(37);
    expect(validateVerification(overLong, overLong).password).toBeDefined();
  });

  it("catches a mistyped confirmation before the single-use token is spent", () => {
    expect(validateVerification("long-enough-1", "long-enough-2").confirmPassword)
      .toBe("The two passwords do not match.");
  });
});

describe("buildVerificationPayload", () => {
  it("sends the token and both passwords, and nothing else", () => {
    // The confirmation goes to the server too, so a typo is refused before the credential is
    // consumed. Asserting the exact key set is what keeps page state out of the request body.
    const payload = buildVerificationPayload("t", "p", "p");
    expect(Object.keys(payload).sort()).toEqual(["confirmPassword", "password", "token"]);
  });
});

describe("describeVerificationFailure", () => {
  it("repeats the backend's deliberately uninformative message verbatim", () => {
    // Unknown, expired, spent, superseded and revoked all produce this one sentence. Rewording it
    // into something more helpful would tell a stranger which links had already been used.
    const generic = "This verification link is invalid or has expired. Request a new one and try again.";
    expect(describeVerificationFailure(400, generic)).toBe(generic);
  });

  it("says what to do about a rate limit", () => {
    expect(describeVerificationFailure(429, null)).toContain("few minutes");
  });

  it("does not show a server fault body to a stranger", () => {
    expect(describeVerificationFailure(500, "Object reference not set to an instance of an object"))
      .toBe("We could not confirm your email address right now. Please try again.");
    expect(describeVerificationFailure(0, null))
      .toBe("We could not confirm your email address right now. Please try again.");
  });
});
