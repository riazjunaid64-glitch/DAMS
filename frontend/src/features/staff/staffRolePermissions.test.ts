import { describe, expect, it } from "vitest";
import { canActOnAccount, canChangeEmploymentStatus, canOpenCrmSettings, grantableRoles } from "./staffRolePermissions.ts";

describe("CRM settings role checks", () => {
  it("opens CRM settings to Admins and Sales Managers only", () => {
    expect(canOpenCrmSettings("Admin")).toBe(true);
    expect(canOpenCrmSettings("Manager")).toBe(true);
    expect(canOpenCrmSettings("Employee")).toBe(false);
    expect(canOpenCrmSettings("Client")).toBe(false);
  });

  it("offers the Admin role only to an Admin", () => {
    expect(grantableRoles("Admin").map(([v]) => v)).toEqual(["Admin", "Manager", "Employee"]);
    expect(grantableRoles("Manager").map(([v]) => v)).toEqual(["Manager", "Employee"]);
  });

  it("lets a manager act only on Sales Manager and Salesperson accounts", () => {
    expect(canActOnAccount("Manager", "Employee")).toBe(true);
    expect(canActOnAccount("Manager", "Manager")).toBe(true);
    expect(canActOnAccount("Manager", "Admin")).toBe(false);
    expect(canActOnAccount("Manager", "Client")).toBe(false);
    expect(canActOnAccount("Manager", null)).toBe(false);
    expect(canActOnAccount("Admin", "Admin")).toBe(true);
  });

  it("keeps employment status with the Admin", () => {
    expect(canChangeEmploymentStatus("Admin")).toBe(true);
    expect(canChangeEmploymentStatus("Manager")).toBe(false);
  });
});
