import { isValidCnic, isValidEmail, isValidPkMobile } from "../../utils/validation.ts";

/** Shared customer fields for New customer, Edit customer, and New booking step 2. */
export type CustomerFormValues = {
  fullName: string;
  guardianName: string;
  mobile: string;
  cnic: string;
  email: string;
  whatsapp: string;
  dob: string;
  nationality: string;
  occupation: string;
  address: string;
  notes: string;
};

export type CustomerFormField = keyof CustomerFormValues;

export const emptyCustomerForm = (): CustomerFormValues => ({
  fullName: "",
  guardianName: "",
  mobile: "",
  cnic: "",
  email: "",
  whatsapp: "",
  dob: "",
  nationality: "",
  occupation: "",
  address: "",
  notes: "",
});

export const MOBILE_ERROR = "Enter a valid mobile number, e.g. 0300 1234567";
export const CNIC_ERROR = "Use 00000-0000000-0 or a passport number";

// A CNIC is digits and dashes; anything else (a passport number) is accepted as typed.
const looksLikeCnic = (text: string) => /^[\d-]+$/.test(text.trim());
export const badCnic = (text: string) => text.trim() !== "" && looksLikeCnic(text) && !isValidCnic(text);
export const badMobile = (text: string) => text.trim() !== "" && !isValidPkMobile(text);

/** Browser checks shared by New customer and New booking step 2. */
export function customerFormErrors(values: CustomerFormValues): Partial<Record<CustomerFormField, string>> {
  const errors: Partial<Record<CustomerFormField, string>> = {};
  if (values.fullName.trim().length < 2) errors.fullName = "Enter the customer's full name.";
  if (values.mobile.trim() === "" || badMobile(values.mobile)) errors.mobile = MOBILE_ERROR;
  if (badCnic(values.cnic)) errors.cnic = CNIC_ERROR;
  if (values.email.trim() !== "" && !isValidEmail(values.email)) {
    errors.email = "Enter a valid email address, e.g. name@example.com";
  }
  if (badMobile(values.whatsapp)) errors.whatsapp = MOBILE_ERROR;
  return errors;
}
