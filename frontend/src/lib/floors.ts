/** One floor of a project: its position (basements negative, ground 0) and the name people see. */
export type Floor = { number: number; name: string };

const ORDINALS: Record<number, string> = { 1: "st", 2: "nd", 3: "rd" };

/** "Basement 1" · "Ground floor" · "1st floor" · "8th floor". */
export function floorLabel(floorNumber: number): string {
  if (floorNumber === 0) return "Ground floor";
  if (floorNumber < 0) return `Basement ${Math.abs(floorNumber)}`;
  const mod100 = floorNumber % 100;
  const suffix = mod100 >= 11 && mod100 <= 13 ? "th" : ORDINALS[floorNumber % 10] ?? "th";
  return `${floorNumber}${suffix} floor`;
}

/** The name the API sends for a unit's floor; the standard label only when it is missing. */
export function floorName(name: string | null | undefined, floorNumber: number): string {
  return name?.trim() ? name : floorLabel(floorNumber);
}

/**
 * Floors the unit form offers, bottom to top: the project's own floor list, or — while it has none —
 * Basement 2 up to the 30th floor. The current floor is kept if the choices do not include it.
 */
export function floorChoices(current?: number | null, projectFloors?: readonly Floor[] | null): { value: string; label: string }[] {
  const floors: Floor[] = projectFloors && projectFloors.length > 0 ? [...projectFloors] : [];
  if (floors.length === 0) {
    for (let floor = -2; floor <= 30; floor += 1) floors.push({ number: floor, name: floorLabel(floor) });
  }
  if (current != null && Number.isFinite(current) && !floors.some((floor) => floor.number === current)) {
    floors.push({ number: current, name: floorLabel(current) });
  }
  floors.sort((a, b) => a.number - b.number);
  return floors.map((floor) => ({ value: String(floor.number), label: floor.name }));
}
