import { describe, expect, it } from "vitest";
import { floorChoices, floorLabel } from "./floors.ts";

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
});
