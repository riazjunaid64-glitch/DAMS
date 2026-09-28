import { describe, expect, it } from "vitest";
import { floorChoices, floorLabel, floorName } from "./floors.ts";

describe("floorLabel", () => {
  it("writes basements, the ground floor, and ordinals", () => {
    expect(floorLabel(-2)).toBe("Basement 2");
    expect(floorLabel(-1)).toBe("Basement 1");
    expect(floorLabel(0)).toBe("Ground floor");
    expect(floorLabel(1)).toBe("1st floor");
    expect(floorLabel(2)).toBe("2nd floor");
    expect(floorLabel(3)).toBe("3rd floor");
    expect(floorLabel(8)).toBe("8th floor");
    expect(floorLabel(11)).toBe("11th floor");
    expect(floorLabel(12)).toBe("12th floor");
    expect(floorLabel(13)).toBe("13th floor");
    expect(floorLabel(21)).toBe("21st floor");
    expect(floorLabel(30)).toBe("30th floor");
  });

  it("offers basement 2 through the 30th floor, and keeps an unusual current floor", () => {
    const choices = floorChoices();
    expect(choices[0]).toEqual({ value: "-2", label: "Basement 2" });
    expect(choices.find((choice) => choice.value === "0")?.label).toBe("Ground floor");
    expect(choices.at(-1)).toEqual({ value: "30", label: "30th floor" });
    expect(floorChoices(40).some((choice) => choice.value === "40")).toBe(true);
  });

  it("offers the project's own floors when it has a list, keeping a current floor that is not on it", () => {
    const floors = [{ number: 1, name: "1st floor" }, { number: -1, name: "Parking" }, { number: 19, name: "Rooftop" }];
    expect(floorChoices(null, floors)).toEqual([
      { value: "-1", label: "Parking" },
      { value: "1", label: "1st floor" },
      { value: "19", label: "Rooftop" },
    ]);
    expect(floorChoices(5, floors).map((choice) => choice.label)).toEqual(["Parking", "1st floor", "5th floor", "Rooftop"]);
    expect(floorChoices(null, [])).toHaveLength(33);
  });

  it("shows the API's floor name, and the standard label only when it is missing", () => {
    expect(floorName("Parking", -1)).toBe("Parking");
    expect(floorName("", -1)).toBe("Basement 1");
    expect(floorName(undefined, 0)).toBe("Ground floor");
  });
});
