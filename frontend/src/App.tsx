import { lazy, Suspense, useEffect, useMemo, useState } from "react";
import { Route, Routes } from "react-router-dom";
import { api, refreshAccessToken, setAccessToken } from "./api/api";
import AuthModal from "./components/AuthModal.tsx";
import { ProjectsProvider } from "./contexts/ProjectsContext.tsx";
import { detachPushOnLogout } from "./features/notifications/push.ts";
import AppLayout from "./layouts/AppLayout.tsx";

const AboutPage = lazy(() => import("./pages/AboutPage.tsx"));
const ContactPage = lazy(() => import("./pages/ContactPage.tsx"));
const HomePage = lazy(() => import("./pages/HomePage.tsx"));
const LandingPage = lazy(() => import("./pages/LandingPage.tsx"));
const EmployeesPage = lazy(() => import("./pages/EmployeesPage.tsx"));
const EmployeeDetailPage = lazy(() => import("./pages/EmployeeDetailPage.tsx"));
const BookingRequestsPage = lazy(() => import("./pages/BookingRequestsPage.tsx"));
const CustomersPage = lazy(() => import("./pages/CustomersPage.tsx"));
const CustomerDetailPage = lazy(() => import("./pages/CustomerDetailPage.tsx"));
const CustomerDocumentCategoriesPage = lazy(() => import("./pages/CustomerDocumentCategoriesPage.tsx"));
const ConfirmedBookingsPage = lazy(() => import("./pages/ConfirmedBookingsPage.tsx"));
const CreateBookingPage = lazy(() => import("./pages/CreateBookingPage.tsx"));
const ApplicationFormPage = lazy(() => import("./pages/ApplicationFormPage.tsx"));
const FinanceDashboardPage = lazy(() => import("./pages/FinanceDashboardPage.tsx"));
const FinanceAccountsPage = lazy(() => import("./pages/FinanceAccountsPage.tsx"));
const BookingDetailPage = lazy(() => import("./pages/BookingDetailPage.tsx"));
const ReceiptPage = lazy(() => import("./pages/ReceiptPage.tsx"));
const ProjectDetailPage = lazy(() => import("./pages/ProjectDetailPage.tsx"));
const ProjectsPage = lazy(() => import("./pages/ProjectsPage.tsx"));
const UnitDetailPage = lazy(() => import("./pages/UnitDetailPage.tsx"));
const MyProjectsPage = lazy(() => import("./pages/MyProjectsPage.tsx"));
const MyProjectDetailPage = lazy(() => import("./pages/MyProjectDetailPage.tsx"));
const LeadsPage = lazy(() => import("./pages/LeadsPage.tsx"));
const LeadDetailPage = lazy(() => import("./pages/LeadDetailPage.tsx"));
const CrmSettingsPage = lazy(() => import("./pages/CrmSettingsPage.tsx"));
const NotificationsPage = lazy(() => import("./pages/NotificationsPage.tsx"));
const NotificationAdminPage = lazy(() => import("./pages/NotificationAdminPage.tsx"));

export interface User {
  userId: string;
  email: string;
  role: string;
}

const NAV_LINKS = [
  { to: "/", label: "Home" },
  { to: "/projects", label: "Projects" },
  { to: "/about", label: "About" },
  { to: "/contact", label: "Contact" },
];

const AUTH_SESSION_EVENT = "dams-auth-session";

function publishAuthSession(type: "login" | "logout") {
  try {
    localStorage.setItem(AUTH_SESSION_EVENT, JSON.stringify({ type, at: Date.now() }));
  } catch {
    // Session changes still apply in this tab when storage is unavailable.
  }
}

function App() {
  const [modal, setModal] = useState<null | "login" | "signup">(null);
  const [user, setUser] = useState<User | null>(null);

  const displayName = useMemo(
    () => (user?.email ? user.email.split("@")[0] : "User"),
    [user]
  );
  const displayInitial = displayName[0]?.toUpperCase() ?? "U";

  const mainNavLinks = useMemo(() => {
    if (user?.role === "Admin") {
      return [
        ...NAV_LINKS,
        { to: "/crm", label: "Lead CRM" },
        { to: "/bookings", label: "Requests" },
        { to: "/confirmed-bookings", label: "Bookings" },
        { to: "/customers", label: "Customers" },
        { to: "/customer-document-categories", label: "Document Setup" },
        { to: "/employees", label: "Employees" },
        { to: "/finance", label: "Finance" },
        { to: "/notifications/settings", label: "Notifications" },
      ];
    }

    if (user?.role === "Manager" || user?.role === "Employee") {
      return [...NAV_LINKS, { to: "/crm", label: "Lead CRM" }];
    }

    if (user) {
      return [...NAV_LINKS, { to: "/my-projects", label: "My Projects" }];
    }

    return NAV_LINKS;
  }, [user]);

  const fetchProfile = async () => {
    const res = await api("/api/Auth/profile");
    if (!res.ok) {
      setAccessToken(null);
      setUser(null);
      return;
    }
    const data = await res.json();
    setUser(data);
  };

  useEffect(() => {
    // On page load, try to silently restore the session using the httpOnly refresh cookie.
    // If the cookie is absent or expired, the user stays logged out.
    void refreshAccessToken().then((ok) => {
      if (!ok) return;
      void api("/api/Auth/profile").then(async (res) => {
        if (res.ok) setUser(await res.json());
      });
    });
  }, []);

  useEffect(() => {
    const onStorage = (event: StorageEvent) => {
      if (event.key !== AUTH_SESSION_EVENT || !event.newValue) return;

      let payload: { type?: string };
      try {
        payload = JSON.parse(event.newValue) as { type?: string };
      } catch {
        return;
      }
      if (payload.type === "logout") {
        setAccessToken(null);
        setUser(null);
        return;
      }

      if (payload.type === "login") {
        void refreshAccessToken().then((ok) => {
          if (!ok) return;
          void api("/api/Auth/profile").then(async (res) => {
            if (res.ok) setUser(await res.json());
          });
        });
      }
    };

    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }, []);

  const logout = async () => {
    // Detach this browser's push subscription first, while the session is still valid.
    // On a shared computer that is what stops the next person from receiving the previous
    // user's notifications.
    await detachPushOnLogout();
    void api("/api/Auth/logout", { method: "POST" }, false);
    setAccessToken(null);
    setUser(null);
    publishAuthSession("logout");
  };

  return (
    <ProjectsProvider>
      <Suspense fallback={<div className="flex min-h-screen items-center justify-center text-sm text-[var(--app-text-muted)]">Loading…</div>}>
        <Routes>
          <Route
            element={
              <AppLayout
                user={user}
                mainNavLinks={mainNavLinks}
                displayName={displayName}
                displayInitial={displayInitial}
                setModal={setModal}
                onLogout={() => void logout()}
              />
            }
          >
            <Route path="/" element={<HomePage />} />
            <Route path="/landing" element={<LandingPage />} />
            <Route path="/projects" element={<ProjectsPage user={user} />} />
            <Route path="/projects/:id" element={<ProjectDetailPage user={user} />} />
            <Route path="/units/:id" element={<UnitDetailPage user={user} />} />
            <Route path="/about" element={<AboutPage />} />
            <Route path="/contact" element={<ContactPage />} />
            <Route path="/my-projects" element={<MyProjectsPage user={user} />} />
            <Route path="/my-projects/:id" element={<MyProjectDetailPage user={user} />} />
            <Route path="/bookings" element={<BookingRequestsPage user={user} />} />
            <Route path="/confirmed-bookings" element={<ConfirmedBookingsPage user={user} />} />
            <Route path="/confirmed-bookings/new" element={<CreateBookingPage user={user} />} />
            <Route path="/confirmed-bookings/:id" element={<BookingDetailPage user={user} />} />
            <Route path="/application-form" element={<ApplicationFormPage user={user} />} />
            <Route path="/receipt/:bookingId/:paymentId" element={<ReceiptPage user={user} />} />
            <Route path="/customers" element={<CustomersPage user={user} />} />
            <Route path="/customers/:id" element={<CustomerDetailPage user={user} />} />
            <Route path="/customer-document-categories" element={<CustomerDocumentCategoriesPage user={user} />} />
            <Route path="/employees" element={<EmployeesPage user={user} />} />
            <Route path="/employees/:id" element={<EmployeeDetailPage user={user} />} />
            <Route path="/finance" element={<FinanceDashboardPage user={user} />} />
            <Route path="/finance/accounts" element={<FinanceAccountsPage user={user} />} />
            <Route path="/crm" element={<LeadsPage user={user} />} />
            <Route path="/crm/leads/:id" element={<LeadDetailPage user={user} />} />
            <Route path="/crm/settings" element={<CrmSettingsPage user={user} />} />
            <Route path="/notifications" element={<NotificationsPage user={user} />} />
            <Route path="/notifications/settings" element={<NotificationAdminPage user={user} />} />
          </Route>
        </Routes>
      </Suspense>

      {/* ─── Auth Modal ─── */}
      {modal && (
        <AuthModal
          mode={modal}
          onClose={() => {
            setModal(null);
          }}
          onSuccess={() => {
            void fetchProfile().then(() => publishAuthSession("login"));
          }}
        />
      )}
    </ProjectsProvider>
  );
}

export default App;
