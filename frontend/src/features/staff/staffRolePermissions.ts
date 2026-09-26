/**
 * What a signed-in role may do in CRM settings. The server enforces every rule here
 * (StaffManagementService); these only keep the screen from offering what it would refuse.
 */

/** Admins and Sales Managers run CRM settings; salespeople do not. */
export const canOpenCrmSettings = (actorRole: string) =>
  actorRole === "Admin" || actorRole === "Manager";

/** Only an Admin may hand out the Admin role. */
export const grantableRoles = (actorRole: string): [string, string][] => [
  ...(actorRole === "Admin" ? [["Admin", "Admin"] as [string, string]] : []),
  ["Manager", "Sales Manager"],
  ["Employee", "Sales Employee"],
];

/** A non-admin may act only on accounts that are already staff below Admin. */
export const canActOnAccount = (actorRole: string, accountRole?: string | null) =>
  actorRole === "Admin" || accountRole === "Manager" || accountRole === "Employee";

/** Employment status is HR data from the Admin-only Employees area. */
export const canChangeEmploymentStatus = (actorRole: string) => actorRole === "Admin";
