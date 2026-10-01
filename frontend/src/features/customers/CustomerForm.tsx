import type { ReactNode } from "react";
import { DatePicker, TextArea, TextField } from "../../components/ui";
import { pakistanToday } from "../../lib/financePeriods.ts";
import {
  type CustomerFormField,
  type CustomerFormValues,
} from "./customerForm.ts";

export type CustomerFormErrors = Partial<Record<CustomerFormField, ReactNode>>;

type Props = {
  values: CustomerFormValues;
  errors?: CustomerFormErrors;
  onChange: (values: CustomerFormValues) => void;
  /** Notes only appear on Customers screens, not on New booking step 2. */
  showNotes?: boolean;
  disabled?: boolean;
};

/**
 * One customer form for New customer, Edit customer and New booking step 2. Field order,
 * placeholders and checks stay aligned with the application form (KAN-77 / KAN-79).
 */
export function CustomerForm({ values, errors = {}, onChange, showNotes = false, disabled = false }: Props) {
  const set = <K extends CustomerFormField>(key: K, value: CustomerFormValues[K]) =>
    onChange({ ...values, [key]: value });

  return (
    <div className="flex flex-col gap-4">
      <TextField
        label="Full name"
        required
        maxLength={200}
        disabled={disabled}
        error={errors.fullName}
        value={values.fullName}
        onChange={(event) => set("fullName", event.target.value)}
      />
      <TextField
        label="S/O, W/O, D/O"
        maxLength={200}
        disabled={disabled}
        error={errors.guardianName}
        value={values.guardianName}
        onChange={(event) => set("guardianName", event.target.value)}
      />
      <div className="grid gap-4 md:grid-cols-2">
        <TextField
          label="Mobile"
          required
          type="tel"
          placeholder="0300 1234567"
          maxLength={50}
          disabled={disabled}
          error={errors.mobile}
          value={values.mobile}
          onChange={(event) => set("mobile", event.target.value)}
        />
        <TextField
          label="CNIC / NICOP / Passport"
          placeholder="00000-0000000-0"
          maxLength={50}
          disabled={disabled}
          error={errors.cnic}
          value={values.cnic}
          onChange={(event) => set("cnic", event.target.value)}
        />
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        <TextField
          label="Email"
          type="email"
          placeholder="name@example.com"
          maxLength={200}
          disabled={disabled}
          error={errors.email}
          value={values.email}
          onChange={(event) => set("email", event.target.value)}
        />
        <TextField
          label="WhatsApp"
          type="tel"
          placeholder="Same as mobile if blank"
          maxLength={50}
          disabled={disabled}
          error={errors.whatsapp}
          value={values.whatsapp}
          onChange={(event) => set("whatsapp", event.target.value)}
        />
      </div>
      <div className="grid gap-4 md:grid-cols-3">
        <DatePicker
          label="Date of birth"
          max={pakistanToday()}
          disabled={disabled}
          error={errors.dob}
          value={values.dob}
          onChange={(dob) => set("dob", dob)}
        />
        <TextField
          label="Nationality"
          placeholder="Pakistani"
          maxLength={100}
          disabled={disabled}
          error={errors.nationality}
          value={values.nationality}
          onChange={(event) => set("nationality", event.target.value)}
        />
        <TextField
          label="Occupation"
          maxLength={150}
          disabled={disabled}
          error={errors.occupation}
          value={values.occupation}
          onChange={(event) => set("occupation", event.target.value)}
        />
      </div>
      <TextArea
        label="Mailing address"
        rows={2}
        maxLength={500}
        disabled={disabled}
        error={errors.address}
        value={values.address}
        onChange={(event) => set("address", event.target.value)}
      />
      {showNotes && (
        <TextArea
          label="Notes"
          rows={3}
          maxLength={1000}
          disabled={disabled}
          error={errors.notes}
          value={values.notes}
          onChange={(event) => set("notes", event.target.value)}
        />
      )}
    </div>
  );
}
