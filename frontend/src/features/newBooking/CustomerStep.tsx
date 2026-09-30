import { useEffect, useRef, useState } from "react";
import { api } from "../../api/api.ts";
import { Avatar, ChoiceChips, DatePicker, IconCheck, SearchBar, TextArea, TextField, cx } from "../../components/ui";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { PLACEHOLDERS } from "../../utils/validation.ts";
import { parseCustomers, searchIsUsable, type PickerCustomer } from "./customers.ts";
import { customerLine, type BookingDraft, type DraftErrors } from "./draft.ts";

type Props = {
  draft: BookingDraft;
  errors: DraftErrors;
  onChange: (draft: BookingDraft) => void;
};

const MODES = [{ value: "new", label: "New customer" }, { value: "existing", label: "Existing customer" }];

/** Existing customer: searches the server as the user types (name, any phone format, or CNIC), with no 100-customer limit. */
function ExistingCustomer({ draft, errors, onChange }: Props) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<PickerCustomer[] | null>(null);
  const [failed, setFailed] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const sequence = useRef(0);

  useEffect(() => {
    const request = ++sequence.current;
    if (!searchIsUsable(query)) {
      setResults(null);
      setFailed(null);
      setLoading(false);
      return;
    }
    setLoading(true);
    void (async () => {
      try {
        const response = await api(`/api/Customer?search=${encodeURIComponent(query.trim())}&pageSize=20`);
        if (!response.ok) throw new Error();
        const found = parseCustomers(await response.json());
        if (request === sequence.current) { setResults(found); setFailed(null); }
      } catch {
        if (request === sequence.current) setFailed("Customers could not be searched. Try again.");
      } finally {
        if (request === sequence.current) setLoading(false);
      }
    })();
  }, [query]);

  // The picked customer stays on show even when a newer search no longer lists them.
  const picked = draft.pickedCustomer;
  const shown = results ?? [];
  const list = picked && !shown.some((customer) => customer.id === picked.id) && results !== null ? [picked, ...shown] : shown;

  return (
    <div className="flex flex-col gap-2">
      <span className="text-label font-bold uppercase tracking-[0.4px] text-ink-2">Search customers <span aria-hidden="true">*</span></span>
      <SearchBar size="lg" value={query} onSearch={setQuery} placeholder="Name, phone or CNIC" aria-label="Search customers" />
      {errors.customer && <p role="alert" className="m-0 text-small font-bold text-danger">{errors.customer}</p>}
      {failed && <p role="alert" className="m-0 text-small font-bold text-danger">{failed}</p>}
      {picked && results === null && (
        <ul className="m-0 flex list-none flex-col gap-2 p-0">{resultRow(picked, true)}</ul>
      )}
      {results !== null && list.length === 0 && !loading && <p className="m-0 py-4 text-center text-body text-ink-muted">No customer found.</p>}
      {list.length > 0 && <ul className="m-0 flex list-none flex-col gap-2 p-0" aria-label="Customers">{list.map((customer) => resultRow(customer, picked?.id === customer.id))}</ul>}
      {loading && <p className="m-0 text-small text-ink-muted" role="status">Searching…</p>}
    </div>
  );

  function resultRow(customer: PickerCustomer, selected: boolean) {
    return (
      <li key={customer.id}>
        <button
          type="button"
          aria-pressed={selected}
          onClick={() => onChange({ ...draft, pickedCustomer: customer })}
          className={cx(
            "flex w-full cursor-pointer items-center gap-3 rounded-card border bg-card p-3 text-left font-ui focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary",
            selected ? "border-2 border-primary" : "border-line hover:bg-page",
          )}
        >
          <Avatar name={customer.fullName} size={38} />
          <span className="min-w-0 flex-1">
            <span className="block text-body font-extrabold text-ink">{customer.fullName}</span>
            <span className="block text-small text-ink-muted">{customerLine(customer)}</span>
          </span>
          {selected && <span aria-hidden="true" className="flex size-5 shrink-0 items-center justify-center rounded-full bg-primary text-white"><IconCheck size={12} /></span>}
        </button>
      </li>
    );
  }
}

/**
 * Step 2 · Customer: a new customer's details, or an existing customer found by search. The fields,
 * their order, placeholders and checks are the application form's; the customer is only created
 * when the booking is, so stopping here leaves nothing behind.
 */
export function CustomerStep({ draft, errors, onChange }: Props) {
  const set = (change: Partial<BookingDraft>) => onChange({ ...draft, ...change });
  return (
    <div className="flex flex-col gap-4">
      <ChoiceChips variant="segmented" aria-label="Customer" options={MODES} value={draft.customerMode} onChange={(customerMode) => set({ customerMode: customerMode as BookingDraft["customerMode"] })} />

      {draft.customerMode === "existing" ? (
        <ExistingCustomer draft={draft} errors={errors} onChange={onChange} />
      ) : (
        <div className="flex flex-col gap-4">
          <TextField label="Full name" required maxLength={200} error={errors.fullName} value={draft.fullName} onChange={(event) => set({ fullName: event.target.value })} />
          <TextField label="S/O, W/O, D/O" maxLength={200} value={draft.guardianName} onChange={(event) => set({ guardianName: event.target.value })} />
          <div className="grid gap-4 md:grid-cols-2">
            <TextField label="Mobile" required type="tel" placeholder="0300 1234567" maxLength={50} error={errors.mobile} value={draft.mobile} onChange={(event) => set({ mobile: event.target.value })} />
            <TextField label="CNIC / NICOP / Passport" placeholder={PLACEHOLDERS.cnic} maxLength={50} error={errors.cnic} value={draft.cnic} onChange={(event) => set({ cnic: event.target.value })} />
          </div>
          <div className="grid gap-4 md:grid-cols-2">
            <TextField label="Email" type="email" placeholder={PLACEHOLDERS.email} maxLength={200} error={errors.email} value={draft.email} onChange={(event) => set({ email: event.target.value })} />
            <TextField label="WhatsApp" type="tel" placeholder="Same as mobile if blank" maxLength={50} error={errors.whatsapp} value={draft.whatsapp} onChange={(event) => set({ whatsapp: event.target.value })} />
          </div>
          <div className="grid gap-4 md:grid-cols-3">
            <DatePicker label="Date of birth" max={pakistanToday()} value={draft.dob} onChange={(dob) => set({ dob })} />
            <TextField label="Nationality" maxLength={100} value={draft.nationality} onChange={(event) => set({ nationality: event.target.value })} />
            <TextField label="Occupation" maxLength={150} value={draft.occupation} onChange={(event) => set({ occupation: event.target.value })} />
          </div>
          <TextArea label="Mailing address" rows={2} maxLength={500} value={draft.address} onChange={(event) => set({ address: event.target.value })} />
        </div>
      )}
    </div>
  );
}
