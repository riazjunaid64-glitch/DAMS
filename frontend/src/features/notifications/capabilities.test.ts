import { describe, expect, it } from "vitest";
import { notificationCategoryIsAllowed, notificationCategoryLabels } from "./capabilities.ts";
import { markSummaryCategoryRead, markSummaryItemRead } from "./summaryState.ts";
import type { NotificationCapabilities, NotificationSummary } from "./types.ts";

const employeeCapabilities: NotificationCapabilities = {
  role: "Employee",
  emptyStateMessage: "Assigned work will appear here.",
  categories: [
    {
      category: "LeadAssignments",
      label: "Leads",
      description: "Leads relevant to your work.",
      isMandatory: false,
      emailAvailable: true,
      pushAvailable: true,
    },
    {
      category: "EmployeeTasks",
      label: "Employee tasks",
      description: "Tasks assigned directly to you.",
      isMandatory: false,
      emailAvailable: true,
      pushAvailable: true,
    },
  ],
};

describe("notification capabilities", () => {
  it("builds labels only from categories returned by the server", () => {
    const labels = notificationCategoryLabels(employeeCapabilities);

    expect([...labels.entries()]).toEqual([
      ["LeadAssignments", "Leads"],
      ["EmployeeTasks", "Employee tasks"],
    ]);
    expect(labels.has("PaymentsAndReceipts")).toBe(false);
  });

  it("fails closed while capabilities are absent or omit a category", () => {
    expect(notificationCategoryIsAllowed(null, "LeadAssignments")).toBe(false);
    expect(notificationCategoryIsAllowed(employeeCapabilities, "PaymentsAndReceipts")).toBe(false);
    expect(notificationCategoryIsAllowed(employeeCapabilities, "LeadAssignments")).toBe(true);
  });
});

const summary: NotificationSummary = {
  unreadCount: 3,
  unreadByCategory: { LeadAssignments: 2, EmployeeTasks: 1 },
  recent: [
    {
      id: 1,
      category: "LeadAssignments",
      type: "LeadAssigned",
      priority: "Normal",
      module: "Leads",
      title: "Lead assigned",
      message: "One",
      entityType: "Lead",
      entityId: 1,
      deepLink: "/crm/leads/1",
      isRead: false,
      isEscalation: false,
      createdAt: "2026-08-01T00:00:00Z",
      readAt: null,
      expiresAt: null,
    },
    {
      id: 2,
      category: "EmployeeTasks",
      type: "EmployeeTaskAssigned",
      priority: "Normal",
      module: "Employees",
      title: "Task assigned",
      message: "Two",
      entityType: "EmployeeTask",
      entityId: 2,
      deepLink: "/employees/tasks/2",
      isRead: false,
      isEscalation: false,
      createdAt: "2026-08-01T00:00:00Z",
      readAt: null,
      expiresAt: null,
    },
  ],
};

describe("notification summary state", () => {
  it("decrements both total and category counts when one item is read", () => {
    const next = markSummaryItemRead(summary, 1);

    expect(next.unreadCount).toBe(2);
    expect(next.unreadByCategory.LeadAssignments).toBe(1);
    expect(next.recent[0]?.isRead).toBe(true);
  });

  it("subtracts the selected category when all items in it are read", () => {
    const next = markSummaryCategoryRead(summary, "LeadAssignments");

    expect(next.unreadCount).toBe(1);
    expect(next.unreadByCategory.LeadAssignments).toBe(0);
    expect(next.recent[0]?.isRead).toBe(true);
    expect(next.recent[1]?.isRead).toBe(false);
  });
});
