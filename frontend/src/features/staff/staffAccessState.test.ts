import { describe, expect, it } from "vitest";
import type {
  StaffAccount,
  StaffAccountProvisionResult,
  StaffInvitationResult,
} from "../leads/types.ts";
import {
  accessLabel,
  buildProvisionPayload,
  buildUpdatePayload,
  canManageAccount,
  canProvisionAccess,
  canResendInvitation,
  describeProvisionOutcome,
  describeResendOutcome,
  invitationState,
  invitationSummary,
  newManageForm,
  newProvisionForm,
  parseServerDateTime,
  provisionableEmployees,
} from "./staffAccessState.ts";

const account = (overrides: Partial<StaffAccount> = {}): StaffAccount => ({
  employeeId: 1,
  userId: 7,
  fullName: "Sana Sales",
  email: "sana@dams.test",
  role: "Employee",
  status: "Active",
  canOwnLeads: true,
  jobTitle: "Sales Executive",
  department: "Sales",
  phone: "03001234567",
  joinDate: "2026-01-01T00:00:00",
  access: "Active",
  ...overrides,
});

const provision = (overrides: Partial<StaffAccountProvisionResult> = {}): StaffAccountProvisionResult => ({
  account: account({ access: "Invited" }),
  invitationRequired: true,
  invitationSent: true,
  ...overrides,
});

const resend = (overrides: Partial<StaffInvitationResult> = {}): StaffInvitationResult => ({
  issued: true,
  emailSent: true,
  ...overrides,
});

describe("access presentation", () => {
  it("says an employee has no account rather than showing an empty state", () => {
    expect(accessLabel("None")).toBe("No account");
    expect(accessLabel("Invited")).toBe("Invited");
    expect(accessLabel("Active")).toBe("Active");
    expect(accessLabel("Disabled")).toBe("Disabled");
  });

  it("keeps employment status and DAMS access as separate facts", () => {
    // The case that makes the two columns worth having: employed and working, but has not
    // activated a login yet.
    const waiting = account({ status: "Active", access: "Invited" });
    expect(waiting.status).toBe("Active");
    expect(accessLabel(waiting.access)).toBe("Invited");
  });
});

describe("row actions", () => {
  it("offers provisioning, never the update flow, for an employee with no login", () => {
    // A PUT here has nothing to update and the backend answers "This employee has no login
    // account." Provisioning is a POST, which is what the row must lead to.
    expect(canProvisionAccess("None")).toBe(true);
    expect(canManageAccount("None")).toBe(false);
  });

  it("offers the update flow for every access state that has a login behind it", () => {
    for (const access of ["Invited", "Active", "Disabled"] as const) {
      expect(canManageAccount(access)).toBe(true);
      expect(canProvisionAccess(access)).toBe(false);
    }
  });

  it("offers a resend only while an invitation is actually outstanding", () => {
    expect(canResendInvitation("Invited")).toBe(true);
    // An activated login needs no link, and a disabled one must not be handed one.
    expect(canResendInvitation("Active")).toBe(false);
    expect(canResendInvitation("Disabled")).toBe(false);
    expect(canResendInvitation("None")).toBe(false);
  });

  it("lists only employees without a login as provisioning targets", () => {
    const rows = [
      account({ employeeId: 1, access: "None", userId: null }),
      account({ employeeId: 2, access: "Invited" }),
      account({ employeeId: 3, access: "Active" }),
    ];
    expect(provisionableEmployees(rows).map((r) => r.employeeId)).toEqual([1]);
  });
});

describe("invitation expiry", () => {
  const now = new Date("2026-08-22T12:00:00Z");

  it("reads a timezone-less server value as UTC rather than local time", () => {
    // SQL Server drops the offset, so this arrives without a Z. Parsed as local time in
    // Pakistan it would land five hours early and a live link would read as expired.
    expect(parseServerDateTime("2026-08-29T14:23:11")?.toISOString())
      .toBe("2026-08-29T14:23:11.000Z");
    expect(parseServerDateTime("2026-08-29T14:23:11Z")?.toISOString())
      .toBe("2026-08-29T14:23:11.000Z");
    expect(parseServerDateTime("2026-08-29T19:23:11+05:00")?.toISOString())
      .toBe("2026-08-29T14:23:11.000Z");
    expect(parseServerDateTime(null)).toBeNull();
    expect(parseServerDateTime("not a date")).toBeNull();
  });

  it("describes a link that is still good", () => {
    const state = invitationState(
      account({ access: "Invited", invitationExpiresAt: "2026-08-29T14:00:00" }), now);
    expect(state.kind).toBe("pending");
    expect(invitationSummary(state)).toContain("Activation link expires");
  });

  it("presents a link whose moment has passed as expired", () => {
    const state = invitationState(
      account({ access: "Invited", invitationExpiresAt: "2026-08-15T14:00:00" }), now);
    expect(state.kind).toBe("expired");
    expect(invitationSummary(state)).toBe("Invitation expired — resend to send a new link");
  });

  it("says nothing about expiry for an account that is not waiting on one", () => {
    for (const access of ["None", "Active", "Disabled"] as const) {
      const state = invitationState(
        account({ access, invitationExpiresAt: "2026-08-15T14:00:00" }), now);
      expect(state).toEqual({ kind: "none" });
      expect(invitationSummary(state)).toBeNull();
    }
  });

  it("handles an invited account with no outstanding link", () => {
    const state = invitationState(account({ access: "Invited", invitationExpiresAt: null }), now);
    expect(state.kind).toBe("unknown");
    expect(invitationSummary(state)).toContain("resend");
  });
});

describe("describeProvisionOutcome", () => {
  it("reports a linked existing login without implying an email went out", () => {
    const notice = describeProvisionOutcome(provision({ invitationRequired: false, invitationSent: false }));
    expect(notice.tone).toBe("success");
    expect(notice.message).toContain("keeps its current password");
  });

  it("confirms the activation email when it was actually sent", () => {
    const notice = describeProvisionOutcome(provision());
    expect(notice.tone).toBe("success");
    expect(notice.message).toContain("activation email has been sent");
  });

  it("treats a failed email as an account that exists, not a failed create", () => {
    // This is the outcome that causes duplicate accounts if it is reported as an error: the
    // login was created, so pressing Create again would make a second one.
    const notice = describeProvisionOutcome(provision({
      invitationSent: false,
      invitationError: "The mail server refused the message.",
    }));
    expect(notice.tone).toBe("warning");
    expect(notice.message).toContain("DAMS access was created");
    expect(notice.message).toContain("The mail server refused the message.");
    expect(notice.message).toContain("Resend invitation");
  });

  it("still explains itself when the backend gave no reason", () => {
    const notice = describeProvisionOutcome(provision({ invitationSent: false }));
    expect(notice.tone).toBe("warning");
    expect(notice.message).not.toContain("undefined");
  });
});

describe("describeResendOutcome", () => {
  it("confirms a sent invitation and that the old link died with it", () => {
    const notice = describeResendOutcome(resend());
    expect(notice.tone).toBe("success");
    expect(notice.message).toContain("previous link no longer works");
  });

  it("warns when the link was replaced but the email failed", () => {
    // HTTP 200 with emailSent false. The employee is now worse off than before — the old
    // link is dead too — so this cannot read as a plain success.
    const notice = describeResendOutcome(resend({
      emailSent: false,
      error: "SMTP is not configured.",
    }));
    expect(notice.tone).toBe("warning");
    expect(notice.message).toContain("SMTP is not configured.");
    expect(notice.message).toContain("previous link no longer works");
  });

  it("reports a refused invitation as an error", () => {
    const notice = describeResendOutcome(resend({
      issued: false,
      emailSent: false,
      error: "This employee has already activated their account.",
    }));
    expect(notice.tone).toBe("error");
    expect(notice.message).toBe("This employee has already activated their account.");
  });
});

describe("request payloads", () => {
  const form = newProvisionForm(account({ employeeId: 4, access: "None", userId: null }));

  it("has no password field anywhere in the form", () => {
    const fields = [...Object.keys(newProvisionForm(null)), ...Object.keys(newManageForm(account()))];
    expect(fields.filter((key) => /password/i.test(key))).toEqual([]);
  });

  it("sends no password when provisioning", () => {
    const payload = buildProvisionPayload(form);
    expect(Object.keys(payload).filter((key) => /password/i.test(key))).toEqual([]);
    expect(JSON.stringify(payload).toLowerCase()).not.toContain("password");
  });

  it("sends exactly the fields the create endpoint accepts and nothing else", () => {
    // The old code spread the whole form, which is how temporaryPassword reached the API.
    expect(Object.keys(buildProvisionPayload(form)).sort()).toEqual([
      "department", "email", "existingEmployeeId", "existingUserId", "fullName",
      "joinDate", "jobTitle", "phone", "role", "teamId",
    ].sort());
  });

  it("carries the chosen employee through as an id, not a name", () => {
    const payload = buildProvisionPayload(form);
    expect(payload.existingEmployeeId).toBe(4);
    expect(payload.existingUserId).toBeNull();
  });

  it("leaves ids null when nothing was chosen", () => {
    const payload = buildProvisionPayload(newProvisionForm(null));
    expect(payload.existingEmployeeId).toBeNull();
    expect(payload.existingUserId).toBeNull();
    expect(payload.teamId).toBeNull();
  });

  it("sends only role, team and employment status on update, with no password", () => {
    const payload = buildUpdatePayload(newManageForm(account({ role: "Manager", teamId: 3, status: "OnLeave" })));
    expect(payload).toEqual({ role: "Manager", teamId: 3, status: "OnLeave" });
    expect(JSON.stringify(payload).toLowerCase()).not.toContain("password");
  });

  it("keeps the established way of clearing a team", () => {
    const payload = buildUpdatePayload(newManageForm(account({ teamId: null })));
    expect(payload.teamId).toBe(-1);
  });
});
