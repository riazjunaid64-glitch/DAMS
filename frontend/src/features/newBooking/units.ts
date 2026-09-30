import { floorName } from "../../lib/floors.ts";

/** A unit as the picker needs it: what the project's unit list returns. */
export interface PickerUnit {
  id: number;
  unitNumber: string;
  unitType: string;
  floorNumber: number;
  floorName: string;
  size: number;
  price: number;
  status: string;
}

/** Units are shown this many at a time; "Show more" adds the next page. */
export const UNIT_PAGE_SIZE = 20;

export interface UnitFilters {
  /** Typed in the search box: matches any part of the unit number. */
  query: string;
  /** A floor number as text, or "" for all floors. */
  floor: string;
  /** A unit type, or "" for all types. */
  type: string;
}

export const NO_FILTERS: UnitFilters = { query: "", floor: "", type: "" };

/** Only units that can still be booked. The project's list carries every unit; the rest are dropped here. */
export const availableUnits = (units: PickerUnit[]): PickerUnit[] => units.filter((unit) => unit.status === "Available");

const byNumber = (a: PickerUnit, b: PickerUnit) => a.unitNumber.localeCompare(b.unitNumber, undefined, { numeric: true });

/** Filtering runs on the device over the whole project list, so typing narrows it at once. */
export function filterUnits(units: PickerUnit[], filters: UnitFilters): PickerUnit[] {
  const query = filters.query.trim().toLowerCase();
  return units.filter((unit) =>
    (query === "" || unit.unitNumber.toLowerCase().includes(query))
    && (filters.floor === "" || String(unit.floorNumber) === filters.floor)
    && (filters.type === "" || unit.unitType === filters.type));
}

export interface FloorGroup {
  floorNumber: number;
  name: string;
  units: PickerUnit[];
}

/** Units grouped by floor, lowest floor first, each floor's units in number order. */
export function groupByFloor(units: PickerUnit[]): FloorGroup[] {
  const groups = new Map<number, FloorGroup>();
  for (const unit of [...units].sort(byNumber)) {
    const group = groups.get(unit.floorNumber) ?? { floorNumber: unit.floorNumber, name: floorName(unit.floorName, unit.floorNumber), units: [] };
    group.units.push(unit);
    groups.set(unit.floorNumber, group);
  }
  return [...groups.values()].sort((a, b) => a.floorNumber - b.floorNumber);
}

/**
 * The first `count` units across the floors, in order, keeping each floor's full size so its header
 * still says how many are available ("8TH FLOOR · 4 available") even when only some are on screen.
 */
export function firstUnits(groups: FloorGroup[], count: number): { group: FloorGroup; shown: PickerUnit[] }[] {
  const out: { group: FloorGroup; shown: PickerUnit[] }[] = [];
  let left = count;
  for (const group of groups) {
    if (left <= 0) break;
    const shown = group.units.slice(0, left);
    out.push({ group, shown });
    left -= shown.length;
  }
  return out;
}

/** The floors the Floor filter offers, bottom to top, for the units the project has available. */
export function floorFilterOptions(units: PickerUnit[]): { value: string; label: string }[] {
  return groupByFloor(units).map((group) => ({ value: String(group.floorNumber), label: group.name }));
}

/** The unit types the Type filter offers: the ones this project actually has available, in the usual order. */
export function typeFilterOptions(units: PickerUnit[], order: readonly string[]): { value: string; label: string }[] {
  const present = new Set(units.map((unit) => unit.unitType));
  return [...order.filter((type) => present.has(type)), ...[...present].filter((type) => !order.includes(type)).sort()]
    .map((type) => ({ value: type, label: type }));
}
