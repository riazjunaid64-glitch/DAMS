export type StatusTone = "blue" | "green" | "red" | "orange" | "grey" | "gold";

/**
 * The one status → colour map. Screens pass the status they have; the colour comes from here.
 * Keys are compared case- and space-insensitively, so "In progress", "InProgress" and
 * "IN_PROGRESS" all land on the same tone.
 */
const TONES: Record<string, StatusTone> = {
  inprogress: "blue",
  ongoing: "blue",
  new: "blue",
  won: "green",
  available: "green",
  completed: "green",
  active: "green",
  lost: "red",
  cancelled: "red",
  dormant: "orange",
  booked: "orange",
  planning: "orange",
  needsdetails: "orange",
  reserved: "orange",
  pending: "orange",
  sold: "grey",
  closed: "grey",
  archived: "grey",
  inactive: "grey",
  cover: "gold",
  highlight: "gold",
};

const normalise = (status: string) => status.toLowerCase().replace(/[\s_-]+/g, "");

export function statusTone(status: string): StatusTone {
  return TONES[normalise(status)] ?? "grey";
}

/** "InProgress" / "in_progress" → "In progress" for display. */
export function statusLabel(status: string): string {
  const words = status.replace(/[_-]+/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").trim().toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}
