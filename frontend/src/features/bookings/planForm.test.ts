import { describe, expect, it } from "vitest";
import type { BookingDetail, InstallmentSchedule, ScheduleItem } from "./detailTypes.ts";
import {
  canSavePlan, frequencyPhrase, installmentLabel, orderInstallments, phoneInstallments, planErrors, planFields,
  planFigures, planStats,
} from "./planForm.ts";

const fields = (change: Partial<ReturnType<typeof planFields>> = {}) => ({
  agreedSalePrice: "12800000", discountPercent: "0", installments: "12", frequency: "Monthly",
  firstDue: "2026-10-26", possessionAmount: "", possessionDue: "", ...change,
});

const item = (over: Partial<ScheduleItem>): ScheduleItem => ({
  id: 1, sequenceNumber: 1, type: "Regular", dueDate: "2026-10-26T00:00:00", amount: 918000, status: "Pending",
  amountPaid: 0, remainingBalance: 918000, isOverdue: false, ...over,
});

describe("planFigures", () => {
  it("works out the ticket's example: 12,800,000 less 500,000 is 12,300,000 to schedule", () => {
    const figures = planFigures(fields(), 500000, 0);
    expect(figures.toSchedule).toBe(12300000);
    expect(figures.each).toBe(1025000);
  });

  it("takes the possession amount out before dividing: 1,284,000 possession, 12 installments = 918,000 each", () => {
    const figures = planFigures(fields({ possessionAmount: "1284000" }), 500000, 0);
    expect(figures.pool).toBe(11016000);
    expect(figures.each).toBe(918000);
  });

  it("takes the discount and the rebate credits off, and rounds each installment down to the paisa", () => {
    const figures = planFigures(fields({ agreedSalePrice: "1000000", discountPercent: "10", installments: "7" }), 100000, 50000);
    expect(figures.discount).toBe(100000);
    expect(figures.toSchedule).toBe(750000);
    expect(figures.each).toBe(107142.85);
  });
});

describe("planErrors", () => {
  const errorsFor = (change: Partial<ReturnType<typeof planFields>>, received = 500000, credits = 0) => {
    const f = fields(change);
    return planErrors(f, planFigures(f, received, credits));
  };

  it("accepts the ticket's example", () => {
    expect(canSavePlan(errorsFor({ possessionAmount: "1284000", possessionDue: "2027-11-01" }))).toBe(true);
  });

  it("needs the first due date", () => {
    expect(errorsFor({ firstDue: "" }).firstDue).toBeTruthy();
  });

  it("needs a possession due date whenever there is a possession amount", () => {
    expect(errorsFor({ possessionAmount: "100000" }).possessionDue).toBeTruthy();
    expect(errorsFor({ possessionAmount: "" }).possessionDue).toBeUndefined();
  });

  it("refuses a count that is not a whole number from 1 to 600", () => {
    for (const bad of ["0", "1.5", "601", ""]) expect(errorsFor({ installments: bad }).installments).toBeTruthy();
    expect(errorsFor({ installments: "600" }).installments).toBeUndefined();
  });

  it("refuses a possession amount that leaves nothing for the installments", () => {
    expect(errorsFor({ possessionAmount: "12300000", possessionDue: "2027-01-01" }).possession).toBeTruthy();
  });

  it("refuses a plan when nothing is left to schedule", () => {
    expect(errorsFor({}, 12800000).agreed).toBeTruthy();
  });

  it("refuses more installments than there are paisa to share", () => {
    expect(errorsFor({ agreedSalePrice: "500001", installments: "600" }, 500000).installments).toBeTruthy();
  });
});

describe("planFields", () => {
  const booking = { agreedSalePrice: 12800000, discountPercent: 0 } as BookingDetail;

  it("starts a new plan from the booking's terms with no first due date chosen", () => {
    expect(planFields(booking, null)).toMatchObject({ agreedSalePrice: "12800000", installments: "12", frequency: "Monthly", firstDue: "" });
  });

  it("opens Change plan on the plan that is there", () => {
    const schedule = {
      hasSchedule: true, agreedSalePrice: 12800000, discountPercent: 5, numberOfInstallments: 8, frequency: "Quarterly",
      installmentStartDate: "2026-10-26T00:00:00", possessionAmount: 1284000, possessionDueDate: "2027-11-01T00:00:00",
    } as InstallmentSchedule;
    expect(planFields(booking, schedule)).toEqual({
      agreedSalePrice: "12800000", discountPercent: "5", installments: "8", frequency: "Quarterly",
      firstDue: "2026-10-26", possessionAmount: "1284000", possessionDue: "2027-11-01",
    });
  });
});

describe("frequencyPhrase", () => {
  it("names the frequency and the count", () => {
    expect(frequencyPhrase("Monthly", 12)).toBe("12 monthly installments");
    expect(frequencyPhrase("HalfYearly", 4)).toBe("4 half-yearly installments");
    expect(frequencyPhrase("Yearly", 1)).toBe("1 yearly installment");
  });
});

describe("the schedule on screen", () => {
  const rows = [
    item({ id: 1, sequenceNumber: 1, dueDate: "2026-06-26", status: "Paid", amountPaid: 918000, remainingBalance: 0 }),
    item({ id: 2, sequenceNumber: 2, dueDate: "2026-07-26", status: "Paid", amountPaid: 918000, remainingBalance: 0 }),
    item({ id: 3, sequenceNumber: 3, dueDate: "2026-08-26", status: "Paid", amountPaid: 918000, remainingBalance: 0 }),
    item({ id: 4, sequenceNumber: 4, dueDate: "2026-09-26", status: "Overdue", isOverdue: true }),
    item({ id: 5, sequenceNumber: 5, dueDate: "2026-10-26" }),
    item({ id: 6, sequenceNumber: 6, dueDate: "2026-11-26" }),
    item({ id: 7, sequenceNumber: 7, dueDate: "2026-12-26" }),
    item({ id: 0, sequenceNumber: 0, type: "Possession", dueDate: "2026-12-26", amount: 1284000, remainingBalance: 1284000 }),
  ];

  it("orders by due date and puts possession after the regular installment of the same day", () => {
    expect(orderInstallments([...rows].reverse()).map((row) => row.id)).toEqual([1, 2, 3, 4, 5, 6, 7, 0]);
  });

  it("names the rows", () => {
    expect(installmentLabel(rows[3]!)).toBe("Installment 4");
    expect(installmentLabel(rows[7]!)).toBe("Possession");
  });

  it("shows the last two paid and the next unpaid ones, the overdue one first, on a phone", () => {
    expect(phoneInstallments(rows).map((row) => row.id)).toEqual([2, 3, 4, 5, 6]);
  });

  it("reads the stat cards off the schedule", () => {
    const schedule = { items: rows, scheduleTotal: 8000000, schedulePaid: 2754000, scheduleRemaining: 5246000 } as InstallmentSchedule;
    const stats = planStats(schedule);
    expect(stats).toMatchObject({ paidCount: 3, count: 8, overdue: 918000, overdueCount: 1 });
    expect(stats.next?.id).toBe(4);
  });
});
