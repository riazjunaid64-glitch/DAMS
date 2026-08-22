import { describe, expect, it } from "vitest";
import {
  buildActivationPayload,
  describeActivationFailure,
  hasValidationErrors,
  MAX_PASSWORD_BYTES,
  messageFromBody,
  readActivationToken,
  stripTokenFromUrl,
  utf8ByteLength,
  validateActivation,
} from "./staffActivationState.ts";

describe("reading the token from the link", () => {
  it("reads the token out of the fragment invitation emails now use", () => {
    expect(readActivationToken("", "#token=abc123")).toBe("abc123");
  });

  it("still reads a token from the query string of an older link", () => {
    // Links issued before the fragment change are already sitting in inboxes. Refusing them
    // would strand employees whose only route back is asking for a whole new invitation.
    expect(readActivationToken("?token=abc123", "")).toBe("abc123");
  });

  it("prefers the fragment when a link somehow carries both", () => {
    expect(readActivationToken("?token=fromQuery", "#token=fromFragment")).toBe("fromFragment");
  });

  it("treats a link with no token as a missing link", () => {
    expect(readActivationToken("", "")).toBeNull();
    expect(readActivationToken("?ref=email", "#setup")).toBeNull();
  });

  it("treats an empty token as missing rather than sending nothing to the backend", () => {
    expect(readActivationToken("?token=", "")).toBeNull();
    expect(readActivationToken("", "#token=")).toBeNull();
  });

  it("does not transform the token", () => {
    // Case, padding and punctuation are the backend's business. A token that gets lowercased or
    // trimmed on the way in is a token that will never match the stored hash.
    expect(readActivationToken("", "#token=AbC-_xYz09")).toBe("AbC-_xYz09");
    expect(readActivationToken("", "#token=%20abc%20")).toBe(" abc ");
    expect(readActivationToken("", "#token=a%2Bb%2Fc%3D")).toBe("a+b/c=");
    expect(readActivationToken("?token=AbC-_xYz09", "")).toBe("AbC-_xYz09");
  });

  it("does not care how long the token is", () => {
    // Tokens are 43 characters today. Asserting that here would tie the page to an implementation
    // detail the backend is free to change.
    expect(readActivationToken("", "#token=x")).toBe("x");
    expect(readActivationToken("", `#token=${"y".repeat(90)}`)).toBe("y".repeat(90));
  });
});

describe("scrubbing the token from the address bar", () => {
  it("removes a fragment token", () => {
    expect(stripTokenFromUrl("/activate-account#token=secret")).toBe("/activate-account");
  });

  it("removes a query token", () => {
    expect(stripTokenFromUrl("/activate-account?token=secret")).toBe("/activate-account");
  });

  it("keeps unrelated parameters and the hash", () => {
    expect(stripTokenFromUrl("/activate-account?token=secret&ref=email#setup")).toBe(
      "/activate-account?ref=email#setup"
    );
    expect(stripTokenFromUrl("/activate-account?ref=email&token=secret")).toBe(
      "/activate-account?ref=email"
    );
    expect(stripTokenFromUrl("/activate-account?ref=email#token=secret")).toBe(
      "/activate-account?ref=email"
    );
  });

  it("keeps the rest of a fragment that carried more than the token", () => {
    expect(stripTokenFromUrl("/activate-account#ref=email&token=secret")).toBe(
      "/activate-account#ref=email"
    );
  });

  it("leaves a URL without a token untouched", () => {
    expect(stripTokenFromUrl("/activate-account")).toBe("/activate-account");
    // A plain fragment is not a parameter list, so it must come back byte for byte rather than
    // being rewritten as "#setup=".
    expect(stripTokenFromUrl("/activate-account#setup")).toBe("/activate-account#setup");
  });

  it("never leaves the secret anywhere in the result", () => {
    expect(stripTokenFromUrl("/activate-account?token=secret&ref=email#setup")).not.toContain("secret");
    expect(stripTokenFromUrl("/activate-account?ref=email#token=secret")).not.toContain("secret");
  });
});

describe("password rules", () => {
  it("rejects a password shorter than the backend minimum", () => {
    expect(validateActivation("7chars!", "7chars!").password).toBeDefined();
  });

  it("accepts a password of exactly the minimum length", () => {
    expect(validateActivation("8chars!!", "8chars!!")).toEqual({});
  });

  it("rejects an empty password and an empty confirmation", () => {
    const found = validateActivation("", "");
    expect(found.password).toBeDefined();
    expect(found.confirmPassword).toBeDefined();
    expect(hasValidationErrors(found)).toBe(true);
  });

  it("rejects a mismatched confirmation", () => {
    const found = validateActivation("correct-horse", "correct-house");
    expect(found.password).toBeUndefined();
    expect(found.confirmPassword).toBeDefined();
  });

  it("accepts a password of exactly 72 UTF-8 bytes", () => {
    const password = "a".repeat(MAX_PASSWORD_BYTES);
    expect(utf8ByteLength(password)).toBe(72);
    expect(validateActivation(password, password)).toEqual({});
  });

  it("rejects a password over 72 UTF-8 bytes", () => {
    const password = "a".repeat(MAX_PASSWORD_BYTES + 1);
    expect(validateActivation(password, password).password).toBeDefined();
  });

  it("counts multibyte characters in bytes, not characters", () => {
    // 18 four-byte emoji: 36 characters, and exactly bcrypt's 72-byte boundary once encoded.
    // Measuring characters here would send passwords the backend refuses.
    const emoji = "😀".repeat(18);
    expect(emoji.length).toBeLessThan(MAX_PASSWORD_BYTES);
    expect(utf8ByteLength(emoji)).toBe(72);
    expect(validateActivation(emoji, emoji)).toEqual({});

    const tooLong = `${emoji}a`;
    expect(tooLong.length).toBeLessThan(MAX_PASSWORD_BYTES);
    expect(utf8ByteLength(tooLong)).toBe(73);
    expect(validateActivation(tooLong, tooLong).password).toBeDefined();
  });

  it("does not invent complexity rules the backend does not have", () => {
    expect(validateActivation("aaaaaaaa", "aaaaaaaa")).toEqual({});
  });
});

describe("the activation request body", () => {
  it("contains the token and password and nothing else", () => {
    const payload = buildActivationPayload("secret-token", "chosen-password");
    expect(Object.keys(payload).sort()).toEqual(["password", "token"]);
    expect(payload).toEqual({ token: "secret-token", password: "chosen-password" });
  });

  it("never carries the confirmation, an email or an identifier", () => {
    const serialised = JSON.stringify(buildActivationPayload("secret-token", "chosen-password"));
    for (const forbidden of ["confirm", "email", "userId", "employeeId", "role", "accountStatus"]) {
      expect(serialised.toLowerCase()).not.toContain(forbidden.toLowerCase());
    }
  });

  it("sends the token exactly as it was captured", () => {
    expect(buildActivationPayload(" MiXeD-token ", "chosen-password").token).toBe(" MiXeD-token ");
  });
});

describe("what the employee is told when it fails", () => {
  it("repeats the backend's deliberately vague invitation message", () => {
    expect(describeActivationFailure(400, "This activation link is invalid or has expired.")).toBe(
      "This activation link is invalid or has expired."
    );
  });

  it("gives a plain message when the rate limiter answers", () => {
    const message = describeActivationFailure(429, "Too many requests");
    expect(message).toContain("Too many activation attempts");
  });

  it("does not repeat a server fault back to the employee", () => {
    expect(describeActivationFailure(500, "Object reference not set to an instance of an object")).toBe(
      "We could not activate the account right now. Please try again."
    );
  });

  it("falls back to a generic message when the network failed", () => {
    expect(describeActivationFailure(0, null)).toBe(
      "We could not activate the account right now. Please try again."
    );
    expect(describeActivationFailure(400, "   ")).toBe(
      "We could not activate the account right now. Please try again."
    );
  });

  it("reads the message from an Auth response body", () => {
    expect(messageFromBody({ message: "This activation link is invalid or has expired." })).toBe(
      "This activation link is invalid or has expired."
    );
    expect(messageFromBody({ errors: { Password: ["too short"] } })).toBeNull();
    expect(messageFromBody(null)).toBeNull();
    expect(messageFromBody("plain text")).toBeNull();
  });
});
