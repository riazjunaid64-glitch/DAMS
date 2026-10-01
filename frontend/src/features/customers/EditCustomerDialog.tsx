import { useState } from "react";
import { Modal, useToast } from "../../components/ui";
import { api } from "../../api/api.ts";
import { DialogTitle } from "../bookings/DialogTitle.tsx";
import { CustomerForm, type CustomerFormErrors } from "./CustomerForm.tsx";
import { customerFormErrors, customerPayload, formFromCustomer, type CustomerDetail } from "./customerForm.ts";
import { conflictErrors, type ConflictBody } from "./duplicateFieldError.tsx";

type Props = {
  customer: CustomerDetail;
  onClose: () => void;
  /** Opens another customer, from a duplicate mobile / CNIC message. */
  onOpenCustomer: (id: number) => void;
  /** Reloads the page once the server has saved, before the popup closes. */
  onSaved: () => Promise<void> | void;
};

/**
 * Edit customer: the shared Customer form, filled in, with Notes. Status and Source are not in it —
 * blocking and unblocking have their own buttons. A mobile or CNIC that belongs to another customer
 * is refused by the server and shown under that field with an Open customer link.
 */
export function EditCustomerDialog({ customer, onClose, onOpenCustomer, onSaved }: Props) {
  const toast = useToast();
  const [values, setValues] = useState(() => formFromCustomer(customer));
  const [errors, setErrors] = useState<CustomerFormErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const save = async () => {
    const local = customerFormErrors(values);
    if (Object.keys(local).length) {
      setErrors(local);
      return;
    }
    setSaving(true);
    setErrors({});
    setFormError(null);
    try {
      const response = await api(`/api/Customer/${customer.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(customerPayload(values)),
      });
      const body = (await response.json().catch(() => ({}))) as ConflictBody;
      if (response.status === 409) {
        const conflict = conflictErrors(body, (existingId) => {
          onClose();
          onOpenCustomer(existingId);
        });
        setErrors(conflict ?? {});
        if (!conflict) setFormError(body.message ?? "Another customer already has these details.");
        return;
      }
      if (!response.ok) {
        setFormError(body.message ?? "The customer could not be saved.");
        return;
      }
      toast.success("Customer saved");
      await onSaved();
      onClose();
    } catch {
      setFormError("The customer could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={<DialogTitle title="Edit customer" subtitle={customer.fullName} />}
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: "Save changes", onClick: () => void save(), loading: saving }}
    >
      {formError && <p role="alert" className="mb-4 text-small font-bold text-danger">{formError}</p>}
      <CustomerForm values={values} errors={errors} onChange={setValues} showNotes disabled={saving} />
    </Modal>
  );
}
