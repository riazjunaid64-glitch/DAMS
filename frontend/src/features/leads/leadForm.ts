import { paymentPreferenceLabel, purchaseIntentLabel } from "./labels.ts";

/** The requirement choices the New lead and Edit details forms offer. */
export const PAYMENT_CHOICES = ["Installments", "NeedsDetails", "Cash"].map((value) => ({ value, label: paymentPreferenceLabel(value)!.label }));
export const BUYING_FOR_CHOICES = ["SelfUse", "Investment"].map((value) => ({ value, label: purchaseIntentLabel(value)! }));

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Digits in a phone number: the API needs at least seven to match or reach anyone. */
const digits = (value: string) => value.replace(/\D/g, "").length;

export type ContactErrors = Partial<Record<"firstName" | "phone" | "email", string>>;

/** What is wrong with a lead's name, phone and email, field by field. A blank phone is fine when it is optional. */
export function contactErrors(form: { firstName: string; phone: string; email: string }, phoneRequired = true): ContactErrors {
  const phone = form.phone.trim();
  const email = form.email.trim();
  return {
    firstName: form.firstName.trim().length < 2 ? "Enter at least 2 letters." : undefined,
    phone: (phoneRequired || phone) && digits(phone) < 7 ? "Enter the full phone number." : undefined,
    email: email && !EMAIL.test(email) ? "Enter a valid email address." : undefined,
  };
}

export const hasErrors = (errors: ContactErrors) => Object.values(errors).some(Boolean);
