// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { WhtPayableSummary } from "../whtTypes.ts";
import { PeriodLine, SummaryCards } from "./Summary.tsx";

afterEach(cleanup);

const summary: WhtPayableSummary = {
  openingPayable: 0, totalWithheldAllTime: 1_245_850, totalDepositedAllTime: 1_061_500, outstandingPayable: 184_350,
  withheldInPeriod: 412_300, depositedInPeriod: 412_000, paymentCount: 18, vendorCount: 6, bySection: [],
};

const line = () => screen.getByText(/In the selected period/).textContent;

describe("the period line", () => {
  it("reads as one sentence with the supplier count it is given", () => {
    render(<PeriodLine summary={summary} supplierCount={6} />);
    expect(line()).toBe("In the selected period: withheld Rs 412,300 from 18 payments to 6 suppliers · deposited Rs 412,000");
  });

  it("says '1 payment' and '1 supplier'", () => {
    render(<PeriodLine summary={{ ...summary, paymentCount: 1 }} supplierCount={1} />);
    expect(line()).toBe("In the selected period: withheld Rs 412,300 from 1 payment to 1 supplier · deposited Rs 412,000");
  });

  it("counts the rows it is given, not the card's own supplier count", () => {
    expect(summary.vendorCount).toBe(6);
    render(<PeriodLine summary={summary} supplierCount={4} />);
    expect(line()).toContain("to 4 suppliers");
    expect(line()).not.toContain("6 suppliers");
  });

  it("leaves the supplier clause out until the list has answered", () => {
    render(<PeriodLine summary={summary} supplierCount={null} />);
    expect(line()).toBe("In the selected period: withheld Rs 412,300 from 18 payments · deposited Rs 412,000");
  });

  it("makes the amounts bold", () => {
    render(<PeriodLine summary={summary} supplierCount={6} />);
    expect([...document.querySelectorAll("b")].map((node) => node.textContent)).toEqual(["Rs 412,300", "Rs 412,000"]);
  });
});

describe("the four cards", () => {
  it("shows the server's figures with their notes, and the gold card last", () => {
    render(<SummaryCards summary={summary} state="ready" error={null} onRetry={() => {}} />);
    for (const text of ["Brought forward", "Rs 0", "Tax owed before go-live", "Withheld from suppliers", "Rs 1,245,850", "Kept back when paying suppliers",
      "Deposited to FBR", "Rs 1,061,500", "Paid with challans", "Still owed to FBR", "Rs 184,350", "Sitting in your accounts, not yours to spend"]) {
      expect(screen.getByText(text)).toBeTruthy();
    }
  });

  it("makes the still-owed number gold and the others ink", () => {
    render(<SummaryCards summary={summary} state="ready" error={null} onRetry={() => {}} />);
    expect(screen.getByText("Rs 184,350").className).toContain("text-gold-text");
    expect(screen.getByText("Rs 1,245,850").className).toContain("text-ink");
    expect(screen.getByText("Rs 1,245,850").className).not.toContain("text-gold-text");
  });

  it("shows the brought-forward card even when it is zero", () => {
    render(<SummaryCards summary={summary} state="ready" error={null} onRetry={() => {}} />);
    expect(screen.getByText("Brought forward")).toBeTruthy();
  });

  it("shows the server's still-owed figure rather than working it out", () => {
    render(<SummaryCards summary={{ ...summary, outstandingPayable: 777 }} state="ready" error={null} onRetry={() => {}} />);
    expect(screen.getByText("Rs 777")).toBeTruthy();
    expect(screen.queryByText("Rs 184,350")).toBeNull();
  });

  it("hides the signs from assistive technology", () => {
    render(<SummaryCards summary={summary} state="ready" error={null} onRetry={() => {}} />);
    const signs = [...document.querySelectorAll('[aria-hidden="true"]')].map((node) => node.textContent).filter((text) => ["+", "−", "="].includes(text ?? ""));
    expect(signs).toEqual(["+", "−", "="]);
  });

  it("never shows Rs 0 before the figures have arrived", () => {
    render(<SummaryCards summary={null} state="loading" error={null} onRetry={() => {}} />);
    expect(screen.queryByText(/Rs\s?\d/)).toBeNull();
    expect(screen.queryByText("Tax owed before go-live")).toBeNull();
  });

  it("shows a dash and a Try again button when the figures failed", () => {
    const retry = vi.fn();
    render(<SummaryCards summary={null} state="error" error="The withholding position could not be loaded." onRetry={retry} />);
    expect(screen.queryByText(/Rs\s?\d/)).toBeNull();
    expect(screen.getAllByText("—")).toHaveLength(4);
    expect(screen.getByRole("alert").textContent).toContain("The withholding position could not be loaded.");
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(retry).toHaveBeenCalledTimes(1);
  });
});
