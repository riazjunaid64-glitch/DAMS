import { lazy, Suspense, useEffect, useMemo, useState } from "react";
import { Route, Routes } from "react-router-dom";
import { api, refreshAccessToken, setAccessToken } from "./api/api";
import AuthModal from "./components/AuthModal.tsx";
import { ProjectsProvider } from "./contexts/ProjectsContext.tsx";
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

function App() {
  const [modal, setModal] = useState<null | "login" | "signup">(null);
  const [user, setUser] = useState<User | null>(null);

  const mainNavLinks = useMemo(() => {
    if (user?.role === "Admin") {
      return [
        ...NAV_LINKS,
        { to: "/bookings", label: "Requests" },
        { to: "/confirmed-bookings", label: "Bookings" },
        { to: "/customers", label: "Customers" },
        { to: "/employees", label: "Employees" },
        { to: "/finance", label: "Finance" },
      ];
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

  const logout = () => {
    void api("/api/Auth/logout", { method: "POST" }, false);
    setAccessToken(null);
    setUser(null);
  };

  return (
    <ProjectsProvider>
      <Suspense fallback={<div className="flex min-h-[50vh] items-center justify-center text-sm text-[var(--text-muted)]">Loading…</div>}>
        <Routes>
          {/* Receipt is a standalone print/PDF page — no sidebar chrome */}
          <Route path="/receipt/:bookingId/:paymentId" element={<ReceiptPage user={user} />} />

          <Route
            element={
              <AppLayout
                user={user}
                mainNavLinks={mainNavLinks}
                setModal={setModal}
                logout={logout}
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
            <Route path="/customers" element={<CustomersPage user={user} />} />
            <Route path="/customers/:id" element={<CustomerDetailPage user={user} />} />
            <Route path="/employees" element={<EmployeesPage user={user} />} />
            <Route path="/employees/:id" element={<EmployeeDetailPage user={user} />} />
            <Route path="/finance" element={<FinanceDashboardPage user={user} />} />
            <Route path="/finance/accounts" element={<FinanceAccountsPage user={user} />} />
          </Route>
        </Routes>
      </Suspense>

      {/* ─── Auth Modal ─── */}
      {modal && (
        <AuthModal
          mode={modal}
          onClose={() => {
            setModal(null);
            fetchProfile();
          }}
          onSuccess={() => {}}
        />
      )}
    </ProjectsProvider>
  );
}

export default App;
