// Numeric project status codes from the API (Project.Status on the backend).
export const PROJECT_STATUS = {
  Planning: 1,
  Ongoing: 2,
  Completed: 3,
  Cancelled: 4,
  Archived: 5,
} as const;

// Internal/admin wording, used on management pages.
export const ADMIN_STATUS_LABELS: Record<number, string> = {
  1: "Planning",
  2: "Ongoing",
  3: "Completed",
  4: "Cancelled",
  5: "Archived",
};

// Marketing wording for the public site (cards, homepage).
export const PUBLIC_STATUS_LABELS: Record<number, string> = {
  1: "Coming Soon",
  2: "Booking Open",
  3: "Completed",
  4: "Cancelled",
  5: "Archived",
};

export const getStatusNum = (status: number | string): number =>
  typeof status === "number" ? status : Number(status) || PROJECT_STATUS.Planning;

// Cancelled and archived projects stay internal-only.
export const isPubliclyVisible = (status: number | string): boolean =>
  getStatusNum(status) <= PROJECT_STATUS.Completed;
