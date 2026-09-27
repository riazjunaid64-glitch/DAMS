import { Link, Outlet, useLocation } from "react-router-dom";
import type { User } from "../App.tsx";
import SiteFooter from "../components/SiteFooter.tsx";
import {
  activeNavItem,
  BottomNav,
  Button,
  PhoneTopBar,
  Sidebar,
  TopBar,
  type Crumb,
  useIsPhone,
  type NavGroup,
} from "../components/ui";
import NotificationBell from "../features/notifications/NotificationBell.tsx";
import { SITE_CONTACT } from "../lib/siteContact.ts";
import { homePathFor, isSalesRole } from "./navigation.tsx";

interface AppLayoutProps {
  user: User | null;
  navGroups: NavGroup[];
  displayName: string;
  setModal: (m: null | "login" | "signup") => void;
  onLogout: () => void;
}

const ROLE_LABELS: Record<string, string> = { Manager: "Sales manager", Employee: "Sales employee" };

/**
 * The app shell: white sidebar and top bar on desktop; top bar and bottom nav on phone. The menu
 * comes from the role's navigation config, so the sidebar, bottom nav and breadcrumb always agree.
 */
export default function AppLayout({ user, navGroups, displayName, setModal, onLogout }: AppLayoutProps) {
  const { pathname } = useLocation();
  const home = homePathFor(user?.role);
  const section = activeNavItem(navGroups, pathname);
  const group = section && navGroups.find((g) => g.items.includes(section));
  const innerPage = section != null && pathname !== section.to;
  const staff = user != null && (user.role === "Admin" || isSalesRole(user.role));

  const crumbs: Crumb[] = [
    ...(group?.label ? [{ label: group.label }] : []),
    ...(section ? [{ label: section.label, to: innerPage ? section.to : undefined }] : []),
  ];

  const logo = (
    <Link to={home} aria-label={`${SITE_CONTACT.brandName} home`} className="flex items-center">
      <img src={SITE_CONTACT.logoSrc} alt={SITE_CONTACT.brandName} className="h-8 w-auto" decoding="async" />
    </Link>
  );

  // One bell only: it polls for notifications, so a second copy would double the requests.
  const isPhone = useIsPhone();
  const bell = user && <NotificationBell key={`${user.userId}:${user.role}`} accountKey={`${user.userId}:${user.role}`} />;

  return (
    <div className="flex min-h-dvh flex-col bg-page font-ui text-ink">
      <Sidebar
        logo={logo}
        groups={navGroups}
        user={user ? { name: displayName, role: ROLE_LABELS[user.role] ?? user.role } : undefined}
        onLogout={onLogout}
        footer={
          <div className="flex flex-col gap-2">
            <Button variant="outline" fullWidth onClick={() => setModal("login")}>Log in</Button>
            <Button fullWidth onClick={() => setModal("signup")}>Sign up</Button>
          </div>
        }
      />

      <div className="flex min-w-0 flex-1 flex-col pb-[calc(64px+env(safe-area-inset-bottom))] md:pb-0 md:pl-[248px]">
        <TopBar
          crumbs={crumbs}
          actions={isPhone ? null : bell ?? <Button size="sm" onClick={() => setModal("signup")}>Sign up</Button>}
        />
        <PhoneTopBar
          logo={logo}
          back={innerPage && section ? { to: section.to, label: section.label } : undefined}
          actions={!isPhone ? null : bell ?? <Button size="sm" variant="outline" onClick={() => setModal("login")}>Log in</Button>}
        />

        <main className="flex min-w-0 flex-1 flex-col">
          <Outlet />
        </main>

        {/* The public footer belongs to the website; staff work in the app shell alone. */}
        {!staff && <SiteFooter />}
      </div>

      <BottomNav items={navGroups.flatMap((g) => g.items)} />
    </div>
  );
}
