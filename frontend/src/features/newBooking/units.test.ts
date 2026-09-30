import { describe, expect, it } from "vitest";
import { availableUnits, filterUnits, firstUnits, floorFilterOptions, groupByFloor, NO_FILTERS, typeFilterOptions, type PickerUnit } from "./units";

const unit = (id: number, unitNumber: string, floorNumber: number, unitType = "1 Bed", status = "Available"): PickerUnit =>
  ({ id, unitNumber, unitType, floorNumber, floorName: "", size: 864, price: 14_000_000, status });

const all = [unit(1, "811", 8, "2 Bed"), unit(2, "810", 8), unit(3, "901", 9), unit(4, "812", 8, "Studio"), unit(5, "814", 8, "3 Bed", "Booked"), unit(6, "P-2", -1, "Parking space")];

describe("the unit picker", () => {
  it("offers only Available units", () => {
    expect(availableUnits(all).map((u) => u.unitNumber)).not.toContain("814");
    expect(availableUnits(all)).toHaveLength(5);
  });

  it("filters on the device as a number is typed, by any part of it", () => {
    const units = availableUnits(all);
    expect(filterUnits(units, { ...NO_FILTERS, query: "81" }).map((u) => u.unitNumber).sort()).toEqual(["810", "811", "812"]);
    expect(filterUnits(units, { ...NO_FILTERS, query: " 810 " })).toHaveLength(1);
    expect(filterUnits(units, { ...NO_FILTERS, query: "zzz" })).toHaveLength(0);
  });

  it("narrows by floor and by type, together with the search", () => {
    const units = availableUnits(all);
    expect(filterUnits(units, { ...NO_FILTERS, floor: "9" }).map((u) => u.unitNumber)).toEqual(["901"]);
    expect(filterUnits(units, { ...NO_FILTERS, type: "Studio" }).map((u) => u.unitNumber)).toEqual(["812"]);
    expect(filterUnits(units, { query: "81", floor: "8", type: "2 Bed" }).map((u) => u.unitNumber)).toEqual(["811"]);
  });

  it("groups by floor, lowest first, each floor's units in number order, with the floor's name", () => {
    const groups = groupByFloor(availableUnits(all));
    expect(groups.map((g) => g.floorNumber)).toEqual([-1, 8, 9]);
    expect(groups[1]!.units.map((u) => u.unitNumber)).toEqual(["810", "811", "812"]);
    expect(groups[1]!.name).toBe("8th floor");
    expect(groups[0]!.name).toBe("Basement 1");
  });

  it("shows the first N units across floors but keeps each floor's full count for its header", () => {
    const groups = groupByFloor(availableUnits(all));
    const page = firstUnits(groups, 3);
    expect(page.map((p) => p.shown.length)).toEqual([1, 2]);
    expect(page[1]!.group.units).toHaveLength(3);
    expect(firstUnits(groups, 100).reduce((sum, p) => sum + p.shown.length, 0)).toBe(5);
  });

  it("numbers sort naturally, not as text", () => {
    const groups = groupByFloor([unit(1, "9", 1), unit(2, "10", 1), unit(3, "2", 1)]);
    expect(groups[0]!.units.map((u) => u.unitNumber)).toEqual(["2", "9", "10"]);
  });

  it("lists only the floors and types the project has available", () => {
    const units = availableUnits(all);
    expect(floorFilterOptions(units).map((o) => o.value)).toEqual(["-1", "8", "9"]);
    expect(typeFilterOptions(units, ["Studio", "1 Bed", "2 Bed", "3 Bed", "Parking space"]).map((o) => o.value)).toEqual(["Studio", "1 Bed", "2 Bed", "Parking space"]);
  });
});
