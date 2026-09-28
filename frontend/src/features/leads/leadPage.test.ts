import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  assignChoices,
  floorLabel,
  followUpDue,
  followUpGroups,
  isClosed,
  latestSubmission,
  platformLabel,
  quickDayOf,
  reminderAt,
  reopenBody,
  requirementDetail,
  timelineDays,
  timelineDot,
  unitChoices,
  unmappedAnswers,
  visitDayChoices,
  visitGroups,
  workerChoices,
} from "./leadPage.ts";
import type { ExternalSubmission, FollowUp, SiteVisit, StaffMember, TimelineItem, UnitLookup } from "./types.ts";

// 11:45 AM on Sunday, Sep 27, 2026 in Karachi.
const now = new Date("2026-09-27T06:45:00Z");

const followUp = (id: number, status: FollowUp["status"], dueAt: string, completedAt?: string) =>
  ({ id, status, dueAt, completedAt, title: `F${id}`, type: "Call", priority: "Medium", assignedEmployeeId: 1 }) as FollowUp;

const visit = (id: number, status: SiteVisit["status"], scheduledAt: string) =>
  ({ id, status, scheduledAt, meetingLocation: "Site office", assignedEmployeeId: 1 }) as SiteVisit;

describe("lead page wording", () => {
  it("describes the requirement and whether the lead is closed", () => {
    expect(requirementDetail({ paymentPreference: "NeedsDetails", purchaseIntent: "SelfUse" })).toBe("Needs details · Personal living");
    expect(requirementDetail({ paymentPreference: "Unknown", purchaseIntent: "Unknown" })).toBe("");
    expect(isClosed({ stageGroup: "New" })).toBe(false);
    expect(isClosed({ stageGroup: "Dormant" })).toBe(true);
  });

  it("reopens a never-contacted lead as New, which the server would otherwise refuse", () => {
    expect(reopenBody({ lastContactAt: "2026-09-20T10:00:00" })).toEqual({});
    expect(reopenBody({ lastContactAt: null })).toEqual({ stage: "New" });
  });
});

describe("follow-ups", () => {
  beforeEach(() => { vi.stubEnv("TZ", "UTC"); });
  afterEach(() => { vi.unstubAllEnvs(); });

  it("calls a passed or missed follow-up overdue", () => {
    expect(followUpDue(followUp(1, "Pending", "2026-09-26T12:00:00"), now)).toEqual({ text: "Overdue · Yesterday, 5:00 PM", overdue: true });
    expect(followUpDue(followUp(2, "Pending", "2026-09-28T11:00:00"), now)).toEqual({ text: "Tomorrow, 4:00 PM", overdue: false });
    expect(followUpDue(followUp(3, "Missed", "2026-09-28T11:00:00"), now).overdue).toBe(true);
  });

  it("splits to do from done and leaves cancelled out", () => {
    const { todo, done } = followUpGroups([
      followUp(1, "Pending", "2026-09-29T10:00:00"),
      followUp(2, "Missed", "2026-09-26T10:00:00"),
      followUp(3, "Cancelled", "2026-09-26T10:00:00"),
      followUp(4, "Completed", "2026-09-20T10:00:00", "2026-09-20T11:00:00"),
      followUp(5, "Completed", "2026-09-22T10:00:00", "2026-09-22T11:00:00"),
    ]);
    expect(todo.map((item) => item.id)).toEqual([2, 1]);
    expect(done.map((item) => item.id)).toEqual([5, 4]);
  });
});

describe("site visits", () => {
  it("puts scheduled visits ahead and completed or missed ones in the past", () => {
    const { upcoming, past } = visitGroups([
      visit(1, "Rescheduled", "2026-10-04T06:00:00"),
      visit(2, "Scheduled", "2026-10-03T06:00:00"),
      visit(3, "Completed", "2026-09-20T06:00:00"),
      visit(4, "Missed", "2026-09-25T06:00:00"),
      visit(5, "Cancelled", "2026-09-26T06:00:00"),
    ]);
    expect(upcoming.map((item) => item.id)).toEqual([2, 1]);
    expect(past.map((item) => item.id)).toEqual([4, 3]);
  });

  it("offers tomorrow and the coming weekend, each day once", () => {
    // Sunday: tomorrow is Monday, then Saturday Oct 3 and Sunday Oct 4.
    expect(visitDayChoices(now)).toEqual([
      { value: "2026-09-28", label: "Tomorrow" },
      { value: "2026-10-03", label: "Sat, Oct 3" },
      { value: "2026-10-04", label: "Sun, Oct 4" },
    ]);
    // Friday: tomorrow is the Saturday, so it is offered once.
    expect(visitDayChoices(new Date("2026-10-02T06:00:00Z"))).toEqual([
      { value: "2026-10-03", label: "Tomorrow" },
      { value: "2026-10-04", label: "Sun, Oct 4" },
    ]);
  });

  it("sets a reminder a set time before the visit, or leaves the default", () => {
    expect(reminderAt("2026-10-03T06:00:00.000Z", "3")).toBe("2026-10-03T03:00:00.000Z");
    expect(reminderAt("2026-10-03T06:00:00.000Z", "")).toBeNull();
  });

  it("recognises a quick day", () => {
    expect(quickDayOf("2026-09-28", now)).toBe("1");
    expect(quickDayOf("2026-10-04", now)).toBe("7");
    expect(quickDayOf("2026-10-05", now)).toBe("");
  });
});

describe("converting", () => {
  const unit = (id: number, number: string, type: string, floor: number, status = "Available") =>
    ({ id, projectId: 1, number, type, floor, status }) as UnitLookup;

  it("offers only available units, the lead's apartment type first", () => {
    const choices = unitChoices([
      unit(1, "101", "Studio", 1),
      unit(2, "504", "2 Bed", 5),
      unit(3, "203", "2 Bed", 2, "Booked"),
      unit(4, "12", "3 Bed", 0),
    ], "2 bed");
    expect(choices).toEqual([
      { value: "2", label: "Unit 504 — 2 Bed · 5th floor" },
      { value: "4", label: "Unit 12 — 3 Bed · Ground floor" },
      { value: "1", label: "Unit 101 — Studio · 1st floor" },
    ]);
  });

  it("names the floor as the project does", () => {
    const parking = { ...unit(5, "P-01", "Parking space", -1), floorName: "Parking" };
    expect(unitChoices([parking])).toEqual([{ value: "5", label: "Unit P-01 — Parking space · Parking" }]);
  });

  it("writes floors in words", () => {
    expect([1, 2, 3, 4, 11, 12, 13, 21, 22].map(floorLabel)).toEqual([
      "1st floor", "2nd floor", "3rd floor", "4th floor", "11th floor", "12th floor", "13th floor", "21st floor", "22nd floor",
    ]);
  });
});

describe("where the lead came from", () => {
  const submission = (id: number, receivedAt: string, fieldData: ExternalSubmission["fieldData"] = []) =>
    ({ id, provider: "meta", platform: "facebook", externalLeadId: String(id), receivedAt, fieldData }) as ExternalSubmission;

  it("uses the newest submission and lists the answers DAMS has no field for", () => {
    const latest = latestSubmission([
      submission(1, "2026-09-20T10:00:00"),
      submission(2, "2026-09-25T10:00:00", [
        { name: "full_name", value: "Hamza", isMapped: true },
        { name: "visit_time", value: "evening", isMapped: false, label: "Best time to visit?", valueLabel: "Evening" },
        { name: "budget_note", value: "flexible", isMapped: false },
      ]),
    ]);
    expect(latest?.id).toBe(2);
    expect(unmappedAnswers(latest)).toEqual([
      { label: "Best time to visit?", value: "Evening" },
      { label: "budget_note", value: "flexible" },
    ]);
    expect(latestSubmission([])).toBeNull();
    expect(unmappedAnswers(null)).toEqual([]);
  });

  it("names the platform only when Meta said which", () => {
    expect(platformLabel({ provider: "meta", platform: "instagram" })).toBe("Instagram");
    expect(platformLabel({ provider: "meta", platform: null })).toBe("Meta");
  });
});

describe("timeline", () => {
  const entry = (id: number, occurredAt: string, type = "CommunicationLogged", isSystemGenerated = false) =>
    ({ id, occurredAt, type, isSystemGenerated, summary: "" }) as TimelineItem;

  it("groups newest-first entries under their day", () => {
    const days = timelineDays([entry(3, "b"), entry(2, "b"), entry(1, "a")], (value) => value);
    expect(days.map((day) => [day.day, day.items.map((item) => item.id)])).toEqual([["b", [3, 2]], ["a", [1]]]);
  });

  it("colours follow-ups gold and system entries grey", () => {
    expect(timelineDot(entry(1, "", "FollowUpScheduled"))).toBe("gold");
    expect(timelineDot(entry(1, "", "LeadAssigned", true))).toBe("grey");
    expect(timelineDot(entry(1, "", "CommunicationLogged"))).toBe("navy");
  });
});

describe("who does the work", () => {
  const person = (employeeId: number, fullName: string, canOwnLeads = true) =>
    ({ employeeId, userId: employeeId * 10, fullName, canOwnLeads, status: "Active" }) as StaffMember;
  const staff = [person(1, "Sana Malik"), person(2, "Junaid Satti"), person(3, "Accounts", false), person(4, "Ali Raza")];

  it("lets a manager pick anyone who can own leads, the owner first by default", () => {
    const { options, chosen } = workerChoices(staff, { assignedEmployeeId: 1 }, staff[1], true);
    expect(options.map((option) => option.label)).toEqual(["Sana Malik (lead owner)", "Junaid Satti", "Ali Raza"]);
    expect(chosen).toBe("1");
    expect(workerChoices(staff, { assignedEmployeeId: null }, staff[1], true).chosen).toBe("2");
  });

  it("gives a Sales employee only themselves", () => {
    const { options, chosen } = workerChoices(staff, { assignedEmployeeId: 1 }, staff[0], false);
    expect(options).toEqual([{ value: "1", label: "Sana Malik (lead owner)" }]);
    expect(chosen).toBe("1");
    expect(workerChoices(staff, { assignedEmployeeId: 1 }, null, false)).toEqual({ options: [], chosen: "" });
  });

  it("offers the current owner, then me, then everyone else", () => {
    expect(assignChoices(staff, { assignedEmployeeId: 1 }, staff[3]).map((option) => option.label))
      .toEqual(["Sana Malik (current)", "Me (Ali Raza)", "Junaid Satti"]);
    expect(assignChoices(staff, { assignedEmployeeId: null }, staff[0]).map((option) => option.label))
      .toEqual(["Me (Sana Malik)", "Junaid Satti", "Ali Raza"]);
  });
});
