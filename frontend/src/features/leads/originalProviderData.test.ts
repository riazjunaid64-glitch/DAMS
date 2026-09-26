import { describe, expect, it } from "vitest";
import { canViewOriginalProviderData, formatProviderPayload } from "./originalProviderData.ts";

describe("canViewOriginalProviderData", () => {
  it("offers the original data to admins and managers only", () => {
    expect(canViewOriginalProviderData("Admin", "meta")).toBe(true);
    expect(canViewOriginalProviderData("Manager", "meta")).toBe(true);
    expect(canViewOriginalProviderData("Employee", "meta")).toBe(false);
    expect(canViewOriginalProviderData("Client", "meta")).toBe(false);
    expect(canViewOriginalProviderData(undefined, "meta")).toBe(false);
  });

  it("offers it only for Meta, the one channel that stores any", () => {
    expect(canViewOriginalProviderData("Admin", "website")).toBe(false);
  });
});

describe("formatProviderPayload", () => {
  it("indents stored JSON so it can be read", () => {
    expect(formatProviderPayload('{"leadgen_id":"lead-1","field_data":[{"name":"city"}]}')).toBe(
      '{\n  "leadgen_id": "lead-1",\n  "field_data": [\n    {\n      "name": "city"\n    }\n  ]\n}',
    );
  });

  it("never rounds a long numeric id, showing the payload as stored instead", () => {
    const stored = '{"ad_id":120211234567890123,"leadgen_id":"120211234567890123"}';
    expect(formatProviderPayload(stored)).toBe(stored);
  });

  it("re-indents a long id sent as a string, which cannot lose precision", () => {
    expect(formatProviderPayload('{"leadgen_id":"120211234567890123"}')).toBe('{\n  "leadgen_id": "120211234567890123"\n}');
  });

  it("shows anything that is not JSON exactly as stored", () => {
    expect(formatProviderPayload("not json {")).toBe("not json {");
  });

  it("has nothing to show when nothing was stored", () => {
    expect(formatProviderPayload(null)).toBeNull();
    expect(formatProviderPayload(undefined)).toBeNull();
    expect(formatProviderPayload("   ")).toBeNull();
  });
});
