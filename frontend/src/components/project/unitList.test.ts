import { describe, expect, it } from "vitest";
import { availableByType, filterUnits, floorsInUse, sortUnits, type UnitRow } from "./unitList.ts";

const unit = (overrides: Partial<UnitRow>): UnitRow => ({
  id: 1,
  unitNumber: "101",
  unitType: "1 Bed",
  floorNumber: 1,
  size: 800,
  price: 100,
  status: "Available",
  ...overrides,
});

const rows = [
  unit({ id: 1, unitNumber: "2", unitType: "Studio", floorNumber: 0, size: 400, price: 50, status: "Available" }),
  unit({ id: 2, unitNumber: "10", unitType: "2 Bed", floorNumber: 1, size: 1200, price: 200, status: "Booked" }),
  unit({ id: 3, unitNumber: "11", unitType: "1 Bed", floorNumber: -1, size: 900, price: 80, status: "PendingReview" }),
  unit({ id: 4, unitNumber: "A1", unitType: "3 Bed", floorNumber: 8, size: 1500, price: 300, status: "Sold" }),
];

describe("unit list", () => {
  it("treats reserved and payment-plan units as Booked", () => {
    expect(filterUnits(rows, { search: "", type: "", status: "Booked", floor: "" }).map((row) => row.id)).toEqual([2, 3]);
  });

  it("filters by number, type and floor together", () => {
    expect(filterUnits(rows, { search: "1", type: "1 Bed", status: "", floor: "-1" }).map((row) => row.id)).toEqual([3]);
  });

  it("sorts unit numbers numerically, and price or size when asked", () => {
    expect(sortUnits(rows, "").map((row) => row.unitNumber)).toEqual(["2", "10", "11", "A1"]);
    expect(sortUnits(rows, "priceAsc").map((row) => row.id)).toEqual([1, 3, 2, 4]);
    expect(sortUnits(rows, "sizeDesc")[0]?.id).toBe(4);
  });

  it("counts available units of each type", () => {
    expect(availableByType(rows)).toEqual([
      { type: "Studio", count: 1 },
      { type: "1 Bed", count: 0 },
      { type: "2 Bed", count: 0 },
      { type: "3 Bed", count: 0 },
      { type: "Parking space", count: 0 },
    ]);
  });

  it("lists the floors that have a unit, lowest first", () => {
    expect(floorsInUse(rows).map((floor) => floor.label)).toEqual(["Basement 1", "Ground floor", "1st floor", "8th floor"]);
  });
});
