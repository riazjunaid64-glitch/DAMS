import type {
  StaffAccount,
  StaffAccountAccess,
  StaffAccountProvisionResult,
  StaffInvitationResult,
} from "../leads/types.ts";

/**
 * The presentation logic behind the Admin staff-accounts screen, kept as pure functions.
 *
 * This project has no component-rendering test setup, so anything worth asserting lives here
 * rather than inside the page, where it would be untestable.
 *
 * Nothing here decides anything about security. Whether a link is still usable, whether a
 * linked login needs an invitation at all, and whether a token is genuine are questions only
 * the backend answers — these functions describe an answer it has already given.
 */

export type NoticeTone = "success" | "warning" | "error";

/** One piece of feedback for the Admin, after an action the page cannot undo. */
export interface Notice {
  tone: NoticeTone;
  message: string;
}

// ── Access presentation ───────────────────────────────────────────────────────

export function accessLabel(access: StaffAccountAccess): string {
  switch (access) {
    case "None":
      return "No account";
    case "Invited":
      return "Invited";
    case "Disabled":
      return "Disabled";
    default:
      return "Active";
  }
}

/** Tailwind classes per access state, so a login nobody can use is visible at a glance. */
export function accessTone(access: StaffAccountAccess): string {
  switch (access) {
    case "Active":
      return "border-emerald-500/25 bg-emerald-500/10 text-emerald-400";
    case "Invited":
      return "border-amber-500/25 bg-amber-500/10 text-amber-400";
    case "Disabled":
      return "border-rose-500/25 bg-rose-500/10 text-rose-400";
    default:
      return "border-slate-500/25 bg-slate-500/10 text-slate-400";
  }
}

// ── What a row may offer ──────────────────────────────────────────────────────

/**
 * An employee with no login has nothing for the update endpoint to update, so opening that
 * row in the Manage flow only ever produces "This employee has no login account." The row
 * offers provisioning instead.
 */
export function canProvisionAccess(access: StaffAccountAccess): boolean {
  return access === "None";
}

export function canManageAccount(access: StaffAccountAccess): boolean {
  return access !== "None";
}

/**
 * Only a waiting invitation can be resent. An Active login has already been activated, a
 * Disabled one must not be handed a way back in, and an employee with no account has no
 * invitation to replace.
 */
export function canResendInvitation(access: StaffAccountAccess): boolean {
  return access === "Invited";
}

/** The employees a brand-new login can be provisioned for: the ones without one. */
export function provisionableEmployees(staff: StaffAccount[]): StaffAccount[] {
  return staff.filter((member) => canProvisionAccess(member.access));
}

// ── Invitation expiry, for display only ───────────────────────────────────────

export type InvitationPresentation =
  | { kind: "none" }
  | { kind: "unknown" }
  | { kind: "pending"; expiresAt: Date }
  | { kind: "expired"; expiresAt: Date };

/**
 * SQL Server stores these without a timezone, so they reach the browser as
 * `2026-08-29T14:23:11` with no `Z`. Parsing that as local time would shift a UTC instant by
 * the browser's offset — five hours in Pakistan — which is enough to show a live invitation
 * as expired. A value that already carries an offset is left exactly as it is.
 */
export function parseServerDateTime(value?: string | null): Date | null {
  if (!value) return null;
  const trimmed = value.trim();
  const hasZone = /(z|[+-]\d{2}:?\d{2})$/i.test(trimmed);
  const parsed = new Date(hasZone ? trimmed : `${trimmed}Z`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/**
 * How the outstanding link should be described. Calling one "expired" here is a courtesy to
 * the Admin, not a decision: the backend alone accepts or refuses an activation, and a link
 * this says has expired is still refused by the backend rather than by the browser clock.
 */
export function invitationState(
  account: StaffAccount,
  now: Date = new Date(),
): InvitationPresentation {
  if (account.access !== "Invited") return { kind: "none" };

  const expiresAt = parseServerDateTime(account.invitationExpiresAt);
  if (expiresAt === null) return { kind: "unknown" };

  return expiresAt.getTime() > now.getTime()
    ? { kind: "pending", expiresAt }
    : { kind: "expired", expiresAt };
}

export function invitationSummary(state: InvitationPresentation): string | null {
  switch (state.kind) {
    case "pending":
      return `Activation link expires ${state.expiresAt.toLocaleString()}`;
    case "expired":
      return "Invitation expired — resend to send a new link";
    case "unknown":
      return "No activation link outstanding — resend to send one";
    default:
      return null;
  }
}

// ── Outcomes ──────────────────────────────────────────────────────────────────

/**
 * Provisioning has three endings, and conflating them is how an Admin ends up creating the
 * same person twice: the account exists in all three, so none of them is a failed create.
 */
export function describeProvisionOutcome(result: StaffAccountProvisionResult): Notice {
  if (!result.invitationRequired) {
    return {
      tone: "success",
      message: "DAMS access connected. The existing login stays active and keeps its current password.",
    };
  }

  if (result.invitationSent) {
    return {
      tone: "success",
      message: "DAMS access created and the activation email has been sent.",
    };
  }

  const detail = result.invitationError ? ` ${result.invitationError}` : "";
  return {
    tone: "warning",
    message:
      `DAMS access was created, but the activation email could not be sent.${detail}` +
      " The employee is now Invited — use Resend invitation rather than creating the account again.",
  };
}

/**
 * Resending has the same split. A stored invitation whose email failed still replaced the
 * previous link, so saying only "sending failed" would leave the Admin thinking the old link
 * still works.
 */
export function describeResendOutcome(result: StaffInvitationResult): Notice {
  if (result.emailSent) {
    return {
      tone: "success",
      message: "A new activation email was sent. The previous link no longer works.",
    };
  }

  if (result.issued) {
    const detail = result.error ? ` ${result.error}` : "";
    return {
      tone: "warning",
      message:
        `A new activation link was created, but the email could not be sent.${detail}` +
        " The previous link no longer works either, so this employee needs a successful resend.",
    };
  }

  return {
    tone: "error",
    message: result.error ?? "The invitation could not be sent.",
  };
}

// ── The form, and what may leave it ───────────────────────────────────────────

/**
 * Everything the staff modal edits. There is no password field and there is no place to add
 * one: an Admin never chooses, sees or resets another person's password.
 */
export interface StaffAccountForm {
  existingUserId: string;
  existingEmployeeId: string;
  fullName: string;
  email: string;
  role: string;
  teamId: string;
  jobTitle: string;
  department: string;
  phone: string;
  joinDate: string;
  status: string;
}

/** A fresh provisioning form, prefilled from the employee the Admin picked, if any. */
export function newProvisionForm(employee: StaffAccount | null): StaffAccountForm {
  return {
    existingUserId: "",
    existingEmployeeId: employee ? String(employee.employeeId) : "",
    fullName: employee?.fullName ?? "",
    email: employee?.email ?? "",
    role: "Employee",
    teamId: employee?.teamId?.toString() ?? "",
    jobTitle: employee?.jobTitle ?? "Sales Executive",
    department: employee?.department ?? "Sales",
    phone: employee?.phone ?? "",
    joinDate: employee?.joinDate?.slice(0, 10) ?? new Date().toISOString().slice(0, 10),
    status: employee?.status ?? "Active",
  };
}

export function newManageForm(account: StaffAccount): StaffAccountForm {
  return {
    ...newProvisionForm(account),
    role: account.role ?? "Employee",
  };
}

/**
 * Only the fields `POST /api/staff/accounts` accepts, named one by one. Spreading the whole
 * form is how a temporary-password field used to reach the backend; listing each field is
 * what stops the next piece of UI-only state doing the same.
 */
export function buildProvisionPayload(form: StaffAccountForm) {
  return {
    existingUserId: toId(form.existingUserId),
    existingEmployeeId: toId(form.existingEmployeeId),
    fullName: form.fullName.trim(),
    email: form.email.trim(),
    role: form.role,
    teamId: toId(form.teamId),
    jobTitle: form.jobTitle.trim(),
    department: form.department.trim(),
    phone: form.phone.trim(),
    joinDate: form.joinDate || null,
  };
}

/**
 * Role, team and employment status are the only things this endpoint changes. `-1` is how it
 * has always been told to remove team membership.
 */
export function buildUpdatePayload(form: StaffAccountForm) {
  return {
    role: form.role,
    teamId: form.teamId ? Number(form.teamId) : -1,
    status: form.status,
  };
}

function toId(value: string): number | null {
  const parsed = Number(value);
  return value !== "" && Number.isFinite(parsed) ? parsed : null;
}
