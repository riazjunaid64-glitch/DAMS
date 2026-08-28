import { describe, expect, it } from "vitest";
import { financeApiError } from "./financeAttachments";

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const FALLBACK = "Could not record this movement.";

describe("financeApiError", () => {
  it("prefers the controller's own message", () => {
    return expect(financeApiError(json(400, { message: "Amount must be greater than zero." }), FALLBACK))
      .resolves.toBe("Amount must be greater than zero.");
  });

  it("reads the per-field errors of a model-binding rejection", async () => {
    // What [ApiController] returns on its own, before any controller code runs.
    const body = {
      type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
      title: "One or more validation errors occurred.",
      status: 400,
      errors: { Date: ["The value 'x' is not valid."], Amount: ["The Amount field is required."] },
    };
    await expect(financeApiError(json(400, body), FALLBACK))
      .resolves.toBe("The value 'x' is not valid. The Amount field is required.");
  });

  it("falls back to detail, then title, when there are no field errors", async () => {
    await expect(financeApiError(json(500, { title: "An error occurred.", detail: "Object reference not set." }), FALLBACK))
      .resolves.toBe("Object reference not set.");
    await expect(financeApiError(json(500, { title: "An error occurred." }), FALLBACK))
      .resolves.toBe("An error occurred.");
  });

  it("stamps the status when the server gave no reason at all", async () => {
    await expect(financeApiError(new Response("", { status: 500 }), FALLBACK))
      .resolves.toBe(`${FALLBACK} (HTTP 500)`);
    await expect(financeApiError(new Response("<html>oops</html>", { status: 502 }), FALLBACK))
      .resolves.toBe(`${FALLBACK} (HTTP 502)`);
    await expect(financeApiError(json(500, {}), FALLBACK))
      .resolves.toBe(`${FALLBACK} (HTTP 500)`);
    await expect(financeApiError(json(500, null), FALLBACK))
      .resolves.toBe(`${FALLBACK} (HTTP 500)`);
  });

  it("keeps the attachment-size message, which the status alone would not explain", () => {
    return expect(financeApiError(new Response("", { status: 413 }), FALLBACK))
      .resolves.toBe("The attachment is too large. The maximum allowed size is 15 MB.");
  });
});
