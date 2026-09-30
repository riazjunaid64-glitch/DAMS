import { describe, expect, it } from "vitest";
import { headerActions, phoneActions } from "./headerActions.ts";

const base = {
  status: "PaymentPlanActive",
  bookingAmountRequired: 500_000,
  bookingAmountRemaining: 0,
  hasInstallmentSchedule: true,
  outstanding: 8_628_000,
  installmentsPaid: 4,
  installmentsTotal: 13,
};

describe("headerActions", () => {
  it("terms not set: Cancel, and no main button", () => {
    const actions = headerActions({ ...base, status: "AwaitingBookingAmount", bookingAmountRequired: 0, hasInstallmentSchedule: false });
    expect(actions).toEqual({ cancel: true, main: null });
    expect(phoneActions(actions)).toEqual({ extras: ["print", "cancel"], more: "menu" });
  });

  it("awaiting booking amount: Record payment, disabled once nothing is left to record", () => {
    const awaiting = { ...base, status: "AwaitingBookingAmount", hasInstallmentSchedule: false, bookingAmountRemaining: 350_000 };
    const actions = headerActions(awaiting);
    expect(actions.cancel).toBe(true);
    expect(actions.main).toEqual({ kind: "recordPayment", label: "Record payment", disabled: false });
    expect(headerActions({ ...awaiting, bookingAmountRemaining: 0 }).main?.disabled).toBe(true);
  });

  it("payment plan without a plan yet: no main button, unless nothing is left to pay", () => {
    expect(headerActions({ ...base, hasInstallmentSchedule: false }).main).toBeNull();
    expect(headerActions({ ...base, hasInstallmentSchedule: false, outstanding: 0 }).main?.kind).toBe("givePossession");
  });

  it("payment plan with a plan: Give possession, with ⋯ on the phone", () => {
    const actions = headerActions(base);
    expect(actions.main?.kind).toBe("givePossession");
    expect(phoneActions(actions).more).toBe("menu");
  });

  it("possession given: Complete sale, disabled while anything is owed or unpaid", () => {
    const owed = headerActions({ ...base, status: "PossessionGiven" });
    expect(owed).toEqual({ cancel: false, main: { kind: "completeSale", label: "Complete sale", disabled: true } });
    expect(phoneActions(owed)).toEqual({ extras: ["print"], more: "printer" });

    const unpaidRow = headerActions({ ...base, status: "PossessionGiven", outstanding: 0, installmentsPaid: 12 });
    expect(unpaidRow.main?.disabled).toBe(true);

    const settled = headerActions({ ...base, status: "PossessionGiven", outstanding: 0, installmentsPaid: 13 });
    expect(settled.main?.disabled).toBe(false);
  });

  it("sale completed and cancelled: Print only, as a printer icon on the phone", () => {
    for (const status of ["SaleCompleted", "Cancelled"]) {
      const actions = headerActions({ ...base, status });
      expect(actions).toEqual({ cancel: false, main: null });
      expect(phoneActions(actions)).toEqual({ extras: ["print"], more: "printer" });
    }
  });
});
