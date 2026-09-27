import { describe, expect, it } from "vitest";
import { statusLabel, statusTone } from "./statusTone.ts";

describe("statusTone", () => {
  it("maps the design's statuses to their colours", () => {
    expect(statusTone("In progress")).toBe("blue");
    expect(statusTone("Ongoing")).toBe("blue");
    expect(statusTone("Won")).toBe("green");
    expect(statusTone("Available")).toBe("green");
    expect(statusTone("Completed")).toBe("green");
    expect(statusTone("Lost")).toBe("red");
    expect(statusTone("Dormant")).toBe("orange");
    expect(statusTone("Booked")).toBe("orange");
    expect(statusTone("Planning")).toBe("orange");
    expect(statusTone("Needs details")).toBe("orange");
    expect(statusTone("Sold")).toBe("grey");
  });
  it("ignores case, spaces and separators", () => {
    expect(statusTone("InProgress")).toBe("blue");
    expect(statusTone("in_progress")).toBe("blue");
    expect(statusTone("NEEDS-DETAILS")).toBe("orange");
  });
  it("falls back to grey for anything unknown", () => {
    expect(statusTone("Something else")).toBe("grey");
  });
});

describe("statusLabel", () => {
  it("turns codes into sentence case", () => {
    expect(statusLabel("InProgress")).toBe("In progress");
    expect(statusLabel("needs_details")).toBe("Needs details");
    expect(statusLabel("Won")).toBe("Won");
  });
});
