import { pakistanToday } from "../../lib/financePeriods.ts";
import { formatPkr } from "../../utils/currency.ts";
import { isValidCnic, isValidEmail, isValidPkMobile } from "../../utils/validation.ts";
import { formatPhone } from "../bookings/format.ts";
import { referenceRequired } from "../bookings/paymentForm.ts";
import { CUSTOM_CHIP, termsErrors, termsFigures, type TermsFigures } from "../bookings/termsForm.ts";
import type { PickerCustomer } from "./customers.ts";
import type { PickerUnit } from "./units.ts";

export const STEPS = ["Unit", "Customer", "Next of kin", "Price & payment", "Review"] as const;
export type StepIndex = 0 | 1 | 2 | 3 | 4;

export const SOURCES = [
  { value: "WalkIn", label: "Walk-in" },
  { value: "Phone", label: "Phone" },
  { value: "Referral", label: "Referral" },
  { value: "Other", label: "Other" },
];

/** "Payment for" is only the label printed on the application form; it is never the payment method. */
export const PAYMENT_FOR = [
  { value: "Booking", label: "Booking" },
  { value: "Confirmation", label: "Confirmation" },
  { value: "Lump sum", label: "Lump sum" },
];

export const KIN_RELATIONS = ["Wife", "Husband", "Father", "Mother", "Son", "Daughter", "Brother", "Sister", "Other"]
  .map((value) => ({ value, label: value }));

export interface BookingDraft {
  projectId: string;
  projectName: string;
  unit: PickerUnit | null;
  // For the application form
  serialNo: string;
  category: string;
  tower: string;
  isCorner: boolean;
  // Customer
  customerMode: "new" | "existing";
  pickedCustomer: PickerCustomer | null;
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
  // Next of kin
  kinName: string;
  kinRelation: string;
  kinMobile: string;
  kinCnic: string;
  kinDob: string;
  kinAddress: string;
  // Price & payment
  agreedPrice: string;
  pricePerSft: string;
  /** Once the person types a price per sq ft, it stops following the agreed price. */
  pricePerSftEdited: boolean;
  discountPercent: string;
  discountReason: string;
  source: string;
  chip: string;
  customAmount: string;
  referenceId: string;
  received: string;
  accountId: string;
  method: string;
  paymentFor: string;
  reference: string;
  paidOn: string;
}

export function emptyDraft(): BookingDraft {
  return {
    projectId: "", projectName: "", unit: null, serialNo: "", category: "", tower: "", isCorner: false,
    customerMode: "new", pickedCustomer: null, fullName: "", guardianName: "", mobile: "", cnic: "", email: "", whatsapp: "", dob: "",
    nationality: "", occupation: "", address: "",
    kinName: "", kinRelation: "", kinMobile: "", kinCnic: "", kinDob: "", kinAddress: "",
    agreedPrice: "", pricePerSft: "", pricePerSftEdited: false, discountPercent: "", discountReason: "", source: "WalkIn", chip: "10", customAmount: "",
    referenceId: "", received: "", accountId: "", method: "Cash", paymentFor: "Booking", reference: "", paidOn: pakistanToday(),
  };
}

/** Price divided by size, to the paisa; empty when the unit has no size. */
export function perSqFt(price: number, size: number): string {
  return price > 0 && size > 0 ? String(Math.round((price / size) * 100) / 100) : "";
}

/** Picking a unit fills what follows from it: the category, the list price and the price per sq ft. */
export function withUnit(draft: BookingDraft, unit: PickerUnit | null): BookingDraft {
  if (!unit) return { ...draft, unit: null };
  const agreedPrice = String(unit.price);
  return {
    ...draft,
    unit,
    category: unit.unitType,
    agreedPrice,
    pricePerSft: perSqFt(unit.price, unit.size),
    pricePerSftEdited: false,
  };
}

/** Changing the agreed price moves the price per sq ft with it, until the person has set that themselves. */
export function withAgreedPrice(draft: BookingDraft, agreedPrice: string): BookingDraft {
  if (draft.pricePerSftEdited) return { ...draft, agreedPrice };
  return { ...draft, agreedPrice, pricePerSft: perSqFt(Number(agreedPrice) || 0, draft.unit?.size ?? 0) };
}

export const draftFigures = (draft: BookingDraft): TermsFigures =>
  termsFigures({ agreedSalePrice: draft.agreedPrice, discountPercent: draft.discountPercent, chip: draft.chip, customAmount: draft.customAmount });

export const receivedAmount = (draft: BookingDraft): number => (draft.received.trim() === "" ? 0 : Number(draft.received));

/** Anything typed or chosen beyond where the form started means leaving has to be confirmed. */
export const isDirty = (draft: BookingDraft, baseline: BookingDraft): boolean => JSON.stringify(draft) !== JSON.stringify(baseline);

export type DraftErrors = Partial<Record<keyof BookingDraft | "unit" | "customer" | "bookingAmount" | "discount", string>>;

const MOBILE_ERROR = "Enter a valid mobile number, e.g. 0300 1234567";
const CNIC_ERROR = "Use 00000-0000000-0 or a passport number";
// A CNIC is digits and dashes; anything else (a passport number) is accepted as typed.
const looksLikeCnic = (text: string) => /^[\d-]+$/.test(text.trim());
const badCnic = (text: string) => text.trim() !== "" && looksLikeCnic(text) && !isValidCnic(text);
const badMobile = (text: string) => text.trim() !== "" && !isValidPkMobile(text);

/** What is wrong with one step, field by field. Continue stays on the step while this is not empty. */
export function stepErrors(step: StepIndex, draft: BookingDraft): DraftErrors {
  const errors: DraftErrors = {};
  if (step === 0) {
    if (!draft.unit) errors.unit = "Pick a unit.";
  } else if (step === 1) {
    if (draft.customerMode === "existing") {
      if (!draft.pickedCustomer) errors.customer = "Choose a customer.";
    } else {
      if (draft.fullName.trim().length < 2) errors.fullName = "Enter the customer's full name.";
      if (draft.mobile.trim() === "" || badMobile(draft.mobile)) errors.mobile = MOBILE_ERROR;
      if (badCnic(draft.cnic)) errors.cnic = CNIC_ERROR;
      if (draft.email.trim() !== "" && !isValidEmail(draft.email)) errors.email = "Enter a valid email address, e.g. name@example.com";
      if (badMobile(draft.whatsapp)) errors.whatsapp = MOBILE_ERROR;
    }
  } else if (step === 2) {
    if (badMobile(draft.kinMobile)) errors.kinMobile = MOBILE_ERROR;
    if (badCnic(draft.kinCnic)) errors.kinCnic = CNIC_ERROR;
  } else if (step === 3) {
    const figures = draftFigures(draft);
    if (!(figures.agreed > 0)) errors.agreedPrice = "Enter the agreed sale price.";
    const terms = termsErrors(figures, 0);
    if (terms.discount) errors.discount = terms.discount;
    if (!(figures.bookingAmount > 0)) errors.bookingAmount = "Enter the booking amount.";
    else if (terms.amount) errors.bookingAmount = terms.amount;
    if (!draft.source) errors.source = "Choose the source.";

    const received = receivedAmount(draft);
    if (received < 0 || Number.isNaN(received)) errors.received = "Enter the amount received.";
    else if (received > 0) {
      if (!draft.accountId) errors.accountId = "Choose the account it was received in.";
      if (!draft.method) errors.method = "Choose the payment method.";
      if (referenceRequired(draft.method) && !draft.reference.trim()) errors.reference = "Enter the cheque or transfer number.";
      if (!draft.paidOn) errors.paidOn = "Choose the date.";
      else if (draft.paidOn > pakistanToday()) errors.paidOn = "Can't be in the future.";
      if (figures.bookingAmount > 0 && received > figures.bookingAmount) errors.received = `Can't be more than the booking amount, ${formatPkr(figures.bookingAmount)}`;
    }
  }
  return errors;
}

/** The first step that still has something wrong, or null when every step is fine. */
export function firstStepWithErrors(draft: BookingDraft): StepIndex | null {
  for (const step of [0, 1, 2, 3] as const) if (Object.keys(stepErrors(step, draft)).length > 0) return step;
  return null;
}

const text = (value: string): string | null => value.trim() || null;
const numberOrNull = (value: string): number | null => (value.trim() === "" || !Number.isFinite(Number(value)) ? null : Number(value));

/** The request body of Create booking. Money received is only sent when there is some. */
export function bookingPayload(draft: BookingDraft) {
  const figures = draftFigures(draft);
  const received = receivedAmount(draft);
  const gotMoney = received > 0;
  const existing = draft.customerMode === "existing";
  return {
    unitId: draft.unit!.id,
    source: draft.source,
    customerId: existing ? draft.pickedCustomer!.id : null,
    newCustomer: existing ? null : {
      fullName: draft.fullName.trim(),
      fatherName: text(draft.guardianName),
      phone: draft.mobile.trim(),
      cnic: text(draft.cnic),
      email: text(draft.email),
      address: text(draft.address),
      dateOfBirth: draft.dob || null,
      nationality: text(draft.nationality),
      occupation: text(draft.occupation),
      whatsapp: text(draft.whatsapp),
    },
    agreedSalePrice: figures.agreed,
    discountPercent: figures.discountPercent,
    discountReason: figures.discountPercent > 0 ? text(draft.discountReason) : null,
    bookingAmountRequired: figures.bookingAmount,
    // The application form
    serialNo: text(draft.serialNo),
    apartmentCategory: text(draft.category),
    tower: text(draft.tower),
    isCorner: draft.isCorner,
    pricePerSft: numberOrNull(draft.pricePerSft),
    referenceId: text(draft.referenceId),
    // Money received with the form
    applicationAmountReceived: gotMoney ? received : null,
    applicationFinanceAccountId: gotMoney ? Number(draft.accountId) : null,
    applicationPaymentMethod: gotMoney ? draft.method : null,
    applicationPaymentType: gotMoney ? draft.paymentFor : null,
    paymentThrough: gotMoney ? text(draft.reference) : null,
    applicationDate: gotMoney ? draft.paidOn : null,
    // Next of kin
    nextOfKinName: text(draft.kinName),
    nextOfKinRelation: text(draft.kinRelation),
    nextOfKinContact: text(draft.kinMobile),
    nextOfKinCnic: text(draft.kinCnic),
    nextOfKinDob: draft.kinDob || null,
    nextOfKinAddress: text(draft.kinAddress),
  };
}

/** The Summary card: "—" until known. */
export function draftSummary(draft: BookingDraft): { label: string; value: string }[] {
  const figures = draftFigures(draft);
  const customer = draft.customerMode === "existing" ? draft.pickedCustomer?.fullName : draft.fullName.trim();
  return [
    { label: "Project", value: draft.projectName || "—" },
    { label: "Unit", value: draft.unit ? `Unit ${draft.unit.unitNumber} · ${draft.unit.unitType}` : "—" },
    { label: "Customer", value: customer || "—" },
    { label: "Agreed price", value: figures.agreed > 0 ? formatPkr(figures.agreed) : "—" },
    { label: "Booking amount", value: figures.bookingAmount > 0 ? formatPkr(figures.bookingAmount) : "—" },
  ];
}

/** "0333 4412987 · 37405-1234567-1", as a search result or the picked customer reads. */
export const customerLine = (customer: PickerCustomer): string => [formatPhone(customer.phone), customer.cnic].filter(Boolean).join(" · ");

export { CUSTOM_CHIP };
