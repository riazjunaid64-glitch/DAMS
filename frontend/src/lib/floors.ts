// Ground floor is stored as 0; a level below it (parking, basement storage) is stored as a
// negative FloorNumber. Both are valid data — every Parking Space unit in the system is -1 — but
// showing the raw number on a booking form, receipt or customer screen reads as broken rather
// than as "basement". This is display-only: sorting, filtering and data entry keep using the
// number itself, which already orders Basement < Ground < 1st correctly.
export function formatFloor(floorNumber: number): string {
  if (floorNumber === 0) return "Ground";
  if (floorNumber < 0) return `Basement ${Math.abs(floorNumber)}`;
  return String(floorNumber);
}

const ORDINALS: Record<number, string> = { 1: "st", 2: "nd", 3: "rd" };

/** "Basement 1" · "Ground floor" · "1st floor" · "8th floor". */
export function floorLabel(floorNumber: number): string {
  if (floorNumber === 0) return "Ground floor";
  if (floorNumber < 0) return `Basement ${Math.abs(floorNumber)}`;
  const mod100 = floorNumber % 100;
  const suffix = mod100 >= 11 && mod100 <= 13 ? "th" : ORDINALS[floorNumber % 10] ?? "th";
  return `${floorNumber}${suffix} floor`;
}

/** Floors the Add unit form offers: Basement 2 up to the 30th floor. The current floor is kept if it sits outside that range. */
export function floorChoices(current?: number | null): { value: string; label: string }[] {
  const numbers: number[] = [];
  for (let floor = -2; floor <= 30; floor += 1) numbers.push(floor);
  if (current != null && Number.isFinite(current) && !numbers.includes(current)) numbers.push(current);
  numbers.sort((a, b) => a - b);
  return numbers.map((floor) => ({ value: String(floor), label: floorLabel(floor) }));
}
