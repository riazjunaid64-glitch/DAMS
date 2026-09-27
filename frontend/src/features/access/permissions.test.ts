import { describe, expect, it } from "vitest";
import { can, homePathFor } from "./permissions.ts";

describe("role permissions", () => {
  it("keeps the Accountant out of the Lead CRM and staff administration", () => {
    expect(can("Accountant", "crm")).toBe(false);
    expect(can("Accountant", "crm.settings")).toBe(false);
    expect(can("Accountant", "staff.admin")).toBe(false);
    expect(can("Accountant", "notifications.admin")).toBe(false);
    expect(can("Accountant", "bookings")).toBe(true);
    expect(can("Accountant", "finance")).toBe(true);
  });

  it("leaves staff administration with the Admin", () => {
    expect(can("Admin", "staff.admin")).toBe(true);
    expect(can("Manager", "staff.admin")).toBe(false);
  });

  it("lands each role on a page it can use", () => {
    expect(homePathFor("Admin")).toBe("/");
    expect(homePathFor("Manager")).toBe("/crm");
    expect(homePathFor("Employee")).toBe("/crm");
    expect(homePathFor("Accountant")).toBe("/confirmed-bookings");
  });
});
