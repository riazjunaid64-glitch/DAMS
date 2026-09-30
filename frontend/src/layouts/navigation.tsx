import {
  IconBadge,
  IconBell,
  IconBuilding,
  IconCalendarCheck,
  IconFile,
  IconFolder,
  IconHome,
  IconInfo,
  IconMail,
  IconTarget,
  IconUsers,
  IconWallet,
  type NavGroup,
} from "../components/ui";
import { can, homePathFor, isSalesRole } from "../features/access/permissions.ts";

export { homePathFor, isSalesRole };

/*
 * The menu for each role, in one place. The sidebar, the phone bottom nav, the breadcrumb and the
 * footer links are all drawn from what this returns.
 */

const ICON = 19;

const website: NavGroup = {
  label: "Website",
  items: [
    { to: "/", label: "Home", icon: <IconHome size={ICON} /> },
    { to: "/projects", label: "Projects", icon: <IconBuilding size={ICON} /> },
    { to: "/about", label: "About", icon: <IconInfo size={ICON} /> },
    { to: "/contact", label: "Contact", icon: <IconMail size={ICON} /> },
  ],
};

export function navigationFor(role: string | null | undefined): NavGroup[] {
  if (can(role, "finance") && can(role, "crm")) {
    return [
      {
        label: "Sales",
        items: [
          { to: "/crm", label: "Lead CRM", icon: <IconTarget size={ICON} /> },
          { to: "/projects", label: "Projects", icon: <IconBuilding size={ICON} /> },
          { to: "/confirmed-bookings", label: "Bookings", icon: <IconCalendarCheck size={ICON} /> },
          { to: "/customers", label: "Customers", icon: <IconUsers size={ICON} /> },
        ],
      },
      {
        label: "Company",
        items: [
          { to: "/employees", label: "Employees", icon: <IconBadge size={ICON} /> },
          { to: "/finance", label: "Finance", icon: <IconWallet size={ICON} /> },
          { to: "/customer-document-categories", label: "Document setup", icon: <IconFile size={ICON} /> },
          { to: "/notifications/settings", label: "Notifications", icon: <IconBell size={ICON} /> },
        ],
      },
      { ...website, items: website.items.filter((item) => item.to !== "/projects") },
    ];
  }
  if (isSalesRole(role)) {
    return [{
      items: [
        { to: "/crm", label: "Lead CRM", icon: <IconTarget size={ICON} /> },
        { to: "/projects", label: "Projects", icon: <IconBuilding size={ICON} /> },
      ],
    }];
  }
  if (can(role, "finance")) {
    return [
      {
        label: "Sales",
        items: [
          { to: "/projects", label: "Projects", icon: <IconBuilding size={ICON} /> },
          { to: "/confirmed-bookings", label: "Bookings", icon: <IconCalendarCheck size={ICON} /> },
          { to: "/customers", label: "Customers", icon: <IconUsers size={ICON} /> },
        ],
      },
      {
        label: "Company",
        items: [
          { to: "/employees", label: "Employees", icon: <IconBadge size={ICON} /> },
          { to: "/finance", label: "Finance", icon: <IconWallet size={ICON} /> },
          { to: "/customer-document-categories", label: "Document setup", icon: <IconFile size={ICON} /> },
        ],
      },
      { ...website, items: website.items.filter((item) => item.to !== "/projects") },
    ];
  }
  if (role) {
    return [
      { items: [{ to: "/my-projects", label: "My Projects", icon: <IconFolder size={ICON} /> }] },
      website,
    ];
  }
  return [{ items: website.items }];
}
