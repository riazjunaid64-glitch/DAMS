import { describe, expect, it } from "vitest";
import { navigationFor } from "./navigation.tsx";

const items = (role: string) => navigationFor(role).flatMap((group) => group.items);
const labels = (role: string) => items(role).map((item) => item.label);

describe("navigationFor", () => {
  it("has no Requests item for anyone", () => {
    for (const role of ["Admin", "Accountant", "Sales manager", "Sales employee", "Client"]) {
      expect(labels(role)).not.toContain("Requests");
      expect(items(role).map((item) => item.to)).not.toContain("/bookings");
    }
  });

  it("gives the Admin Lead CRM, Projects, Bookings, Customers first, so the phone bar shows them before More", () => {
    expect(labels("Admin").slice(0, 4)).toEqual(["Lead CRM", "Projects", "Bookings", "Customers"]);
  });

  it("gives the Accountant Projects, Bookings, Customers, Employees first and no Lead CRM", () => {
    expect(labels("Accountant").slice(0, 4)).toEqual(["Projects", "Bookings", "Customers", "Employees"]);
    expect(labels("Accountant")).not.toContain("Lead CRM");
  });

  it("never shows Bookings to sales roles", () => {
    expect(labels("Sales manager")).not.toContain("Bookings");
    expect(labels("Sales employee")).not.toContain("Bookings");
  });

  it("never shows Document setup in the side menu (entry is on the Customers page)", () => {
    for (const role of ["Admin", "Accountant", "Sales manager", "Sales employee", "Client"]) {
      expect(labels(role)).not.toContain("Document setup");
      expect(items(role).map((item) => item.to)).not.toContain("/customer-document-categories");
      expect(items(role).map((item) => item.to)).not.toContain("/customers/document-setup");
    }
  });
});
