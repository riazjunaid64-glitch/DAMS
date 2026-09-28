import { describe, expect, it } from "vitest";
import {
  addFloorRow,
  createFloors,
  floorRowErrors,
  floorSummary,
  floorsPayload,
  quickSetupFrom,
  rowsFromFloors,
  sortRows,
  standardFloors,
  unitCountLabel,
  type ProjectFloor,
} from "./floorRules.ts";

const floria: ProjectFloor[] = [
  { number: 1, name: "1st floor", unitCount: 4 },
  { number: -1, name: "Parking", unitCount: 1 },
  { number: -2, name: "Basement 2", unitCount: 0 },
  { number: 0, name: "Ground floor", unitCount: 0 },
];

const shown = (rows: { number: string; name: string }[]) => rows.map((row) => `${row.number} ${row.name}`);

describe("Create floors", () => {
  it("fills an empty list with the standard names, bottom to top", () => {
    expect(shown(createFloors([], 2, 3))).toEqual([
      "-2 Basement 2",
      "-1 Basement 1",
      "0 Ground floor",
      "1 1st floor",
      "2 2nd floor",
      "3 3rd floor",
    ]);
    expect(shown(createFloors([], 0, 1))).toEqual(["0 Ground floor", "1 1st floor"]);
  });

  it("keeps every floor that has units, with its own name, and rebuilds the rest", () => {
    const rows = rowsFromFloors(floria).map((row) => (row.number === "0" ? { ...row, name: "Lobby" } : row));
    const rebuilt = createFloors(rows, 1, 2);
    expect(shown(rebuilt)).toEqual(["-1 Parking", "0 Ground floor", "1 1st floor", "2 2nd floor"]);
    expect(rebuilt.find((row) => row.number === "-1")?.unitCount).toBe(1);
    expect(rebuilt.find((row) => row.number === "1")?.savedNumber).toBe(1);
  });
});

describe("floor rows", () => {
  it("sorts by number and reads the quick setup back from the list", () => {
    expect(shown(rowsFromFloors(floria))).toEqual(["-2 Basement 2", "-1 Parking", "0 Ground floor", "1 1st floor"]);
    expect(quickSetupFrom(floria)).toEqual({ basements: "2", aboveGround: "1" });
    expect(quickSetupFrom([])).toEqual({ basements: "", aboveGround: "" });
    expect(quickSetupFrom([...standardFloors(2, 18), { number: 19, name: "Rooftop" }])).toEqual({ basements: "2", aboveGround: "18" });
  });

  it("adds an empty row with the next free number, and re-sorts once a number is typed", () => {
    const added = addFloorRow(rowsFromFloors(floria));
    expect(added.at(-1)).toMatchObject({ number: "2", name: "", unitCount: 0, savedNumber: null });
    expect(addFloorRow([])[0]?.number).toBe("0");

    const typed = added.map((row, index) => (index === added.length - 1 ? { ...row, number: "-3" } : row));
    expect(sortRows(typed)[0]?.number).toBe("-3");
  });

  it("marks a duplicate number, a duplicate name, an empty name and a locked number that changed", () => {
    const rows = rowsFromFloors(floria);
    const [b2, parking, ground, first] = rows;
    const broken = [
      b2!,
      { ...parking!, name: "  " },
      { ...ground!, number: "-2" },
      { ...first!, number: "6" },
      { key: "extra", number: "7", name: "basement 2", unitCount: 0, savedNumber: null },
      { key: "high", number: "201", name: "Sky", unitCount: 0, savedNumber: null },
    ];
    const errors = floorRowErrors(broken);
    expect(errors[b2!.key]).toBeUndefined();
    expect(errors[parking!.key]).toEqual({ field: "name", message: "Enter a floor name." });
    expect(errors[ground!.key]).toEqual({ field: "number", message: "Floor number -2 is used twice." });
    expect(errors[first!.key]).toEqual({ field: "number", message: "1st floor has 4 units, so its number can't change. Move the units first." });
    expect(errors.extra).toEqual({ field: "name", message: "\"basement 2\" is used for two floors." });
    expect(errors.high).toEqual({ field: "number", message: "Enter a floor number from -10 to 200." });
    expect(floorRowErrors(rows)).toEqual({});
  });

  it("sends trimmed names and numbers, bottom to top", () => {
    const rows = rowsFromFloors(floria).map((row) => ({ ...row, name: ` ${row.name} ` }));
    expect(floorsPayload(rows.reverse())).toEqual([
      { number: -2, name: "Basement 2" },
      { number: -1, name: "Parking" },
      { number: 0, name: "Ground floor" },
      { number: 1, name: "1st floor" },
    ]);
  });

  it("counts units", () => {
    expect([0, 1, 4].map(unitCountLabel)).toEqual(["No units", "1 unit", "4 units"]);
  });
});

describe("Overview floors summary", () => {
  it("counts basements, ground and standard floors, then adds custom-named floors", () => {
    const floors = [
      { number: -1, name: "Parking" },
      ...standardFloors(2, 18).filter((floor) => floor.number !== -1),
      { number: 19, name: "Rooftop" },
    ];
    expect(floorSummary(floors)).toBe("2 basements · Ground · 18 floors · Rooftop");
  });

  it("uses the singular and skips what the building does not have", () => {
    expect(floorSummary([{ number: -1, name: "Parking" }, { number: 1, name: "1st floor" }])).toBe("1 basement · 1 floor");
    expect(floorSummary([{ number: 0, name: "Lobby" }])).toBe("Ground");
  });

  it("says Not set up without a list", () => {
    expect(floorSummary([])).toBe("Not set up");
    expect(floorSummary(undefined)).toBe("Not set up");
  });
});
