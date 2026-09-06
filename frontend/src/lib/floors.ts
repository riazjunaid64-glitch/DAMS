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
