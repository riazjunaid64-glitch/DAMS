import { describe, expect, it } from "vitest";
import { CNIC_ERROR, MOBILE_ERROR, customerFormErrors, emptyCustomerForm } from "./customerForm.ts";

describe("customerFormErrors", () => {
  it("asks for a name and a valid mobile", () => {
    const errors = customerFormErrors({ ...emptyCustomerForm(), fullName: "", mobile: "12345" });
    expect(errors.fullName).toBeTruthy();
    expect(errors.mobile).toBe(MOBILE_ERROR);
  });

  it("accepts a passport-shaped CNIC and refuses a broken digit CNIC", () => {
    expect(customerFormErrors({ ...emptyCustomerForm(), fullName: "Hamza", mobile: "03001234567", cnic: "AB1234567" })).toEqual({});
    expect(customerFormErrors({ ...emptyCustomerForm(), fullName: "Hamza", mobile: "03001234567", cnic: "37405-7654321" }).cnic).toBe(CNIC_ERROR);
  });
});
