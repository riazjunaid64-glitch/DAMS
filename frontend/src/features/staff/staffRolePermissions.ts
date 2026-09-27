/**
 * What a signed-in role may do in CRM settings. The server enforces every rule here
 * (StaffManagementService); these only keep the screen from offering what it would refuse.
 */
import { can } from "../access/permissions.ts";

/** Admins and Sales Managers run CRM settings; salespeople and accountants do not. */
export const canOpenCrmSettings = (actorRole: string) => can(actorRole, "crm.settings");

/** Only an Admin may hand out Admin or Accountant. A manager gives the two sales roles. */
export const grantableRoles = (actorRole: string): [string, string][] => [
  ...(can(actorRole, "staff.admin")
    ? [
        ["Admin", "Admin"] as [string, string],
        ["Accountant", "Accountant"] as [string, string],
      ]
    : []),
  ["Manager", "Sales manager"],
  ["Employee", "Sales employee"],
];

/** A non-admin may act only on logins that are already a sales role. */
export const canActOnAccount = (actorRole: string, accountRole?: string | null) =>
  can(actorRole, "staff.admin") || accountRole === "Manager" || accountRole === "Employee";

/** Employment status is HR data. CRM settings leaves it to an Admin. */
export const canChangeEmploymentStatus = (actorRole: string) => can(actorRole, "staff.admin");
