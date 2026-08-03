export type NotificationCategory =
  | "PaymentsAndReceipts"
  | "BookingUpdates"
  | "InstallmentReminders"
  | "LeadAssignments"
  | "FollowUps"
  | "SiteVisits"
  | "Mentions"
  | "EmployeeTasks"
  | "ProjectUpdates"
  | "Announcements"
  | "AccountAndSecurity"
  | "ManagerEscalations";

export type NotificationPriority = "Low" | "Normal" | "High" | "Critical";

export type NotificationChannel = "InApp" | "Email" | "WebPush";

export type DeliveryStatus =
  | "Pending"
  | "Scheduled"
  | "Processing"
  | "Sent"
  | "Delivered"
  | "Failed"
  | "Retrying"
  | "Bounced"
  | "Expired"
  | "Cancelled"
  | "Skipped"
  | "Unavailable";

export type JobStatus = "Scheduled" | "Processing" | "Sent" | "PartiallyFailed" | "Failed" | "Cancelled";

export interface NotificationItem {
  id: number;
  category: NotificationCategory;
  type: string;
  priority: NotificationPriority;
  module: string;
  title: string;
  message: string;
  entityType: string;
  entityId: number | null;
  deepLink: string | null;
  isRead: boolean;
  isEscalation: boolean;
  createdAt: string;
  readAt: string | null;
  expiresAt: string | null;
}

export interface NotificationPage {
  items: NotificationItem[];
  totalCount: number;
  unreadCount: number;
  page: number;
  pageSize: number;
}

export interface NotificationSummary {
  unreadCount: number;
  unreadByCategory: Record<string, number>;
  recent: NotificationItem[];
}

export interface NotificationCategoryCapability {
  category: NotificationCategory;
  label: string;
  description: string;
  isMandatory: boolean;
  emailAvailable: boolean;
  pushAvailable: boolean;
}

export interface NotificationCapabilities {
  role: string;
  emptyStateMessage: string;
  categories: NotificationCategoryCapability[];
}

export interface OpenResult {
  allowed: boolean;
  deepLink: string | null;
  message: string;
  notification: NotificationItem | null;
}

export interface PushConfig {
  enabled: boolean;
  publicKey: string | null;
  displayName: string;
  iconUrl: string | null;
  badgeUrl: string | null;
  hasActiveSubscription: boolean;
  deviceCount: number;
}

export interface PreferenceRow {
  category: NotificationCategory;
  label: string;
  description: string;
  emailEnabled: boolean;
  pushEnabled: boolean;
  isMandatory: boolean;
  emailAvailable: boolean;
  pushAvailable: boolean;
}

export interface PushDevice {
  id: number;
  deviceLabel: string | null;
  fingerprint: string;
  createdAt: string;
  lastSeenAt: string;
  lastSuccessAt: string | null;
  isActive: boolean;
}

// ── Admin ──────────────────────────────────────────────────────────────────────

export interface NotificationStatus {
  emailEnabled: boolean;
  emailConfigured: boolean;
  emailConfigurationIssue: string | null;
  emailLastTestAt: string | null;
  emailLastFailure: string | null;
  pushEnabled: boolean;
  pushConfigured: boolean;
  pushConfigurationIssue: string | null;
  pushLastTestAt: string | null;
  pushLastFailure: string | null;
  activePushSubscriptions: number;
  pendingDeliveries: number;
  failedDeliveries: number;
}

export interface NotificationSettings {
  values: Record<string, string | null>;
  /** Masked hints only — a stored secret is never sent to the browser. */
  secrets: Record<string, string>;
  status: NotificationStatus;
}

export interface TemplateRow {
  id: number;
  type: string;
  channel: NotificationChannel;
  name: string;
  category: NotificationCategory;
  subject: string;
  heading: string | null;
  body: string;
  actionText: string | null;
  actionUrl: string | null;
  footer: string | null;
  iconUrl: string | null;
  badgeUrl: string | null;
  isEnabled: boolean;
  version: number;
  updatedAt: string | null;
  updatedByName: string | null;
  availableVariables: string[];
  isMandatory: boolean;
}

export interface TemplatePreview {
  subject: string;
  html: string;
  plainText: string;
  pushTitle: string | null;
  pushBody: string | null;
  actionUrl: string | null;
}

export interface RuleRow {
  type: string;
  name: string;
  category: NotificationCategory;
  module: string;
  isEnabled: boolean;
  inAppEnabled: boolean;
  emailEnabled: boolean;
  pushEnabled: boolean;
  priority: NotificationPriority;
  delayMinutes: number;
  reminderLeadDays: number;
  remindOnDueDate: boolean;
  repeatWhenOverdue: boolean;
  escalateToSupervisors: boolean;
  isMandatory: boolean;
  updatedAt: string | null;
}

export type AudienceType =
  | "SelectedUsers"
  | "AllCustomers"
  | "AllSalesEmployees"
  | "AllManagers"
  | "AllInternalStaff"
  | "Team"
  | "CustomersInProject"
  | "CustomersOfBookings"
  | "CustomersWithOverdueInstallments"
  | "EmployeesAssignedToLeads";

export interface ComposeRequest {
  title: string;
  message: string;
  actionText?: string | null;
  actionUrl?: string | null;
  type: string;
  priority: NotificationPriority;
  sendEmail: boolean;
  sendPush: boolean;
  audience: {
    type: AudienceType;
    userIds: number[];
    teamId: number | null;
    projectId: number | null;
    bookingIds: number[];
    leadIds: number[];
  };
  scheduledAt?: string | null;
  confirmLargeAudience?: boolean;
  requestKey?: string | null;
}

export interface AudiencePreview {
  totalRecipients: number;
  withEmail: number;
  withPushDevices: number;
  pushDeviceCount: number;
  emailOptedOut: number;
  pushOptedOut: number;
  requiresConfirmation: boolean;
  description: string;
  sampleRecipients: string[];
}

export interface JobRow {
  id: number;
  status: JobStatus;
  type: string;
  category: NotificationCategory;
  priority: NotificationPriority;
  title: string;
  message: string;
  channels: string;
  audienceType: AudienceType;
  audienceDescription: string;
  scheduledAt: string | null;
  createdAt: string;
  createdByName: string | null;
  completedAt: string | null;
  cancelledAt: string | null;
  recipientCount: number;
  failureReason: string | null;
  actionUrl: string | null;
}

export interface DeliveryRow {
  id: number;
  notificationId: number;
  type: string;
  category: NotificationCategory;
  title: string;
  channel: NotificationChannel;
  status: DeliveryStatus;
  recipientUserId: number | null;
  recipientName: string | null;
  target: string | null;
  entityType: string;
  entityId: number | null;
  createdAt: string;
  scheduledFor: string | null;
  processingStartedAt: string | null;
  sentAt: string | null;
  deliveredAt: string | null;
  failedAt: string | null;
  attemptCount: number;
  nextAttemptAt: string | null;
  providerReference: string | null;
  failureReason: string | null;
  isPermanentFailure: boolean;
  canRetry: boolean;
  jobId: number | null;
  createdByName: string | null;
}

export interface DeliveryPage {
  items: DeliveryRow[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface SuppressionRow {
  id: number;
  email: string;
  reason: string;
  createdAt: string;
}

export interface AuditRow {
  id: number;
  area: string;
  action: string;
  details: string | null;
  performedByName: string | null;
  occurredAt: string;
}

export const CATEGORY_LABELS: Record<NotificationCategory, string> = {
  PaymentsAndReceipts: "Payments",
  BookingUpdates: "Bookings",
  InstallmentReminders: "Installments",
  LeadAssignments: "Leads",
  FollowUps: "Follow-ups",
  SiteVisits: "Site visits",
  Mentions: "Mentions",
  EmployeeTasks: "Tasks",
  ProjectUpdates: "Projects",
  Announcements: "Announcements",
  AccountAndSecurity: "Account",
  ManagerEscalations: "Escalations",
};

export const DELIVERY_STATUS_LABELS: Record<DeliveryStatus, string> = {
  Pending: "Queued",
  Scheduled: "Scheduled",
  Processing: "Sending",
  Sent: "Sent to provider",
  Delivered: "Delivered",
  Failed: "Failed",
  Retrying: "Retrying",
  Bounced: "Bounced",
  Expired: "Expired",
  Cancelled: "Cancelled",
  Skipped: "Skipped by preference",
  Unavailable: "No address or device",
};

/** Short, human relative time — "just now", "12m ago", "3 Feb". */
export function relativeTime(iso: string): string {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return "";

  const diffSeconds = Math.round((Date.now() - then) / 1000);
  if (diffSeconds < 45) return "just now";
  if (diffSeconds < 3600) return `${Math.round(diffSeconds / 60)}m ago`;
  if (diffSeconds < 86400) return `${Math.round(diffSeconds / 3600)}h ago`;
  if (diffSeconds < 604800) return `${Math.round(diffSeconds / 86400)}d ago`;

  return new Date(iso).toLocaleDateString(undefined, { day: "numeric", month: "short" });
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const value = new Date(iso);
  if (Number.isNaN(value.getTime())) return "—";
  return value.toLocaleString(undefined, {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}
