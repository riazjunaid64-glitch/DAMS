/**
 * Who can open what. The menu, the home page and the buttons all read this table,
 * which matches the role rules on the server.
 */

export type Capability =
  | "crm"
  | "crm.manage"
  | "crm.settings"
  | "projects.view"
  | "projects.write"
  | "bookings"
  | "customers"
  | "employees"
  | "finance"
  | "documents"
  | "notifications.admin"
  /** Give roles and change employment status in CRM settings. Admin only. */
  | "staff.admin";

const ALL: Capability[] = [
  "crm",
  "crm.manage",
  "crm.settings",
  "projects.view",
  "projects.write",
  "bookings",
  "customers",
  "employees",
  "finance",
  "documents",
  "notifications.admin",
  "staff.admin",
];

const BY_ROLE: Record<string, ReadonlySet<Capability>> = {
  Admin: new Set(ALL),
  Manager: new Set<Capability>(["crm", "crm.manage", "crm.settings", "projects.view"]),
  Employee: new Set<Capability>(["crm", "projects.view"]),
  Accountant: new Set<Capability>([
    "projects.view",
    "projects.write",
    "bookings",
    "customers",
    "employees",
    "finance",
    "documents",
  ]),
};

export function can(role: string | null | undefined, capability: Capability): boolean {
  if (!role) return false;
  return BY_ROLE[role]?.has(capability) ?? false;
}

/**
 * What a page that needs one capability should do right now. The signed-in user is read after the
 * page first renders, so "no role yet" means "still loading", not "not allowed": sending someone home
 * in that gap is what bounced every refresh.
 */
export function pageAccess(role: string | null | undefined, capability: Capability): "wait" | "allow" | "deny" {
  if (!role) return "wait";
  return can(role, capability) ? "allow" : "deny";
}

export function isSalesRole(role: string | null | undefined): boolean {
  return role === "Manager" || role === "Employee";
}

/** Where a role lands after login, and where the logo goes. */
export function homePathFor(role: string | null | undefined): string {
  if (isSalesRole(role)) return "/crm";
  // Accountants start on the Bookings list.
  if (role === "Accountant") return "/confirmed-bookings";
  return "/";
}

export function roleLabel(role: string | null | undefined): string {
  switch (role) {
    case "Admin":
      return "Admin";
    case "Manager":
      return "Sales manager";
    case "Employee":
      return "Sales employee";
    case "Accountant":
      return "Accountant";
    default:
      return role ?? "No login";
  }
}
