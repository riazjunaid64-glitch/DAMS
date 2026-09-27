import { describe, expect, it } from "vitest";
import { activeNavItem, isNavActive } from "./navigation.ts";

describe("isNavActive", () => {
  it("matches the section and pages below it", () => {
    expect(isNavActive("/crm", "/crm")).toBe(true);
    expect(isNavActive("/crm/leads/4", "/crm")).toBe(true);
    expect(isNavActive("/crmx", "/crm")).toBe(false);
  });
  it("matches home only exactly", () => {
    expect(isNavActive("/", "/")).toBe(true);
    expect(isNavActive("/crm", "/")).toBe(false);
  });
});

describe("activeNavItem", () => {
  const groups = [{ items: [
    { to: "/notifications", label: "Inbox", icon: null },
    { to: "/notifications/settings", label: "Notification settings", icon: null },
  ] }];
  it("prefers the most specific item", () => {
    expect(activeNavItem(groups, "/notifications/settings")?.label).toBe("Notification settings");
    expect(activeNavItem(groups, "/notifications")?.label).toBe("Inbox");
    expect(activeNavItem(groups, "/other")).toBeUndefined();
  });
});
