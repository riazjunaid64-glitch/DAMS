import { floorName } from "../../lib/floors.ts";
import { unitStatus } from "../../features/leads/labels.ts";

export const UNIT_TYPES = ["Studio", "1 Bed", "2 Bed", "3 Bed", "Parking space"] as const;

export type UnitSort = "" | "priceAsc" | "priceDesc" | "sizeDesc";

export type UnitRow = {
  id: number;
  unitNumber: string;
  unitType: string;
  floorNumber: number;
  floorName: string;
  size: number;
  price: number;
  status: string;
};

export type UnitQuery = {
  search: string;
  type: string;
  status: string;
  floor: string;
};

const byNumber = (a: UnitRow, b: UnitRow) =>
  a.unitNumber.localeCompare(b.unitNumber, "en", { numeric: true, sensitivity: "base" });

export function filterUnits(units: readonly UnitRow[], query: UnitQuery): UnitRow[] {
  const search = query.search.trim().toLowerCase();
  return units.filter((unit) => {
    if (search && !unit.unitNumber.toLowerCase().includes(search)) return false;
    if (query.type && unit.unitType !== query.type) return false;
    if (query.status && unitStatus(unit.status) !== query.status) return false;
    if (query.floor !== "" && String(unit.floorNumber) !== query.floor) return false;
    return true;
  });
}

export function sortUnits(units: readonly UnitRow[], sort: UnitSort): UnitRow[] {
  const copy = [...units];
  if (sort === "priceAsc") return copy.sort((a, b) => a.price - b.price || byNumber(a, b));
  if (sort === "priceDesc") return copy.sort((a, b) => b.price - a.price || byNumber(a, b));
  if (sort === "sizeDesc") return copy.sort((a, b) => b.size - a.size || byNumber(a, b));
  return copy.sort(byNumber);
}

/** Available units of each fixed type, in the type list's order. */
export function availableByType(units: readonly { unitType: string; status: string }[]): { type: string; count: number }[] {
  return UNIT_TYPES.map((type) => ({
    type,
    count: units.filter((unit) => unit.unitType === type && unitStatus(unit.status) === "Available").length,
  }));
}

/** Floors that actually have a unit, lowest first, by the name the project gives them. */
export function floorsInUse(units: readonly { floorNumber: number; floorName: string }[]): { value: string; label: string }[] {
  const names = new Map<number, string>();
  for (const unit of units) if (!names.has(unit.floorNumber)) names.set(unit.floorNumber, floorName(unit.floorName, unit.floorNumber));
  return [...names.entries()]
    .sort(([a], [b]) => a - b)
    .map(([floor, label]) => ({ value: String(floor), label }));
}

export function formatSqFt(size: number): string {
  return `${size.toLocaleString("en-PK")} sq ft`;
}
