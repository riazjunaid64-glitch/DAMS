import { parseServerDateTime } from "../../lib/dates.ts";

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

/** One row of the leads list (`GET /api/leads`): only what the list shows. */
export interface LeadListItem {
  id: number;
  leadReference: string;
  firstName: string;
  lastName?: string | null;
  fullName: string;
  // Optional: an ad-platform lead may arrive with no phone number at all.
  phone?: string | null;
  city?: string | null;
  sourceName: string;
  propertyType?: string | null;
  purchaseIntent: string;
  paymentPreference: string;
  assignedEmployeeId?: number | null;
  assignedEmployeeName?: string | null;
  stage: LeadStage;
  /** The stage on the salesperson's simple pipeline, as the server groups it. */
  stageGroup: LeadStageGroup;
  lastActivityAt?: string | null;
  lastActivitySummary?: string | null;
  nextActionAt?: string | null;
  nextActionSummary?: string | null;
  createdAt: string;
}

/** A whole lead, as the lead page and every write return it. */
export interface Lead extends LeadListItem {
  whatsappNumber?: string | null;
  email?: string | null;
  address?: string | null;
  preferredContactMethod: string;
  preferredContactTime?: string | null;
  leadSourceId: number;
  sourceCode: string;
  sourceDetails?: string | null;
  campaignName?: string | null;
  campaignReference?: string | null;
  adReference?: string | null;
  interestedProjectId?: number | null;
  interestedProjectName?: string | null;
  interestedUnitId?: number | null;
  interestedUnitNumber?: string | null;
  preferredLocation?: string | null;
  budgetMin?: number | null;
  budgetMax?: number | null;
  notes?: string | null;
  assignmentState: string;
  assignedAt?: string | null;
  qualification: LeadQualification;
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
  updatedAt?: string | null;
  // The version this copy was read at; an edit sends it back so a stale form cannot overwrite newer changes.
  concurrencyToken: string;
  openFollowUpCount: number;
  documentCount: number;
}

/** `GET /api/leads/{id}`: the lead plus what the lead page's header shows before any tab opens. */
export interface LeadDetail extends Lead {
  counts: { timeline: number; communications: number; followUps: number; siteVisits: number };
  lastCommunication?: LastCommunication | null;
  convertedByName?: string | null;
  convertedUnitNumber?: string | null;
  /** Who last marked the lead Lost or Dormant. */
  closedByName?: string | null;
  /** The one existing customer this lead's phone or email belongs to. */
  matchedCustomerId?: number | null;
  matchedCustomerName?: string | null;
  /** Set when more than one customer shares the phone; conversion has to choose. */
  phoneMatches?: LeadCustomerMatch[];
  /**
   * True when more than one customer shares the phone. Sales get this without phoneMatches
   * (customer PII); admin/manager also get the list to choose from.
   */
  hasAmbiguousCustomerMatch?: boolean;
}

export interface LeadCustomerMatch {
  id: number;
  fullName: string;
  phone: string;
}

export interface LastCommunication {
  channel: string;
  direction: string;
  connected: boolean;
  summary: string;
  occurredAt: string;
  employeeName?: string | null;
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
  /** Days before tokenExpiresAt the sign-in is called out; the server's setting, shared with the Admin alert. 0: never. */
  signInWarningDays?: number;
  /** Set while Meta refuses the account's sign-in during sync but leads still arrive through the Page tokens. */
  syncRejectedAt?: string | null;
  /** When Meta last delivered a lead webhook for a Page this connection takes leads from. */
  lastLeadReceivedAt?: string | null;
  grantedScopes: string[];
  pageCount: number;
  instagramCount: number;
  adAccountCount: number;
  leadFormCount: number;
  enabledResourceCount: number;
  pendingEventCount?: number;
  /** Every event still Failed, however old. */
  failedEventCount?: number;
  /** Failed events whose last attempt was in the last seven days. */
  recentFailedCount?: number;
  /** When the newest event that is still Failed was last attempted. */
  lastFailedAt?: string | null;
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
  /** Lead forms only: whether an administrator has linked it to a project or its answers to lead fields. */
  hasFormMapping?: boolean;
  formMappingProjectName?: string | null;
  /** Facebook Pages only: when a lead from this Page was last received. */
  lastLeadAt?: string | null;
  /** Facebook Pages only: leads received from this Page in the last seven days. */
  leadsLast7Days?: number;
  /** Lead forms only: choice questions. Null until the form's questions have been read. */
  choiceQuestionCount?: number | null;
  /** Lead forms only: a saved mapping includes at least one answer. */
  answersSetUp?: boolean;
  /** Lead forms only: submissions that came from this form. */
  leadCount?: number;
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
  /** The question as the form words it, from the synced form. Absent until the form is synced. */
  label?: string | null;
  /** The chosen option's text, when the answer is one of the form's options. */
  valueLabel?: string | null;
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

export interface LeadList {
  items: LeadListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/** `GET /api/leads/summary`: the list's cards, counted with the list's filters except status. */
export interface LeadSummary {
  total: number;
  inProgress: number;
  won: number;
  lost: number;
  dormant: number;
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

export interface StaffMember {
  employeeId: number;
  userId?: number | null;
  fullName: string;
  email?: string | null;
  role?: string | null;
  status: string;
  canOwnLeads: boolean;
  jobTitle?: string;
  department?: string;
  phone?: string;
  joinDate?: string;
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

/** A unit as the convert popup offers it. */
export interface UnitLookup {
  id: number;
  projectId: number;
  number: string;
  type: string;
  floor: number;
  /** The project's name for the floor ("Parking"). */
  floorName?: string;
  status: string;
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
  /** False when a call we made went unanswered, or a message we sent got no reply. */
  connected: boolean;
  followUpId?: number | null;
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
  status: "Pending" | "Completed" | "Cancelled" | "Missed";
  completedAt?: string | null;
  outcome?: string | null;
}

export interface SiteVisit {
  id: number;
  projectId?: number | null;
  projectName?: string | null;
  unitId?: number | null;
  unitNumber?: string | null;
  assignedEmployeeId: number;
  assignedEmployeeName?: string | null;
  scheduledAt: string;
  remindAt?: string | null;
  meetingLocation: string;
  status: "Scheduled" | "Rescheduled" | "Completed" | "Cancelled" | "Missed";
  notes?: string | null;
  outcome?: string | null;
  outcomeNotes?: string | null;
  customerFeedback?: string | null;
  nextAction?: string | null;
  cancellationReason?: string | null;
}

export const stageLabel = (stage: string) =>
  stage.replace(/([a-z])([A-Z])/g, "$1 $2");

export const enumLabel = stageLabel;

/** The salesperson's simple pipeline (the server's LeadStageGroup). Which detailed stage falls in which
 *  step is decided only on the server (LeadStageRules.GroupOf) and arrives on each lead as `stageGroup`. */
export const leadStageGroups = ["New", "InProgress", "Won", "Lost", "Dormant"] as const;
export type LeadStageGroup = (typeof leadStageGroups)[number];

/** Minute precision: a CRM timeline is read at a glance, and seconds are noise in every column
 *  that shows one. Locale order and 12/24-hour clock still follow the reader's own settings. */
export const formatDateTime = (value?: string | null) =>
  parseServerDateTime(value)?.toLocaleString(undefined, {
    year: "numeric", month: "numeric", day: "numeric", hour: "numeric", minute: "2-digit",
  }) ?? "—";
