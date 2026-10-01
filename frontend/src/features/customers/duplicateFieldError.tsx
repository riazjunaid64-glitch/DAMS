import type { ReactNode } from "react";
import type { CustomerFormErrors } from "./CustomerForm.tsx";

/** "Already used by Usman Tariq. Open customer" under the clashing field. */
export function duplicateFieldError(name: string, onOpen: () => void): ReactNode {
  return (
    <>
      Already used by {name}.{" "}
      <button type="button" className="font-extrabold underline" onClick={onOpen}>
        Open customer
      </button>
    </>
  );
}

/** What the server sends back when a mobile or CNIC already belongs to another customer (HTTP 409). */
export type ConflictBody = {
  message?: string;
  field?: string;
  existingCustomerId?: number;
  existingCustomerName?: string;
};

/** The field message ("Already used by Usman Tariq. Open customer") for a 409, or null when it is not one this form can show. */
export function conflictErrors(body: ConflictBody, openExisting: (customerId: number) => void): CustomerFormErrors | null {
  if (!body.field || !body.existingCustomerId || !body.existingCustomerName) return null;
  const field = body.field === "phone" ? "mobile" : body.field === "cnic" ? "cnic" : null;
  if (!field) return null;
  const id = body.existingCustomerId;
  return { [field]: duplicateFieldError(body.existingCustomerName, () => openExisting(id)) };
}
