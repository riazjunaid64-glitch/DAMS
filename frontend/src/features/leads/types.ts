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
  phone?: string | null;
  city?: string | null;
  sourceName: string;
  propertyType?: string | null;
  purchaseIntent: string;
  paymentPreference: string;
  assignedEmployeeId?: number | null;
  assignedEmployeeName?: string | null;
  stage: LeadStage;
  stageGroup: LeadStageGroup;
  lastActivityAt?: string | null;
  lastActivitySummary?: string | null;
  nextActionAt?: string | null;
  nextActionSummary?: string | null;
  createdAt: string;
}

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
  matchedCustomerId?: number | null;
  matchedCustomerName?: string | null;
  closureReasonId?: number | null;
  closureReasonName?: string | null;
  closureNotes?: string | null;
  closedAt?: string | null;
  reactivateOn?: string | null;
  bookingRequestId?: number | null;
  updatedAt?: string | null;
  concurrencyToken: string;
  openFollowUpCount: number;
  documentCount: number;
}

export interface LeadDetail extends Lead {
  counts: { timeline: number; communications: number; followUps: number; siteVisits: number };
  lastCommunication?: LastCommunication | null;
  convertedByName?: string | null;
  convertedUnitNumber?: string | null;
  closedByName?: string | null;
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
  signInWarningDays?: number;
  syncRejectedAt?: string | null;
  lastLeadReceivedAt?: string | null;
  grantedScopes: string[];
  pageCount: number;
  instagramCount: number;
  adAccountCount: number;
  leadFormCount: number;
  enabledResourceCount: number;
  pendingEventCount?: number;
  failedEventCount?: number;
  recentFailedCount?: number;
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
  hasFormMapping?: boolean;
  formMappingProjectName?: string | null;
  lastLeadAt?: string | null;
  leadsLast7Days?: number;
  choiceQuestionCount?: number | null;
  answersSetUp?: boolean;
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

export interface ExternalFieldAnswer {
  name: string;
  value?: string | null;
  isMapped: boolean;
  label?: string | null;
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

export type StaffAccountAccess = "None" | "Invited" | "Active" | "Disabled";

export interface StaffAccount extends StaffMember {
  access: StaffAccountAccess;
  invitationExpiresAt?: string | null;
}

export interface StaffAccountProvisionResult {
  account: StaffAccount;
  invitationRequired: boolean;
  invitationSent: boolean;
  invitationExpiresAt?: string | null;
  invitationError?: string | null;
}

export interface StaffInvitationResult {
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
  projectId: number;
  number: string;
  type: string;
  floor: number;
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

export const leadStageGroups = ["New", "InProgress", "Won", "Lost", "Dormant"] as const;
export type LeadStageGroup = (typeof leadStageGroups)[number];

export const formatDateTime = (value?: string | null) =>
  parseServerDateTime(value)?.toLocaleString(undefined, {
    year: "numeric", month: "numeric", day: "numeric", hour: "numeric", minute: "2-digit",
  }) ?? "—";
