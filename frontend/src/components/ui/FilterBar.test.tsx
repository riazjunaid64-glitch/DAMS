// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { buildPeriodRange } from "../../lib/financePeriods.ts";
import { FilterBar, type FilterValues } from "./FilterBar.tsx";
import { activeFilterCount } from "./filterCount.ts";

const NOW = new Date(2026, 6, 15);

function rangeFor(preset: "today" | "month" | "lastMonth" | "year" | "lastYear" | "all") {
  return buildPeriodRange(preset, 7, NOW);
}

beforeEach(() => {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: false, media: query, addEventListener: () => {}, removeEventListener: () => {} }));
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-09-29T07:00:00Z"));
  Element.prototype.scrollIntoView = () => {};
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function Harness({
  initial = {},
  search = "",
  financialYear = "ready" as "ready" | "loading" | "error",
  withPeriod = true,
  dateMax,
  dateRequired,
}: {
  initial?: FilterValues;
  search?: string;
  financialYear?: "ready" | "loading" | "error";
  withPeriod?: boolean;
  dateMax?: string;
  dateRequired?: boolean;
}) {
  const [values, setValues] = useState<FilterValues>({ status: "", from: "", to: "", ...initial });
  const [query, setQuery] = useState(search);
  return (
    <>
      <FilterBar
        search={{ value: query, onSearch: setQuery }}
        filters={[
          ...(withPeriod ? [{ type: "period" as const, key: "period", fromKey: "from", toKey: "to", rangeFor, financialYear }] : []),
          { type: "dateRange" as const, fromKey: "from", toKey: "to", max: dateMax, required: dateRequired },
          { type: "select" as const, key: "status", label: "Status", options: [{ value: "open", label: "Open" }] },
        ]}
        values={values}
        onChange={(changes) => setValues((current) => ({ ...current, ...changes }))}
        onReset={() => { setQuery(""); setValues({ status: "", from: "", to: "" }); }}
      />
      <output data-testid="from">{values.from}</output>
      <output data-testid="to">{values.to}</output>
    </>
  );
}

const fromValue = () => screen.getByTestId("from").textContent;
const toValue = () => screen.getByTestId("to").textContent;

describe("FilterBar date range", () => {
  it("counts a range once and shows Reset only when something is set", () => {
    expect(activeFilterCount(
      [
        { type: "period", key: "period", fromKey: "from", toKey: "to", rangeFor },
        { type: "dateRange", fromKey: "from", toKey: "to" },
        { type: "select", key: "status", label: "Status", options: [] },
      ],
      { from: "2026-07-01", to: "2027-06-30", status: "open" },
    )).toBe(2);
    render(<Harness />);
    expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();
    cleanup();
    render(<Harness search="ali" />);
    expect(screen.getByRole("button", { name: "Reset" })).toBeTruthy();
    cleanup();
    render(<Harness initial={{ from: "2026-07-01", to: "2027-06-30" }} />);
    expect(screen.getByRole("button", { name: "Filters (1 applied)" })).toBeTruthy();
  });

  it("waits for both dates, then applies the pair", () => {
    render(<Harness withPeriod={false} />);
    fireEvent.click(screen.getByRole("button", { name: /From/ }));
    fireEvent.click(screen.getByRole("button", { name: "September 15, 2026" }));
    expect(screen.getByRole("alert").textContent).toBe("Enter both a From and a To date, or clear them both.");
    expect(fromValue()).toBe("");
    expect(toValue()).toBe("");
    fireEvent.click(screen.getByRole("button", { name: /To/ }));
    fireEvent.click(screen.getByRole("button", { name: "September 20, 2026" }));
    expect(screen.queryByRole("alert")).toBeNull();
    expect(fromValue()).toBe("2026-09-15");
    expect(toValue()).toBe("2026-09-20");
  });

  it("refuses a From date after To and sends nothing", () => {
    render(<Harness withPeriod={false} initial={{ from: "2026-09-01", to: "2026-09-10" }} />);
    fireEvent.click(screen.getByRole("button", { name: /From/ }));
    fireEvent.click(screen.getByRole("button", { name: "September 20, 2026" }));
    expect(screen.getByRole("alert").textContent).toBe("From date cannot be after To date.");
    expect(fromValue()).toBe("2026-09-01");
    expect(toValue()).toBe("2026-09-10");
  });

  it("passes max and required through to the date picker", () => {
    render(<Harness withPeriod={false} dateMax="2026-09-29" dateRequired />);
    fireEvent.click(screen.getByRole("button", { name: /From/ }));
    expect(screen.queryByRole("button", { name: "Clear" })).toBeNull();
    expect((screen.getByRole("button", { name: "September 30, 2026" }) as HTMLButtonElement).disabled).toBe(true);
  });
});

describe("FilterBar period", () => {
  it("fills the dates from a preset, shows Custom when they match none, and All time clears them", () => {
    render(<Harness />);
    fireEvent.click(screen.getByRole("combobox", { name: /Period/ }));
    fireEvent.click(screen.getByRole("option", { name: "This financial year (Jul 2026 – Jun 2027)" }));
    expect(fromValue()).toBe("2026-07-01");
    expect(toValue()).toBe("2027-06-30");
    expect(screen.getByRole("combobox", { name: /Period/ }).textContent).toContain("This financial year");

    fireEvent.click(screen.getByRole("button", { name: /From/ }));
    fireEvent.click(screen.getByRole("button", { name: /July 2026, choose a year/ }));
    fireEvent.click(screen.getByRole("button", { name: "2026" }));
    fireEvent.click(screen.getByRole("button", { name: "Aug" }));
    fireEvent.click(screen.getByRole("button", { name: "August 2, 2026" }));
    fireEvent.click(screen.getByRole("button", { name: /To/ }));
    fireEvent.click(screen.getByRole("button", { name: /June 2027, choose a year/ }));
    fireEvent.click(screen.getByRole("button", { name: "2026" }));
    fireEvent.click(screen.getByRole("button", { name: "Aug" }));
    fireEvent.click(screen.getByRole("button", { name: "August 20, 2026" }));
    expect(fromValue()).toBe("2026-08-02");
    expect(toValue()).toBe("2026-08-20");
    expect(screen.getByRole("combobox", { name: /Period/ }).textContent).toContain("Custom");

    fireEvent.click(screen.getByRole("combobox", { name: /Period/ }));
    fireEvent.click(screen.getByRole("option", { name: "All time" }));
    expect(fromValue()).toBe("");
    expect(toValue()).toBe("");
    expect(screen.getByRole("combobox", { name: /Period/ }).textContent).toContain("All time");
  });

  it("keeps the financial year options disabled until the start month is known", () => {
    render(<Harness financialYear="loading" />);
    expect(screen.getByText("Loading the configured financial year…")).toBeTruthy();
    fireEvent.click(screen.getByRole("combobox", { name: /Period/ }));
    expect(screen.getByRole("option", { name: "This financial year" }).getAttribute("aria-disabled")).toBe("true");
    expect(screen.getByRole("option", { name: "Last financial year" }).getAttribute("aria-disabled")).toBe("true");
    fireEvent.click(screen.getByRole("option", { name: "This financial year" }));
    expect(fromValue()).toBe("");
    cleanup();

    render(<Harness financialYear="error" />);
    expect(screen.getByText("The financial year setting could not be loaded, so this range cannot be applied.")).toBeTruthy();
  });

  it("resets the period with the dates from the phone sheet", () => {
    render(<Harness initial={{ from: "2026-07-01", to: "2027-06-30" }} />);
    fireEvent.click(screen.getByRole("button", { name: "Filters (1 applied)" }));
    const sheet = screen.getByRole("dialog");
    expect(within(sheet).getByRole("combobox", { name: /Period/ }).textContent).toContain("This financial year");
    fireEvent.click(within(sheet).getByRole("button", { name: "Reset" }));
    expect(fromValue()).toBe("");
    expect(toValue()).toBe("");
    expect(screen.getByRole("combobox", { name: /Period/ }).textContent).toContain("All time");
  });
});

describe("FilterBar defaults and limited Period", () => {
  const defaults = { from: "2026-07-01", to: "2027-06-30", status: "" };

  function DefaultsHarness({ onRangeError, initial }: { onRangeError?: (message: string | null) => void; initial?: FilterValues }) {
    const [values, setValues] = useState<FilterValues>({ ...defaults, ...initial });
    return (
      <FilterBar
        filters={[
          { type: "period", key: "period", fromKey: "from", toKey: "to", rangeFor, presets: ["year", "lastYear"], customLabel: "Custom dates", monthsInLabel: false },
          { type: "dateRange", fromKey: "from", toKey: "to" },
          { type: "select", key: "status", label: "Status", options: [{ value: "open", label: "Open" }] },
        ]}
        values={values}
        defaults={defaults}
        onChange={(changes) => setValues((current) => ({ ...current, ...changes }))}
        onReset={() => setValues({ ...defaults })}
        onRangeError={onRangeError}
      />
    );
  }

  it("counts a filter only when it differs from its default", () => {
    const filters = [
      { type: "dateRange" as const, fromKey: "from", toKey: "to" },
      { type: "select" as const, key: "status", label: "Status", options: [] },
    ];
    expect(activeFilterCount(filters, { from: "2026-07-01", to: "2027-06-30", status: "" }, defaults)).toBe(0);
    expect(activeFilterCount(filters, { from: "2026-08-01", to: "2027-06-30", status: "" }, defaults)).toBe(1);
    expect(activeFilterCount(filters, { from: "2026-08-01", to: "2027-06-30", status: "open" }, defaults)).toBe(2);
  });

  it("hides Reset on the defaults and shows it once something changes", () => {
    render(<DefaultsHarness />);
    expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();
    fireEvent.click(screen.getByRole("combobox", { name: /Status/ }));
    fireEvent.click(screen.getByRole("option", { name: "Open" }));
    fireEvent.click(screen.getByRole("button", { name: "Reset" }));
    expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();
  });

  it("offers only the chosen presets and Custom dates, and reads other dates as Custom dates", () => {
    render(<DefaultsHarness />);
    const period = screen.getByRole("combobox", { name: /Period/ });
    expect(period.textContent).toContain("This financial year");
    fireEvent.click(period);
    const names = screen.getAllByRole("option").map((option) => option.textContent);
    expect(names).toEqual(["This financial year", "Last financial year", "Custom dates"]);
  });

  it("reports the pair error and clears it once the pair is valid", () => {
    const seen: (string | null)[] = [];
    render(<DefaultsHarness initial={{ from: "2026-09-01", to: "2026-09-10" }} onRangeError={(message) => seen.push(message)} />);
    expect(seen[seen.length - 1]).toBe(null);
    fireEvent.click(screen.getByRole("button", { name: /From/ }));
    fireEvent.click(screen.getByRole("button", { name: "September 20, 2026" }));
    expect(seen[seen.length - 1]).toBe("From date cannot be after To date.");
    fireEvent.click(screen.getByRole("button", { name: /To/ }));
    fireEvent.click(screen.getByRole("button", { name: "September 25, 2026" }));
    expect(seen[seen.length - 1]).toBe(null);
  });
});

describe("FilterBar conditional filters", () => {
  const filters = [
    { type: "select" as const, key: "mode", label: "Show", allLabel: "As at", options: [{ value: "range", label: "Date range" }] },
    { type: "date" as const, key: "asAt", label: "As at", when: (v: FilterValues) => v.mode !== "range" },
    { type: "dateRange" as const, fromKey: "from", toKey: "to", when: (v: FilterValues) => v.mode === "range" },
  ];
  const defaults = { mode: "", asAt: "2026-09-29", from: "2026-09-01", to: "2026-09-29" };

  function Conditional() {
    const [values, setValues] = useState<FilterValues>({ ...defaults });
    return (
      <FilterBar
        filters={filters}
        values={values}
        defaults={defaults}
        onChange={(changes) => setValues((current) => ({ ...current, ...changes }))}
        onReset={() => setValues({ ...defaults })}
      />
    );
  }

  it("shows a filter only while its condition holds, and does not count a hidden one", () => {
    expect(activeFilterCount(filters, { ...defaults, asAt: "2026-09-01" }, defaults)).toBe(1);
    expect(activeFilterCount(filters, { ...defaults, mode: "range", asAt: "2026-09-01" }, defaults)).toBe(1);
    render(<Conditional />);
    expect(screen.getByRole("button", { name: /As at/ })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /From/ })).toBeNull();
    fireEvent.click(screen.getByRole("combobox", { name: /Show/ }));
    fireEvent.click(screen.getByRole("option", { name: "Date range" }));
    expect(screen.getByRole("button", { name: /From/ })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /^As at/ })).toBeNull();
  });

  it("follows the choice made inside the phone sheet before it is applied", () => {
    vi.stubGlobal("matchMedia", (query: string) => ({ matches: false, media: query, addEventListener: () => {}, removeEventListener: () => {} }));
    render(<Conditional />);
    fireEvent.click(screen.getByRole("button", { name: "Filters" }));
    const sheet = within(screen.getByRole("dialog"));
    expect(sheet.queryByRole("button", { name: /From/ })).toBeNull();
    fireEvent.click(sheet.getByRole("combobox", { name: /Show/ }));
    fireEvent.click(screen.getByRole("option", { name: "Date range" }));
    expect(sheet.getByRole("button", { name: /From/ })).toBeTruthy();
    expect(sheet.queryByRole("button", { name: /^As at/ })).toBeNull();
  });
});
