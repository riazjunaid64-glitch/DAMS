/** Rules for a project's floor list (KAN-57). Pure, so the Floors popup, the Overview and the tests share them. */

import { floorLabel, type Floor } from "../../lib/floors.ts";

/** A floor as GET /api/Project/{id}/floors returns it. */
export type ProjectFloor = Floor & { unitCount: number };

/** One editable row of the Floors popup. `savedNumber` is set while the floor has units: that number can't change. */
export type FloorRow = {
  key: string;
  number: string;
  name: string;
  unitCount: number;
  savedNumber: number | null;
};

export const LOWEST_FLOOR = -10;
export const HIGHEST_FLOOR = 200;
export const MAX_BASEMENTS = 10;
export const MAX_FLOORS_ABOVE_GROUND = 200;

let nextKey = 0;
const newKey = () => `floor-${(nextKey += 1)}`;

/** "4 units" · "1 unit" · "No units". */
export function unitCountLabel(count: number): string {
  if (count <= 0) return "No units";
  return count === 1 ? "1 unit" : `${count} units`;
}

/** A whole floor number as typed ("-2", "0", "18"); null for blank or anything else. */
export function typedFloorNumber(value: string): number | null {
  const trimmed = value.trim();
  return /^-?\d+$/.test(trimmed) ? Number(trimmed) : null;
}

export function rowsFromFloors(floors: readonly ProjectFloor[]): FloorRow[] {
  return sortRows(floors.map((floor) => ({
    key: newKey(),
    number: String(floor.number),
    name: floor.name,
    unitCount: floor.unitCount,
    savedNumber: floor.unitCount > 0 ? floor.number : null,
  })));
}

/** Bottom to top. A row whose number is not a number yet stays at the end until it is. */
export function sortRows(rows: readonly FloorRow[]): FloorRow[] {
  const position = (row: FloorRow) => typedFloorNumber(row.number) ?? Number.POSITIVE_INFINITY;
  return [...rows].sort((a, b) => position(a) - position(b));
}

const isStandardName = (floor: Floor) => floor.name.trim().toLowerCase() === floorLabel(floor.number).toLowerCase();

/** Basements and floors above ground as the list stands (counted as the Overview summary counts them), for the quick-setup fields. */
export function quickSetupFrom(floors: readonly Floor[]): { basements: string; aboveGround: string } {
  if (floors.length === 0) return { basements: "", aboveGround: "" };
  const standard = floors.filter((floor) => floor.number > 0 && isStandardName(floor)).length;
  return {
    basements: String(floors.filter((floor) => floor.number < 0).length),
    aboveGround: standard > 0 ? String(standard) : "",
  };
}

/** Basement N … Basement 1, Ground floor, 1st floor … Nth floor. */
export function standardFloors(basements: number, aboveGround: number): Floor[] {
  const floors: Floor[] = [];
  for (let number = -basements; number <= aboveGround; number += 1) floors.push({ number, name: floorLabel(number) });
  return floors;
}

/**
 * "Create floors": rebuild the list with standard names, keeping every floor that has units (with its
 * own name) exactly as it is. Nothing is saved until Save floors.
 */
export function createFloors(rows: readonly FloorRow[], basements: number, aboveGround: number): FloorRow[] {
  const kept = rows.filter((row) => row.unitCount > 0);
  const keptNumbers = new Set(kept.map((row) => row.savedNumber ?? typedFloorNumber(row.number)));
  const built: FloorRow[] = standardFloors(basements, aboveGround)
    .filter((floor) => !keptNumbers.has(floor.number))
    .map((floor) => ({ key: newKey(), number: String(floor.number), name: floor.name, unitCount: 0, savedNumber: null }));
  return sortRows([...kept, ...built]);
}

/** "Add floor": an empty row at the end with the next free number above the top floor. */
export function addFloorRow(rows: readonly FloorRow[]): FloorRow[] {
  const numbers = rows.map((row) => typedFloorNumber(row.number)).filter((number): number is number => number !== null);
  const next = numbers.length === 0 ? 0 : Math.max(...numbers) + 1;
  return [...rows, { key: newKey(), number: String(Math.min(next, HIGHEST_FLOOR)), name: "", unitCount: 0, savedNumber: null }];
}

/** What is wrong with a row, and which of its two inputs to mark. */
export type FloorRowError = { message: string; field: "number" | "name" };

/**
 * The message under each row that can't be saved, by row key: an empty name, a number outside the
 * range, a duplicate number or name, or a floor with units whose number was changed.
 */
export function floorRowErrors(rows: readonly FloorRow[]): Record<string, FloorRowError> {
  const errors: Record<string, FloorRowError> = {};
  const numbers = new Set<number>();
  const names = new Set<string>();
  for (const row of rows) {
    const number = typedFloorNumber(row.number);
    const name = row.name.trim();
    if (row.savedNumber !== null && number !== row.savedNumber) {
      const units = unitCountLabel(row.unitCount).toLowerCase();
      errors[row.key] = { field: "number", message: `${name || floorLabel(row.savedNumber)} has ${units}, so its number can't change. Move the units first.` };
    } else if (number === null || number < LOWEST_FLOOR || number > HIGHEST_FLOOR) {
      errors[row.key] = { field: "number", message: `Enter a floor number from ${LOWEST_FLOOR} to ${HIGHEST_FLOOR}.` };
    } else if (numbers.has(number)) {
      errors[row.key] = { field: "number", message: `Floor number ${number} is used twice.` };
    } else if (!name) {
      errors[row.key] = { field: "name", message: "Enter a floor name." };
    } else if (names.has(name.toLowerCase())) {
      errors[row.key] = { field: "name", message: `"${name}" is used for two floors.` };
    }
    if (number !== null) numbers.add(number);
    if (name) names.add(name.toLowerCase());
  }
  return errors;
}

/** The PUT body. Only called once `floorRowErrors` is empty. */
export function floorsPayload(rows: readonly FloorRow[]): Floor[] {
  return sortRows(rows).map((row) => ({ number: Number(row.number.trim()), name: row.name.trim() }));
}

/**
 * The Overview DETAILS line: "2 basements · Ground · 18 floors · Rooftop". Basements count every
 * negative number, "Ground" shows when 0 exists, floors count the positive floors with a standard
 * name, and any other positive floor is added by its own name. No list: "Not set up".
 */
export function floorSummary(floors: readonly Floor[] | null | undefined): string {
  if (!floors || floors.length === 0) return "Not set up";
  const sorted = [...floors].sort((a, b) => a.number - b.number);
  const basements = sorted.filter((floor) => floor.number < 0).length;
  const above = sorted.filter((floor) => floor.number > 0);
  const standard = above.filter(isStandardName).length;
  const parts: string[] = [];
  if (basements > 0) parts.push(basements === 1 ? "1 basement" : `${basements} basements`);
  if (sorted.some((floor) => floor.number === 0)) parts.push("Ground");
  if (standard > 0) parts.push(standard === 1 ? "1 floor" : `${standard} floors`);
  for (const floor of above) if (!isStandardName(floor)) parts.push(floor.name.trim());
  return parts.join(" · ");
}
