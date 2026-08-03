import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import { bookingToApplicationForm } from "../utils/bookingToApplicationForm.ts";
import {
  PLACEHOLDERS,
  formatCnic,
  formatPkMobile,
  isValidCnic,
  isValidEmail,
  isValidPkMobile,
} from "../utils/validation.ts";

type Props = { user: User | null };

interface Project { id: number; projectName: string; location: string; }
interface Unit {
  id: number;
  unitNumber: string;
  unitType: string;
  floorNumber: number;
  size: number;
  price: number;
  status: string;
}
interface Customer { id: number; fullName: string; phone: string; }

const initialForm = {
  // selection
  source: "WalkIn",
  // applicant (new customer)
  fullName: "",
  guardianName: "",
  cnic: "",
  dob: "",
  nationality: "",
  occupation: "",
  address: "",
  contact: "",
  email: "",
  whatsapp: "",
  // unit/application
  serialNo: "",
  apartmentCategory: "",
  tower: "",
  isCorner: false,
  // next of kin
  kinName: "",
  kinRelation: "",
  kinContact: "",
  kinCnic: "",
  kinDob: "",
  kinAddress: "",
  // office use
  agreedSalePrice: "",
  pricePerSft: "",
  discountPercent: "",
  totalDownPayment: "",
  referenceId: "",
  amountReceived: "",
  paymentType: "Booking",
  through: "",
  officeDate: "",
};

type FormState = typeof initialForm;

function SectionCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5 sm:p-6">
      <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-[var(--accent)]">{title}</h2>
      <div className="space-y-4">{children}</div>
    </div>
  );
}

function num(v: string): number | null {
  if (v == null || v.trim() === "") return null;
  const n = Number(v);
  return Number.isFinite(n) ? n : null;
}

export default function CreateBookingPage({ user }: Props) {
  const navigate = useNavigate();
  const isAdmin = user?.role === "Admin";

  const [projects, setProjects] = useState<Project[]>([]);
  const [units, setUnits] = useState<Unit[]>([]);
  const [customers, setCustomers] = useState<Customer[]>([]);

  const [projectId, setProjectId] = useState<number | "">("");
  const [unitId, setUnitId] = useState<number | "">("");
  const [customerMode, setCustomerMode] = useState<"new" | "existing">("new");
  const [customerId, setCustomerId] = useState<number | "">("");

  const [form, setForm] = useState<FormState>(initialForm);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selectedUnit = useMemo(() => units.find((u) => u.id === unitId) ?? null, [units, unitId]);

  useEffect(() => {
    if (!isAdmin) return;
    (async () => {
      try {
        const [pRes, cRes] = await Promise.all([
          api("/api/Project"),
          api("/api/Customer?pageSize=100"),
        ]);
        if (pRes.ok) setProjects(await pRes.json());
        if (cRes.ok) {
          const data = await cRes.json();
          setCustomers(data.items ?? []);
        }
      } catch {
        /* non-fatal */
      }
    })();
  }, [isAdmin]);

  // Load units when project changes.
  useEffect(() => {
    if (projectId === "") return;
    (async () => {
      try {
        const res = await api(`/api/Unit/project/${projectId}`);
        if (res.ok) {
          const list: Unit[] = await res.json();
          setUnits(list.filter((u) => u.status === "Available"));
        }
      } catch {
        setUnits([]);
      }
      setUnitId("");
    })();
  }, [projectId]);

  const handleProjectChange = (value: string) => {
    setProjectId(value ? Number(value) : "");
    setUnits([]);
    setUnitId("");
  };

  const handleUnitChange = (value: string) => {
    const nextUnitId = value ? Number(value) : "";
    setUnitId(nextUnitId);
    const nextUnit = units.find((unit) => unit.id === nextUnitId);
    if (!nextUnit) return;
    setForm((prev) => ({
      ...prev,
      apartmentCategory: prev.apartmentCategory || nextUnit.unitType,
      agreedSalePrice: prev.agreedSalePrice || String(nextUnit.price),
      pricePerSft:
        prev.pricePerSft ||
        (nextUnit.size > 0 ? String(Math.round((nextUnit.price / nextUnit.size) * 100) / 100) : ""),
    }));
  };

  const set = (field: keyof FormState) =>
    (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => {
      let value: string | boolean =
        e.target.type === "checkbox" ? (e.target as HTMLInputElement).checked : e.target.value;
      if (typeof value === "string") {
        if (field === "kinCnic") value = formatCnic(value);
        if (field === "contact" || field === "kinContact") value = formatPkMobile(value);
      }
      setForm((prev) => ({ ...prev, [field]: value }));
      setError(null);
    };

  // Inline, per-field validation messages (shown only once the field has a value).
  const fieldError = (field: "cnic" | "email" | "contact" | "kinContact" | "kinCnic"): string | undefined => {
    const v = form[field].trim();
    if (!v) return undefined;
    switch (field) {
      case "email":
        return isValidEmail(v) ? undefined : "Enter a valid email address (e.g. name@example.com).";
      case "contact":
      case "kinContact":
        return isValidPkMobile(v) ? undefined : "Enter a valid Pakistani mobile number (e.g. 0300-1234567).";
      case "kinCnic":
        return isValidCnic(v) ? undefined : "CNIC must be 13 digits in the format 00000-0000000-0.";
      case "cnic":
        // This field also accepts NICOP / Passport, so only enforce the CNIC
        // mask when the value looks like a CNIC attempt (digits / hyphens only).
        return /^[\d-]+$/.test(v) && !isValidCnic(v)
          ? "For a CNIC use the format 00000-0000000-0 (or enter a passport number)."
          : undefined;
    }
  };

  const validate = (): string | null => {
    if (unitId === "") return "Please select a unit.";
    if (customerMode === "existing") {
      if (customerId === "") return "Please select an existing customer.";
    } else {
      if (form.fullName.trim().length < 2) return "Please enter the applicant's full name.";
      if (!isValidPkMobile(form.contact)) return "Please enter a valid Pakistani contact number (e.g. 0300-1234567).";
      if (form.email.trim() && !isValidEmail(form.email)) return "Please enter a valid email address.";
      if (fieldError("cnic")) return "Please enter the CNIC in the format 00000-0000000-0, or a valid passport number.";
      if (form.kinContact.trim() && !isValidPkMobile(form.kinContact)) return "Please enter a valid next-of-kin contact number.";
      if (form.kinCnic.trim() && !isValidCnic(form.kinCnic)) return "Please enter the next-of-kin CNIC in the format 00000-0000000-0.";
    }
    return null;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const v = validate();
    if (v) { setError(v); return; }

    setSubmitting(true);
    setError(null);

    const payload = {
      unitId,
      source: form.source,
      customerId: customerMode === "existing" ? customerId : null,
      newCustomer: customerMode === "new" ? {
        fullName: form.fullName.trim(),
        fatherName: form.guardianName.trim() || null,
        phone: form.contact.trim(),
        cnic: form.cnic.trim() || null,
        email: form.email.trim() || null,
        address: form.address.trim() || null,
        dateOfBirth: form.dob || null,
        nationality: form.nationality.trim() || null,
        occupation: form.occupation.trim() || null,
        whatsapp: form.whatsapp.trim() || null,
      } : null,
      agreedSalePrice: num(form.agreedSalePrice),
      bookingAmountRequired: num(form.totalDownPayment),
      // Application Form snapshot
      serialNo: form.serialNo.trim() || null,
      apartmentCategory: form.apartmentCategory.trim() || null,
      tower: form.tower.trim() || null,
      isCorner: form.isCorner,
      pricePerSft: num(form.pricePerSft),
      discountPercent: num(form.discountPercent),
      referenceId: form.referenceId.trim() || null,
      paymentThrough: form.through.trim() || null,
      applicationPaymentType: form.paymentType || null,
      applicationAmountReceived: num(form.amountReceived),
      applicationDate: form.officeDate || null,
      nextOfKinName: form.kinName.trim() || null,
      nextOfKinRelation: form.kinRelation.trim() || null,
      nextOfKinContact: form.kinContact.trim() || null,
      nextOfKinCnic: form.kinCnic.trim() || null,
      nextOfKinDob: form.kinDob || null,
      nextOfKinAddress: form.kinAddress.trim() || null,
    };

    try {
      const res = await api("/api/Booking", { method: "POST", body: JSON.stringify(payload) });
      const dataRes = await res.json().catch(() => ({}));
      if (!res.ok) {
        setError(dataRes.message || "Unable to create booking. Please review the details and try again.");
        setSubmitting(false);
        return;
      }
      // Go straight to the printable Application Form, filled with the created booking.
      navigate("/application-form", { state: { data: bookingToApplicationForm(dataRes), fromCreate: true } });
    } catch {
      setError("Something went wrong. Please try again.");
      setSubmitting(false);
    }
  };

  if (!isAdmin) {
    return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">Admin access required.</p></Container>;
  }

  const inputClass =
    "w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)] focus:border-[var(--accent)] focus:outline-none";
  const labelClass = "flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]";

  return (
    <Container className="py-10">
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-[var(--text-heading)]">New Booking — Application Form</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">Fill the applicant details, then generate the printable form.</p>
        </div>
        <Button variant="ghost" size="sm" onClick={() => navigate("/confirmed-bookings")}>← Back</Button>
      </div>

      <form onSubmit={handleSubmit} className="space-y-6">
        {/* Unit selection */}
        <SectionCard title="Unit">
          <div className="grid gap-4 sm:grid-cols-2">
            <label className={labelClass}>
              <span>Project</span>
              <select className={inputClass} value={projectId}
                onChange={(e) => handleProjectChange(e.target.value)}>
                <option value="">Select project...</option>
                {projects.map((p) => <option key={p.id} value={p.id}>{p.projectName}</option>)}
              </select>
            </label>
            <label className={labelClass}>
              <span>Available Unit</span>
              <select className={inputClass} value={unitId} disabled={projectId === ""}
                onChange={(e) => handleUnitChange(e.target.value)}>
                <option value="">{projectId === "" ? "Select a project first" : "Select unit..."}</option>
                {units.map((u) => <option key={u.id} value={u.id}>{u.unitNumber} · {u.unitType}</option>)}
              </select>
            </label>
          </div>
          {selectedUnit && (
            <div className="grid grid-cols-2 gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface-glass-hover)] p-4 sm:grid-cols-4">
              <div><p className="text-xs text-[var(--text-muted)]">Floor</p><p className="font-medium text-[var(--text-primary)]">{selectedUnit.floorNumber}</p></div>
              <div><p className="text-xs text-[var(--text-muted)]">Size (sft)</p><p className="font-medium text-[var(--text-primary)]">{selectedUnit.size}</p></div>
              <div><p className="text-xs text-[var(--text-muted)]">List Price</p><p className="font-medium text-[var(--text-primary)]">{selectedUnit.price.toLocaleString()}</p></div>
              <div><p className="text-xs text-[var(--text-muted)]">Type</p><p className="font-medium text-[var(--text-primary)]">{selectedUnit.unitType}</p></div>
            </div>
          )}
          <div className="grid gap-4 sm:grid-cols-3">
            <Field label="Serial No." value={form.serialNo} onChange={set("serialNo")} placeholder="Auto (booking ref) if blank" />
            <Field label="Apartment Category" value={form.apartmentCategory} onChange={set("apartmentCategory")} />
            <Field label="Tower" value={form.tower} onChange={set("tower")} />
          </div>
          <label className="flex items-center gap-2 text-sm text-[var(--text-secondary)]">
            <input type="checkbox" checked={form.isCorner} onChange={set("isCorner")} className="h-4 w-4" />
            Corner unit
          </label>
        </SectionCard>

        {/* Applicant */}
        <SectionCard title="Applicant">
          <div className="flex gap-2">
            <button type="button" onClick={() => setCustomerMode("new")}
              className={`rounded-lg px-3 py-1.5 text-sm font-medium transition ${customerMode === "new" ? "bg-[var(--accent)] text-[#1c1810]" : "border border-[var(--border)] text-[var(--text-secondary)]"}`}>
              New customer
            </button>
            <button type="button" onClick={() => setCustomerMode("existing")}
              className={`rounded-lg px-3 py-1.5 text-sm font-medium transition ${customerMode === "existing" ? "bg-[var(--accent)] text-[#1c1810]" : "border border-[var(--border)] text-[var(--text-secondary)]"}`}>
              Existing customer
            </button>
          </div>

          {customerMode === "existing" ? (
            <label className={labelClass}>
              <span>Select Customer</span>
              <select className={inputClass} value={customerId}
                onChange={(e) => setCustomerId(e.target.value ? Number(e.target.value) : "")}>
                <option value="">Select customer...</option>
                {customers.map((c) => <option key={c.id} value={c.id}>{c.fullName} · {c.phone}</option>)}
              </select>
            </label>
          ) : (
            <>
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Full Name" value={form.fullName} onChange={set("fullName")} required />
                <Field label="S/o, W/o, D/o" value={form.guardianName} onChange={set("guardianName")} />
                <Field label="CNIC / NICOP / Passport #" value={form.cnic} onChange={set("cnic")} placeholder={PLACEHOLDERS.cnic} error={fieldError("cnic")} />
                <Field label="Date of Birth" type="date" value={form.dob} onChange={set("dob")} />
                <Field label="Nationality" value={form.nationality} onChange={set("nationality")} />
                <Field label="Occupation" value={form.occupation} onChange={set("occupation")} />
                <Field label="Contact #" value={form.contact} onChange={set("contact")} placeholder={PLACEHOLDERS.mobile} inputMode="tel" error={fieldError("contact")} required />
                <Field label="Email" type="email" value={form.email} onChange={set("email")} placeholder={PLACEHOLDERS.email} error={fieldError("email")} />
                <Field label="Whatsapp" value={form.whatsapp} onChange={set("whatsapp")} />
              </div>
              <Field label="Mailing Address" value={form.address} onChange={set("address")} />
            </>
          )}
        </SectionCard>

        {/* Next of Kin */}
        <SectionCard title="Next of Kin(s)">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Full Name" value={form.kinName} onChange={set("kinName")} />
            <Field label="Relation" value={form.kinRelation} onChange={set("kinRelation")} />
            <Field label="Nominee Contact #" value={form.kinContact} onChange={set("kinContact")} placeholder={PLACEHOLDERS.mobile} inputMode="tel" error={fieldError("kinContact")} />
            <Field label="CNIC #" value={form.kinCnic} onChange={set("kinCnic")} placeholder={PLACEHOLDERS.cnic} inputMode="numeric" error={fieldError("kinCnic")} />
            <Field label="Date of Birth" type="date" value={form.kinDob} onChange={set("kinDob")} />
          </div>
          <Field label="Mailing Address" value={form.kinAddress} onChange={set("kinAddress")} />
        </SectionCard>

        {/* Office use */}
        <SectionCard title="Office Use Only">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Agreed Sale Price" type="number" value={form.agreedSalePrice} onChange={set("agreedSalePrice")} />
            <Field label="Price Per Sft" type="number" value={form.pricePerSft} onChange={set("pricePerSft")} />
            <Field label="Discount %" type="number" value={form.discountPercent} onChange={set("discountPercent")} />
            <Field label="Total Down Payment" type="number" value={form.totalDownPayment} onChange={set("totalDownPayment")} />
            <Field label="Reference ID" value={form.referenceId} onChange={set("referenceId")} />
            <Field label="Amount Received" type="number" value={form.amountReceived} onChange={set("amountReceived")} />
          </div>
          <div className="grid gap-4 sm:grid-cols-3">
            <label className={labelClass}>
              <span>Payment Type</span>
              <select className={inputClass} value={form.paymentType} onChange={set("paymentType")}>
                <option value="Booking">Booking</option>
                <option value="Confirmation">Confirmation</option>
                <option value="LumSum">LumSum</option>
              </select>
            </label>
            <Field label="Through" value={form.through} onChange={set("through")} />
            <Field label="Date" type="date" value={form.officeDate} onChange={set("officeDate")} />
            <label className={labelClass}>
              <span>Source</span>
              <select className={inputClass} value={form.source} onChange={set("source")}>
                <option value="WalkIn">Walk-in</option>
                <option value="Phone">Phone</option>
                <option value="Referral">Referral</option>
                <option value="Other">Other</option>
              </select>
            </label>
          </div>
        </SectionCard>

        {error && (
          <div className="rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">{error}</div>
        )}

        <div className="flex items-center justify-end gap-3">
          <Button type="button" variant="ghost" onClick={() => navigate("/confirmed-bookings")} disabled={submitting}>Cancel</Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? "Creating..." : "Create Booking & Generate Form"}
          </Button>
        </div>
      </form>
    </Container>
  );
}
