import { describe, expect, it } from "vitest";
import { financeRangeError } from "./financeChartData";

/**
 * A dashboard date filter is both ends or neither, and never backwards.
 *
 * Neither rule existed before, and the two halves of the screen disagreed about what to do without
 * them: the cards took a lone From as "1 August onward", while the chart saw an incomplete custom
 * range and drew all time; a From after its To was sent to the cards as an inverted range and read
 * by the chart as the configured financial year. Either way the totals and the bars beneath them
 * described different periods, with nothing on screen admitting it. The same check runs again in
 * the controller — this one is only about not sending the request in the first place.
 */
describe("financeRangeError", () => {
  it("accepts both ends, or neither", () => {
    expect(financeRangeError("", "")).toBeNull();
    expect(financeRangeError("2026-08-01", "2026-08-31")).toBeNull();
    // A single day is a legitimate range, not a half-open one.
    expect(financeRangeError("2026-08-01", "2026-08-01")).toBeNull();
  });

  it("refuses one end on its own, whichever end it is", () => {
    expect(financeRangeError("2026-08-01", "")).toMatch(/both a From and a To/i);
    expect(financeRangeError("", "2026-08-31")).toMatch(/both a From and a To/i);
  });

  it("refuses a backwards range instead of reinterpreting it", () => {
    expect(financeRangeError("2026-08-31", "2026-08-01")).toMatch(/cannot be after/i);
  });

  it("compares dates as dates, not as lengths", () => {
    // Lexicographic order is the calendar order for yyyy-mm-dd, and that is why the format is
    // fixed: "2026-09-01" > "2026-08-31" holds as text only because the parts are zero-padded.
    expect(financeRangeError("2026-08-31", "2026-09-01")).toBeNull();
    expect(financeRangeError("2026-09-01", "2026-08-31")).toMatch(/cannot be after/i);
  });
});
