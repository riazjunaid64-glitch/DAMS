import type { MetaEventStatus } from "../integrations/types.ts";
import { parseServerDateTime } from "../staff/staffAccessState.ts";

export const leadStages = [
  "New",
  "FirstContactPending",
  "Contacted",
  "Qualified",
  "SiteVisitScheduled",
  "SiteVisitCompleted",
  "Negotiation",
  "DocumentsInProgress",
  "BookingPending",
  "Won",
  "Lost",
  "Dormant",
] as const;

export type LeadStage = (typeof leadStages)[number];
export type LeadQualification = "Unqualified" | "Cold" | "Warm" | "Hot";

export interface Lead {
  id: number;
  leadReference: string;
  firstName: string;
  lastName?: string | null;
  fullName: string;
  // Optional: an ad-platform lead may arrive with no phone number at all.
  phone?: string | null;
  whatsappNumber?: string | null;
  email?: string | null;
  address?: string | null;
  city?: string | null;
  preferredContactMethod: string;
  preferredContactTime?: string | null;
  leadSourceId: number;
  sourceCode: string;
  sourceName: string;
  sourceDetails?: string | null;
  campaignName?: string | null;
  campaignReference?: string | null;
  adReference?: string | null;
  interestedProjectId?: number | null;
  interestedProjectName?: string | null;
  interestedUnitId?: number | null;
  interestedUnitNumber?: string | null;
  propertyType?: string | null;
  preferredLocation?: string | null;
  budgetMin?: number | null;
  budgetMax?: number | null;
  purchaseIntent: string;
  notes?: string | null;
  assignedEmployeeId?: number | null;
  assignedEmployeeName?: string | null;
  assignedTeamId?: number | null;
  assignedTeamName?: string | null;
  assignmentState: string;
  assignedAt?: string | null;
  stage: LeadStage;
  qualification: LeadQualification;
  lastActivityAt?: string | null;
  lastActivitySummary?: string | null;
  nextActionAt?: string | null;
  nextActionSummary?: string | null;
  firstContactAt?: string | null;
  lastContactAt?: string | null;
  convertedAt?: string | null;
  convertedCustomerId?: number | null;
  convertedBookingId?: number | null;
  convertedBookingReference?: string | null;
  closureReasonId?: number | null;
  closureReasonName?: string | null;
  closureNotes?: string | null;
  closedAt?: string | null;
  reactivateOn?: string | null;
  bookingRequestId?: number | null;
  createdAt: string;
  updatedAt?: string | null;
  // The version this copy was read at; an edit sends it back so a stale form cannot overwrite newer changes.
  concurrencyToken: string;
  openFollowUpCount: number;
  documentCount: number;
}

export type MetaConnectionStatus =
  | "Connected"
  | "NeedsReauthorization"
  | "Disconnected"
  | "Error";

export interface MetaConnection {
  id: number;
  provider: string;
  displayName: string;
  status: MetaConnectionStatus;
  connectedAt: string;
  connectedByName?: string | null;
  lastSyncedAt?: string | null;
  lastErrorAt?: string | null;
  lastError?: string | null;
  tokenExpiresAt?: string | null;
  grantedScopes: string[];
  pageCount: number;
  instagramCount: number;
  adAccountCount: number;
  leadFormCount: number;
  enabledResourceCount: number;
  pendingEventCount?: number;
  failedEventCount?: number;
}

export interface MetaResource {
  id: number;
  resourceType: string;
  externalId: string;
  parentExternalId?: string | null;
  name?: string | null;
  externalStatus?: string | null;
  isEnabled: boolean;
  isActive: boolean;
  isSubscribed: boolean;
  lastSeenAt?: string | null;
}

export interface MetaResourceGroup {
  resourceType: string;
  label: string;
  items: MetaResource[];
}

export interface MetaSyncResult {
  discovered: number;
  updated: number;
  deactivated: number;
  syncedAt: string;
  warning?: string | null;
}

/** One answer from a provider form. Unmapped answers are kept and shown, never discarded. */
export interface ExternalFieldAnswer {
  name: string;
  value?: string | null;
  isMapped: boolean;
}

export interface ExternalSubmission {
  id: number;
  provider: string;
  platform?: string | null;
  connectionDisplayName?: string | null;
  externalLeadId: string;
  externalFormReference?: string | null;
  externalFormName?: string | null;
  pageName?: string | null;
  adAccountExternalId?: string | null;
  campaignName?: string | null;
  adSetName?: string | null;
  adName?: string | null;
  externalSubmittedAt?: string | null;
  receivedAt: string;
  fieldData: ExternalFieldAnswer[];
}

/** What the provider actually sent for one submission. Admins and managers only. */
export interface ExternalSubmissionRaw {
  submissionId: number;
  provider: string;
  externalLeadId: string;
  rawPayloadJson?: string | null;
  event?: IntegrationEventRaw | null;
}

export interface IntegrationEventRaw {
  id: number;
  eventType: string;
  status: MetaEventStatus;
  receivedAt: string;
  processedAt?: string | null;
  rawPayloadJson: string;
}

export interface LeadList {
  items: Lead[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface LeadSource {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
  isSystem: boolean;
  displayOrder: number;
  customerSource: string;
}

export interface ClosureReason {
  id: number;
  code: string;
  name: string;
  kind: "Lost" | "Dormant" | "Both";
  isActive: boolean;
  isSystem: boolean;
  displayOrder: number;
}

export interface Team {
  id: number;
  name: string;
  managerEmployeeId?: number | null;
  managerName?: string | null;
  isActive: boolean;
  memberCount: number;
}

export interface StaffMember {
  employeeId: number;
  userId?: number | null;
  fullName: string;
  email?: string | null;
  role?: string | null;
  teamId?: number | null;
  teamName?: string | null;
  status: string;
  canOwnLeads: boolean;
  jobTitle?: string;
  department?: string;
  phone?: string;
  joinDate?: string;
  isTeamManager?: boolean;
}

/**
 * Whether an employee can sign in to DAMS. `None` means no login exists at all, which is a
 * different thing from a login that is waiting, working or switched off.
 *
 * This is not employment status. `StaffMember.status` says whether somebody works here;
 * this says whether they can sign in. An Active employee who has never activated their
 * link is `Active` employment and `Invited` access at the same time.
 */
export type StaffAccountAccess = "None" | "Invited" | "Active" | "Disabled";

/**
 * An employee as the Admin staff-accounts screen sees them. The directory endpoint returns
 * the smaller {@link StaffMember}; only `/api/staff/accounts` carries account access, which
 * is why the access fields live here rather than being optional on every staff row.
 */
export interface StaffAccount extends StaffMember {
  access: StaffAccountAccess;
  /** When the outstanding activation link stops working. Null unless one is outstanding. */
  invitationExpiresAt?: string | null;
}

/**
 * What `POST /api/staff/accounts` answers. Creating the account and delivering the
 * activation email are separate outcomes: the account survives a failed send, so an HTTP
 * success does not mean the employee was told about it.
 */
export interface StaffAccountProvisionResult {
  account: StaffAccount;
  /** False when an existing login was linked and keeps the password it already had. */
  invitationRequired: boolean;
  invitationSent: boolean;
  invitationExpiresAt?: string | null;
  /** Safe to show an Admin. Set only when the invitation could not be sent. */
  invitationError?: string | null;
}

/** What resending an invitation answers. Never carries a token, a link or a password. */
export interface StaffInvitationResult {
  /** The new invitation was stored. A failed email does not undo this. */
  issued: boolean;
  emailSent: boolean;
  expiresAt?: string | null;
  error?: string | null;
}

export interface ProjectLookup {
  id: number;
  name: string;
}

export interface UnitLookup {
  id: number;
  number: string;
  projectId?: number;
}

export interface TimelineItem {
  id: number;
  type: string;
  summary: string;
  notes?: string | null;
  channel?: string | null;
  previousValue?: string | null;
  newValue?: string | null;
  performedByName?: string | null;
  isSystemGenerated: boolean;
  occurredAt: string;
  bookingId?: number | null;
  customerId?: number | null;
}

export interface Communication {
  id: number;
  channel: string;
  direction: string;
  occurredAt: string;
  employeeName?: string | null;
  summary: string;
  customerResponse?: string | null;
  nextAction?: string | null;
  nextActionAt?: string | null;
}

export interface FollowUp {
  id: number;
  type: string;
  assignedEmployeeId: number;
  assignedEmployeeName?: string | null;
  title: string;
  notes?: string | null;
  dueAt: string;
  priority: string;
  status: string;
  completedAt?: string | null;
  outcome?: string | null;
}

export interface SiteVisit {
  id: number;
  projectId?: number | null;
  projectName?: string | null;
  unitId?: number | null;
  unitNumber?: string | null;
  assignedEmployeeName?: string | null;
  scheduledAt: string;
  meetingLocation: string;
  status: string;
  notes?: string | null;
  outcome?: string | null;
  outcomeNotes?: string | null;
  customerFeedback?: string | null;
  nextAction?: string | null;
  cancellationReason?: string | null;
}

export interface LeadDocument {
  id: number;
  category: string;
  fileName: string;
  contentType: string;
  fileSize: number;
  description?: string | null;
  uploadedByName?: string | null;
  uploadedAt: string;
}

export interface LeadComment {
  id: number;
  body: string;
  isManagerReviewRequest: boolean;
  isDecisionRecord: boolean;
  authorName?: string | null;
  createdAt: string;
  mentions: { userId: number; name?: string | null }[];
}

export interface AssignmentHistory {
  id: number;
  previousEmployeeName?: string | null;
  assignedEmployeeName?: string | null;
  previousTeamId?: number | null;
  assignedTeamId?: number | null;
  reason?: string | null;
  assignedByName?: string | null;
  assignedAt: string;
}

export const stageLabel = (stage: string) =>
  stage.replace(/([a-z])([A-Z])/g, "$1 $2");

export const enumLabel = stageLabel;

export const isClosedStage = (stage: string) =>
  stage === "Won" || stage === "Lost" || stage === "Dormant";

/** Minute precision: a CRM timeline is read at a glance, and seconds are noise in every column
 *  that shows one. Locale order and 12/24-hour clock still follow the reader's own settings. */
export const formatDateTime = (value?: string | null) =>
  parseServerDateTime(value)?.toLocaleString(undefined, {
    year: "numeric", month: "numeric", day: "numeric", hour: "numeric", minute: "2-digit",
  }) ?? "—";

export const formatShortDate = (value?: string | null) =>
  parseServerDateTime(value)?.toLocaleDateString() ?? "—";

export const isPastServerTime = (value?: string | null, now: Date = new Date()) => {
  const date = parseServerDateTime(value);
  return date !== null && date < now;
};
