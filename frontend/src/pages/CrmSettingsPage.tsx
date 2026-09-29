import { useCallback, useEffect, useState } from "react";
import { useLocation } from "react-router-dom";
import type { User } from "../App.tsx";
import {
  Avatar,
  Button,
  ConfirmDialog,
  DataTable,
  Dropdown,
  EmptyState,
  IconPlus,
  IconSettings,
  Modal,
  PageHeader,
  StatusBadge,
  Tabs,
  TextField,
  useIsPhone,
  useToast,
  type DataTableColumn,
} from "../components/ui";
import { roleLabel } from "../features/access/permissions.ts";
import { apiJson, forgetCrmLookups, jsonRequest } from "../features/leads/leadApi.ts";
import type { StaffAccount, StaffAccountProvisionResult, StaffInvitationResult } from "../features/leads/types.ts";
import {
  accessBadgeTone,
  accessLabel,
  accountsWithLogin,
  applyLoginSelection,
  buildProvisionPayload,
  buildUpdatePayload,
  describeProvisionOutcome,
  describeResendOutcome,
  inviteReady,
  isExistingLoginError,
  isOwnAccount,
  loginEmailIsReadOnly,
  newManageForm,
  newProvisionForm,
  provisionableEmployees,
  provisionSubmitLabel,
  roleFilterOptions,
} from "../features/staff/staffAccessState.ts";
import type { LinkableLogin, Notice } from "../features/staff/staffAccessState.ts";
import { canActOnAccount, canOpenCrmSettings, grantableRoles } from "../features/staff/staffRolePermissions.ts";
import MetaIntegrationsPanel from "../features/integrations/MetaIntegrationsPanel.tsx";

type Props = { user: User | null };
const PAGE = "mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7";
const NEW_EMPLOYEE = "new";
const EXISTING_LOGIN_FIELD = "existing-login";

export default function CrmSettingsPage({ user }: Props) {
  if (!user || !canOpenCrmSettings(user.role)) {
    return (
      <div className={PAGE}>
        <EmptyState
          icon={<IconSettings size={26} />}
          title={user ? "Admin or Sales manager access required" : "Sign in required"}
          message={user ? "Only an Admin or Sales manager can manage staff accounts and integrations." : "Sign in with a staff account to open CRM settings."}
        />
      </div>
    );
  }
  return <SettingsWorkspace user={user} />;
}

function SettingsWorkspace({ user }: { user: User }) {
  const route = useLocation();
  const isPhone = useIsPhone();
  const [tab, setTab] = useState(() => initialTab(window.location.search));
  const [seen, setSeen] = useState(route.key);
  const [giving, setGiving] = useState(false);
  const [staffReady, setStaffReady] = useState(false);
  if (route.key !== seen) {
    setSeen(route.key);
    if (new URLSearchParams(route.search).get("tab") === "integrations") setTab("integrations");
  }

  return (
    <div className={PAGE}>
      <PageHeader
        back={{ to: "/crm", label: "Lead CRM" }}
        title="CRM settings"
        actions={tab === "staff" ? <GiveAccessButton isPhone={isPhone} disabled={!staffReady} onClick={() => setGiving(true)} /> : undefined}
      />
      <StaffAndIntegrations user={user} tab={tab} onTab={setTab} giving={giving} onGiving={setGiving} onStaffReady={setStaffReady} />
    </div>
  );
}

function initialTab(search: string) {
  const params = new URLSearchParams(search);
  return params.get("tab") === "integrations" || params.has("meta") ? "integrations" : "staff";
}

/**
 * The Give access button lives in the page header, but the dialog and the staff list live
 * together so a successful invite can reload the same rows the button just changed.
 */
function StaffAndIntegrations({ user, tab, onTab, giving, onGiving, onStaffReady }: {
  user: User;
  tab: string;
  onTab: (tab: string) => void;
  giving: boolean;
  onGiving: (open: boolean) => void;
  onStaffReady: (ready: boolean) => void;
}) {
  const toast = useToast();
  const [staff, setStaff] = useState<StaffAccount[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [role, setRole] = useState("");
  const [managing, setManaging] = useState<StaffAccount | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setStaff(await apiJson<StaffAccount[]>("/api/staff/accounts"));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Staff accounts could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => { if (!loading && error === null) onStaffReady(true); }, [error, loading, onStaffReady]);

  const logins = accountsWithLogin(staff);
  const shown = role ? logins.filter((account) => account.role === role) : logins;
  const finish = async (outcome: Notice) => {
    if (outcome.tone === "error") toast.error(outcome.message);
    else toast.success(outcome.message);
    forgetCrmLookups();
    await load();
  };

  return (
    <>
      <Tabs
        aria-label="CRM settings"
        value={tab}
        onChange={onTab}
        items={[{ id: "staff", label: "Staff accounts", count: logins.length }, { id: "integrations", label: "Integrations" }]}
      />
      {tab === "integrations" ? <MetaIntegrationsPanel /> : (
        <StaffAccounts
          user={user}
          loading={loading}
          error={error}
          role={role}
          options={roleFilterOptions(logins)}
          rows={shown}
          onRole={setRole}
          onRetry={() => void load()}
          onManage={setManaging}
        />
      )}
      {giving && (
        <GiveAccessDialog
          actorRole={user.role}
          staff={provisionableEmployees(staff)}
          onClose={() => onGiving(false)}
          onDone={async (outcome) => { onGiving(false); await finish(outcome); }}
        />
      )}
      {managing && (
        <ManageAccountDialog
          actorRole={user.role}
          actorUserId={user.userId}
          account={managing}
          onClose={() => setManaging(null)}
          onDone={async (outcome) => { setManaging(null); await finish(outcome); }}
        />
      )}
    </>
  );
}

function GiveAccessButton({ isPhone, disabled, onClick }: { isPhone: boolean; disabled?: boolean; onClick: () => void }) {
  return (
    <Button icon={<IconPlus size={16} />} disabled={disabled} onClick={onClick}>
      {isPhone ? "Give access" : "Give DAMS access"}
    </Button>
  );
}

function StaffAccounts({ user, loading, error, role, options, rows, onRole, onRetry, onManage }: {
  user: User;
  loading: boolean;
  error: string | null;
  role: string;
  options: { value: string; label: string }[];
  rows: StaffAccount[];
  onRole: (role: string) => void;
  onRetry: () => void;
  onManage: (account: StaffAccount) => void;
}) {
  if (loading && rows.length === 0) {
    return <div className="flex flex-col gap-2.5">{Array.from({ length: 4 }, (_, index) => <div key={index} className="h-16 animate-pulse rounded-card bg-track" />)}</div>;
  }
  if (error && rows.length === 0) {
    return <EmptyState icon={<IconSettings size={26} />} title="Staff accounts could not be loaded" action={<Button variant="outline" onClick={onRetry}>Try again</Button>} />;
  }

  const columns: DataTableColumn<StaffAccount>[] = [
    {
      key: "name",
      header: "Name",
      render: (account) => (
        <span className="flex items-center gap-3">
          <Avatar name={account.fullName} size={36} />
          <span className="min-w-0">
            <span className="block font-extrabold text-ink">{account.fullName}</span>
            {account.jobTitle && <span className="block text-small text-ink-muted">{account.jobTitle}</span>}
          </span>
        </span>
      ),
    },
    { key: "email", header: "Login email", render: (account) => account.email ?? "—" },
    { key: "role", header: "Role", className: "font-bold", render: (account) => roleLabel(account.role) },
    {
      key: "access",
      header: "Access",
      render: (account) => <StatusBadge status={accessLabel(account.access)} tone={accessBadgeTone(account.access)} />,
    },
    {
      key: "manage",
      header: "",
      align: "right",
      render: (account) => canActOnAccount(user.role, account.role)
        ? <Button variant="outline" size="sm" onClick={(event) => { event.stopPropagation(); onManage(account); }}>Manage</Button>
        : null,
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <Dropdown size="filter" label="Role" aria-label="Role" value={role} onChange={onRole} options={options} className="md:max-w-xs" />
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(account) => account.employeeId}
        onRowClick={(account) => { if (canActOnAccount(user.role, account.role)) onManage(account); }}
        rowLabel={(account) => `Manage ${account.fullName}`}
        minWidth={760}
        empty={<EmptyState icon={<IconSettings size={26} />} title={role ? "No staff accounts for this role" : "No staff accounts yet"} />}
        phoneCard={(account) => <StaffCard account={account} onManage={canActOnAccount(user.role, account.role) ? () => onManage(account) : undefined} />}
      />
    </div>
  );
}

function StaffCard({ account, onManage }: { account: StaffAccount; onManage?: () => void }) {
  return (
    <div className="rounded-card border border-line bg-card p-4">
      <div className="flex items-start gap-3">
        <Avatar name={account.fullName} size={40} />
        <div className="min-w-0 flex-1">
          <div className="flex items-start justify-between gap-2">
            <div className="min-w-0">
              <p className="m-0 truncate text-body font-extrabold text-ink">{account.fullName}</p>
              <p className="m-0 truncate text-small text-ink-muted">{roleLabel(account.role)}{account.jobTitle ? ` · ${account.jobTitle}` : ""}</p>
            </div>
            <StatusBadge status={accessLabel(account.access)} tone={accessBadgeTone(account.access)} />
          </div>
          <div className="mt-3 flex items-center justify-between gap-3">
            <p className="m-0 min-w-0 truncate text-small text-ink-2">{account.email ?? "—"}</p>
            {onManage && <Button variant="outline" size="sm" onClick={onManage}>Manage</Button>}
          </div>
        </div>
      </div>
    </div>
  );
}

function GiveAccessDialog({ actorRole, staff, onClose, onDone }: {
  actorRole: string;
  staff: StaffAccount[];
  onClose: () => void;
  onDone: (outcome: Notice) => Promise<void>;
}) {
  const toast = useToast();
  const [choice, setChoice] = useState("");
  const [form, setForm] = useState(() => newProvisionForm(null));
  const [login, setLogin] = useState<LinkableLogin | null>(null);
  const [logins, setLogins] = useState<LinkableLogin[]>([]);
  const [loginsError, setLoginsError] = useState<string | null>(null);
  const [emailTaken, setEmailTaken] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const creating = choice === NEW_EMPLOYEE;
  const locked = staff.find((employee) => String(employee.employeeId) === choice) ?? null;
  const emailLocked = loginEmailIsReadOnly(form, false);
  const ready = inviteReady({ employeeId: choice, fullName: form.fullName, email: form.email, phone: form.phone, role: form.role });
  const phoneError = creating && form.phone.trim().length > 0 && form.phone.trim().length < 7 ? "At least 7 characters." : undefined;

  useEffect(() => {
    let cancelled = false;
    apiJson<LinkableLogin[]>("/api/staff/linkable-users")
      .then((rows) => { if (!cancelled) setLogins(rows); })
      .catch((caught) => {
        if (!cancelled) setLoginsError(caught instanceof Error ? caught.message : "Existing logins could not be loaded.");
      });
    return () => { cancelled = true; };
  }, []);

  const pickEmployee = (value: string) => {
    setChoice(value);
    setEmailTaken(null);
    const next = value === NEW_EMPLOYEE ? null : staff.find((employee) => String(employee.employeeId) === value) ?? null;
    setForm((current) => applyLoginSelection(
      { ...newProvisionForm(next), role: current.role },
      login,
      next,
    ));
  };

  const pickLogin = (userId: string) => {
    setEmailTaken(null);
    const chosen = logins.find((row) => String(row.userId) === userId) ?? null;
    setLogin(chosen);
    setForm((current) => applyLoginSelection(current, chosen, locked));
  };

  const save = async () => {
    setSaving(true);
    setEmailTaken(null);
    try {
      const result = await apiJson<StaffAccountProvisionResult>("/api/staff/accounts", jsonRequest("POST", buildProvisionPayload(form, locked)));
      await onDone(describeProvisionOutcome(result));
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "The invitation could not be sent.";
      if (isExistingLoginError(message)) setEmailTaken(message);
      else toast.error(message);
      setSaving(false);
    }
  };

  const options = [
    ...staff.map((employee) => ({
      value: String(employee.employeeId),
      label: employee.jobTitle ? `${employee.fullName} — ${employee.jobTitle}` : employee.fullName,
    })),
    { value: NEW_EMPLOYEE, label: "+ Someone new" },
  ];

  const loginOptions = [
    { value: "", label: "None — send a new invite" },
    ...logins.map((row) => ({ value: String(row.userId), label: `${row.fullName} — ${row.email}` })),
  ];

  return (
    <Modal
      open
      onClose={onClose}
      title="Give DAMS access"
      size="md"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: provisionSubmitLabel(login), onClick: () => void save(), disabled: !ready, loading: saving }}
    >
      <div className="flex flex-col gap-4">
        <Dropdown label="Employee" required value={choice} onChange={pickEmployee} options={options} placeholder="Select an employee" />
        <Dropdown
          id={EXISTING_LOGIN_FIELD}
          label="Existing login"
          searchable
          value={form.existingUserId}
          onChange={pickLogin}
          options={loginOptions}
          placeholder="None — send a new invite"
          helper={loginsError ?? undefined}
        />
        {creating && (
          <>
            <TextField label="Full name" required value={form.fullName} onChange={(event) => setForm({ ...form, fullName: event.target.value })} />
            <TextField label="Phone" required value={form.phone} error={phoneError} onChange={(event) => setForm({ ...form, phone: event.target.value })} />
          </>
        )}
        <TextField
          label="Login email"
          required
          type="email"
          value={form.email}
          readOnly={emailLocked}
          disabled={emailLocked}
          error={emailTaken ? (
            <>
              {emailTaken}{" "}
              <button type="button" className="cursor-pointer underline" onClick={() => document.getElementById(EXISTING_LOGIN_FIELD)?.focus()}>
                Choose the existing login
              </button>
            </>
          ) : undefined}
          onChange={(event) => { setEmailTaken(null); setForm({ ...form, email: event.target.value }); }}
        />
        <Dropdown
          label="Role"
          required
          value={form.role}
          onChange={(value) => setForm({ ...form, role: value })}
          options={grantableRoles(actorRole).map(([value, label]) => ({ value, label }))}
        />
      </div>
    </Modal>
  );
}

function ManageAccountDialog({ actorRole, actorUserId, account, onClose, onDone }: {
  actorRole: string;
  actorUserId: string;
  account: StaffAccount;
  onClose: () => void;
  onDone: (outcome: Notice) => Promise<void>;
}) {
  const toast = useToast();
  const [role, setRole] = useState(account.role ?? "Employee");
  const [saving, setSaving] = useState(false);
  const [accessBusy, setAccessBusy] = useState(false);
  const [confirmOff, setConfirmOff] = useState(false);
  const own = isOwnAccount(actorUserId, account);
  const busy = saving || accessBusy;

  const save = async () => {
    if (role === account.role) {
      onClose();
      return;
    }
    setSaving(true);
    try {
      await apiJson(`/api/staff/accounts/${account.employeeId}`, jsonRequest("PUT", buildUpdatePayload({ ...newManageForm(account), role })));
      await onDone({ tone: "success", message: `Saved role for ${account.fullName}.` });
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "The role could not be saved.");
      setSaving(false);
    }
  };

  const runAccess = async (path: string, success: Notice | ((result: StaffInvitationResult) => Notice)) => {
    setAccessBusy(true);
    try {
      const result = path.endsWith("resend-invitation")
        ? await apiJson<StaffInvitationResult>(`/api/staff/accounts/${account.employeeId}/${path}`, { method: "POST" })
        : await apiJson(`/api/staff/accounts/${account.employeeId}/${path}`, { method: "POST" });
      const outcome = typeof success === "function" ? success(result as StaffInvitationResult) : success;
      await onDone(outcome);
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "DAMS access could not be changed.");
      setAccessBusy(false);
    }
  };

  return (
    <>
      <Modal
        open
        onClose={onClose}
        title="Manage staff account"
        size="md"
        phoneLayout="popup"
        busy={busy}
        primaryAction={{ label: "Save changes", onClick: () => void save(), disabled: role === (account.role ?? ""), loading: saving }}
      >
        <div className="flex flex-col gap-4">
          <TextField label="Full name" required disabled readOnly value={account.fullName} />
          <TextField label="Login email" required disabled readOnly value={account.email ?? ""} />
          <Dropdown
            label="Role"
            required
            value={role}
            onChange={setRole}
            options={grantableRoles(actorRole).map(([value, label]) => ({ value, label }))}
          />
          <div className="flex items-center justify-between gap-3 rounded-field border border-line-soft bg-page px-3 py-3">
            <div className="min-w-0">
              <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">DAMS access</p>
              <div className="mt-1.5">
                <StatusBadge status={accessLabel(account.access)} tone={accessBadgeTone(account.access)} />
              </div>
            </div>
            {account.access === "Active" && !own && (
              <Button variant="danger" size="sm" disabled={busy} onClick={() => setConfirmOff(true)}>Turn off access</Button>
            )}
            {account.access === "Disabled" && (
              <Button variant="outline" size="sm" loading={accessBusy} onClick={() => void runAccess("enable-access", { tone: "success", message: `Access turned on for ${account.fullName}.` })}>Turn on access</Button>
            )}
            {account.access === "Invited" && (
              <Button variant="outline" size="sm" loading={accessBusy} onClick={() => void runAccess("resend-invitation", describeResendOutcome)}>Resend invite</Button>
            )}
          </div>
        </div>
      </Modal>
      <ConfirmDialog
        open={confirmOff}
        danger
        loading={accessBusy}
        title="Turn off access?"
        message={`${account.fullName} will not be able to sign in until access is turned on again.`}
        confirmLabel="Turn off access"
        onClose={() => setConfirmOff(false)}
        onConfirm={() => {
          setConfirmOff(false);
          void runAccess("disable-access", { tone: "success", message: `Access turned off for ${account.fullName}.` });
        }}
      />
    </>
  );
}
