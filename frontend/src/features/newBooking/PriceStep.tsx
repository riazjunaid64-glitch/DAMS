import { AttachProof, ChoiceChips, DatePicker, Dropdown, NumberField, TextField, type AttachProofProps } from "../../components/ui";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { formatPkr } from "../../utils/currency.ts";
import type { FinanceAccountOption } from "../bookings/detailTypes.ts";
import { PAYMENT_METHODS, referenceRequired } from "../bookings/paymentForm.ts";
import { BOOKING_CHIPS, CUSTOM_CHIP } from "../bookings/termsForm.ts";
import { PAYMENT_FOR, SOURCES, draftFigures, receivedAmount, withAgreedPrice, type BookingDraft, type DraftErrors } from "./draft.ts";

type Props = {
  draft: BookingDraft;
  errors: DraftErrors;
  financeAccounts: FinanceAccountOption[];
  /** Set when the account list could not be loaded, so the account field can say why it is empty. */
  accountsError: string | null;
  proof: Pick<AttachProofProps, "file" | "onPick" | "onRemove" | "progress" | "error">;
  onChange: (draft: BookingDraft) => void;
};

/**
 * Step 4 · Price & payment. The price and booking-amount fields behave exactly as in Set terms: a chip
 * works the booking amount out from the agreed price and locks the box; Custom frees it. Money received
 * with the form is optional, and its fields appear only once an amount is typed.
 */
export function PriceStep({ draft, errors, financeAccounts, accountsError, proof, onChange }: Props) {
  const set = (change: Partial<BookingDraft>) => onChange({ ...draft, ...change });
  const figures = draftFigures(draft);
  const custom = draft.chip === CUSTOM_CHIP;
  const gotMoney = receivedAmount(draft) > 0;

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 md:grid-cols-2">
        <NumberField
          label="Agreed sale price"
          required
          prefix="Rs"
          helper={draft.unit ? `List price ${formatPkr(draft.unit.price)}` : undefined}
          error={errors.agreedPrice}
          value={draft.agreedPrice}
          onChange={(agreedPrice) => onChange(withAgreedPrice(draft, agreedPrice))}
        />
        <NumberField
          label="Price per sq ft"
          prefix="Rs"
          helper={draft.unit && draft.unit.size > 0 ? `${draft.unit.size.toLocaleString("en-PK")} sq ft` : undefined}
          value={draft.pricePerSft}
          onChange={(pricePerSft) => set({ pricePerSft, pricePerSftEdited: true })}
        />
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <NumberField label="Discount %" error={errors.discount} value={draft.discountPercent} onChange={(discountPercent) => set({ discountPercent })} />
        <Dropdown label="Source" required error={errors.source} options={SOURCES} value={draft.source} onChange={(source) => set({ source })} />
      </div>
      {figures.discountPercent > 0 && (
        <TextField label="Discount reason" helper={`Discount ${formatPkr(figures.discount)}`} maxLength={500} value={draft.discountReason} onChange={(event) => set({ discountReason: event.target.value })} />
      )}

      <div className="flex flex-col gap-3">
        <ChoiceChips
          label="Booking amount"
          required
          options={BOOKING_CHIPS}
          value={draft.chip}
          onChange={(chip) => set({
            chip,
            // Custom starts from the amount the chip had worked out, so nothing typed is lost.
            customAmount: chip === CUSTOM_CHIP && figures.bookingAmount > 0 ? String(figures.bookingAmount) : draft.customAmount,
          })}
        />
        <NumberField
          aria-label="Booking amount in rupees"
          prefix="Rs"
          disabled={!custom}
          error={errors.bookingAmount}
          value={custom ? draft.customAmount : String(figures.bookingAmount)}
          onChange={(customAmount) => set({ customAmount })}
        />
      </div>

      <TextField label="Reference ID" placeholder="Dealer or agent reference" maxLength={100} value={draft.referenceId} onChange={(event) => set({ referenceId: event.target.value })} />

      <NumberField
        label={<>Received today <span className="font-normal normal-case tracking-normal text-ink-muted">(optional)</span></>}
        prefix="Rs"
        error={errors.received}
        value={draft.received}
        onChange={(received) => set({ received })}
      />

      {gotMoney && (
        <>
          <div className="grid gap-4 md:grid-cols-2">
            <Dropdown
              label="Received in account"
              required
              placeholder={accountsError ? "Accounts unavailable" : "Select an account"}
              error={errors.accountId ?? accountsError ?? undefined}
              options={financeAccounts.map((account) => ({ value: String(account.id), label: `${account.name} — ${account.accountHolderName}` }))}
              value={draft.accountId}
              onChange={(accountId) => set({ accountId })}
            />
            <Dropdown label="Payment method" required error={errors.method} options={PAYMENT_METHODS} value={draft.method} onChange={(method) => set({ method })} />
          </div>
          <ChoiceChips variant="segmented" label="Payment for" options={PAYMENT_FOR} value={draft.paymentFor} onChange={(paymentFor) => set({ paymentFor })} />
          <div className="grid gap-4 md:grid-cols-2">
            <TextField
              label="Reference no."
              required={referenceRequired(draft.method)}
              placeholder="Cheque / transfer no."
              maxLength={200}
              error={errors.reference}
              value={draft.reference}
              onChange={(event) => set({ reference: event.target.value })}
            />
            <DatePicker label="Date" required max={pakistanToday()} error={errors.paidOn} value={draft.paidOn} onChange={(paidOn) => set({ paidOn })} />
          </div>
          <AttachProof label="Proof" {...proof} />
        </>
      )}
    </div>
  );
}
