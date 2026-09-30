// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DatePicker, type DatePickerProps } from "./DatePicker.tsx";
import { TimePicker } from "./TimePicker.tsx";

beforeEach(() => {
  // Desktop layout; 2026-09-29 12:00 in Pakistan.
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

function Harness(props: Partial<DatePickerProps> & { start?: string }) {
  const [value, setValue] = useState(props.start ?? "");
  return (
    <>
      <DatePicker label="Payment date" {...props} value={value} onChange={setValue} />
      <output data-testid="value">{value}</output>
    </>
  );
}

const value = () => screen.getByTestId("value").textContent;

describe("DatePicker", () => {
  it("shows the date as 'Sep 29, 2026' and a placeholder when empty", () => {
    render(<Harness start="2026-09-29" />);
    expect(screen.getByRole("button", { name: /Payment date/ }).textContent).toContain("Sep 29, 2026");
    cleanup();
    render(<Harness />);
    expect(screen.getByRole("button", { name: /Payment date/ }).textContent).toContain("Select date");
  });

  it("picks a day, keeps YYYY-MM-DD and closes", () => {
    render(<Harness start="2026-09-29" />);
    fireEvent.click(screen.getByRole("button", { name: /Payment date/ }));
    fireEvent.click(screen.getByRole("button", { name: "September 15, 2026" }));
    expect(value()).toBe("2026-09-15");
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("does not let a day past max be picked", () => {
    render(<Harness start="2026-09-29" max="2026-09-29" />);
    fireEvent.click(screen.getByRole("button", { name: /Payment date/ }));
    const blocked = screen.getByRole("button", { name: "September 30, 2026" }) as HTMLButtonElement;
    expect(blocked.disabled).toBe(true);
    fireEvent.click(blocked);
    expect(value()).toBe("2026-09-29");
  });

  it("jumps to a year and month from the month title", () => {
    render(<Harness start="1988-03-14" label="Date of birth" />);
    fireEvent.click(screen.getByRole("button", { name: /Date of birth/ }));
    fireEvent.click(screen.getByRole("button", { name: /March 1988, choose a year/ }));
    fireEvent.click(screen.getByRole("button", { name: "1990" }));
    fireEvent.click(screen.getByRole("button", { name: "Jul" }));
    fireEvent.click(screen.getByRole("button", { name: "July 4, 1990" }));
    expect(value()).toBe("1990-07-04");
  });

  it("Today picks the Pakistan date and Clear empties an optional field", () => {
    render(<Harness start="2026-01-05" />);
    fireEvent.click(screen.getByRole("button", { name: /Payment date/ }));
    fireEvent.click(screen.getByRole("button", { name: "Today" }));
    expect(value()).toBe("2026-09-29");
    fireEvent.click(screen.getByRole("button", { name: /Payment date/ }));
    fireEvent.click(screen.getByRole("button", { name: "Clear" }));
    expect(value()).toBe("");
  });

  it("has no Clear on a required field", () => {
    render(<Harness start="2026-09-29" required />);
    fireEvent.click(screen.getByRole("button", { name: /Payment date/ }));
    expect(screen.queryByRole("button", { name: "Clear" })).toBeNull();
  });

  it("moves between days with the arrow keys and picks with Enter", () => {
    render(<Harness start="2026-09-29" />);
    const trigger = screen.getByRole("button", { name: /Payment date/ });
    fireEvent.click(trigger);
    const grid = screen.getByRole("grid");
    fireEvent.keyDown(grid, { key: "ArrowRight" });
    expect(document.activeElement?.getAttribute("data-iso")).toBe("2026-09-30");
    fireEvent.keyDown(grid, { key: "ArrowDown" });
    expect(document.activeElement?.getAttribute("data-iso")).toBe("2026-10-07");
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(value()).toBe("2026-09-29");
  });

  it("stays shut when locked", () => {
    render(<Harness start="2026-09-29" locked />);
    const trigger = screen.getByRole("button", { name: /Payment date/ }) as HTMLButtonElement;
    expect(trigger.disabled).toBe(true);
    fireEvent.click(trigger);
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});

function TimeHarness({ start = "" }: { start?: string }) {
  const [time, setTime] = useState(start);
  return (
    <>
      <TimePicker label="Time" value={time} onChange={setTime} />
      <output data-testid="time">{time}</output>
    </>
  );
}

describe("TimePicker", () => {
  it("shows 'HH:mm' as '11:30 AM'", () => {
    render(<TimeHarness start="11:30" />);
    expect((screen.getByLabelText("Time") as HTMLInputElement).value).toBe("11:30 AM");
  });

  it("picks from the 30-minute list", () => {
    render(<TimeHarness start="11:30" />);
    fireEvent.click(screen.getByLabelText("Time"));
    fireEvent.click(screen.getByRole("option", { name: "1:30 PM" }));
    expect(screen.getByTestId("time").textContent).toBe("13:30");
  });

  it("takes a typed time such as 11:15 AM", () => {
    render(<TimeHarness />);
    const box = screen.getByLabelText("Time");
    fireEvent.change(box, { target: { value: "11:15 am" } });
    fireEvent.keyDown(box, { key: "Enter" });
    expect(screen.getByTestId("time").textContent).toBe("11:15");
    expect((box as HTMLInputElement).value).toBe("11:15 AM");
  });

  it("drops half-typed text on Esc", () => {
    render(<TimeHarness start="09:00" />);
    const box = screen.getByLabelText("Time");
    fireEvent.change(box, { target: { value: "11:15 am" } });
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.getByTestId("time").textContent).toBe("09:00");
    expect((box as HTMLInputElement).value).toBe("9:00 AM");
  });

  it("drops text that is not a time", () => {
    render(<TimeHarness start="09:00" />);
    const box = screen.getByLabelText("Time");
    fireEvent.change(box, { target: { value: "later" } });
    fireEvent.keyDown(box, { key: "Tab" });
    expect(screen.getByTestId("time").textContent).toBe("09:00");
    expect((box as HTMLInputElement).value).toBe("9:00 AM");
  });
});
