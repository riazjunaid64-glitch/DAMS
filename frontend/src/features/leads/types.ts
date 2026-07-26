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
  phone: string;
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
  openFollowUpCount: number;
  documentCount: number;
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

export const formatDateTime = (value?: string | null) =>
  value ? new Date(value).toLocaleString() : "—";

export const formatShortDate = (value?: string | null) =>
  value ? new Date(value).toLocaleDateString() : "—";
