import { DatePicker, Dropdown, TextArea, TextField } from "../../components/ui";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { PLACEHOLDERS } from "../../utils/validation.ts";
import { KIN_RELATIONS, type BookingDraft, type DraftErrors } from "./draft.ts";

type Props = {
  draft: BookingDraft;
  errors: DraftErrors;
  onChange: (draft: BookingDraft) => void;
};

/** Step 3 · Next of kin. Everything here is optional. */
export function KinStep({ draft, errors, onChange }: Props) {
  const set = (change: Partial<BookingDraft>) => onChange({ ...draft, ...change });
  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 md:grid-cols-2">
        <TextField label="Full name" maxLength={200} value={draft.kinName} onChange={(event) => set({ kinName: event.target.value })} />
        <Dropdown label="Relation" placeholder="Select" options={[{ value: "", label: "—" }, ...KIN_RELATIONS]} value={draft.kinRelation} onChange={(kinRelation) => set({ kinRelation })} />
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        <TextField label="Mobile" type="tel" placeholder="0300 1234567" maxLength={50} error={errors.kinMobile} value={draft.kinMobile} onChange={(event) => set({ kinMobile: event.target.value })} />
        <TextField label="CNIC" placeholder={PLACEHOLDERS.cnic} maxLength={50} error={errors.kinCnic} value={draft.kinCnic} onChange={(event) => set({ kinCnic: event.target.value })} />
      </div>
      <DatePicker label="Date of birth" max={pakistanToday()} value={draft.kinDob} onChange={(kinDob) => set({ kinDob })} />
      <TextArea label="Mailing address" rows={2} placeholder="Same as applicant if blank" maxLength={500} value={draft.kinAddress} onChange={(event) => set({ kinAddress: event.target.value })} />
    </div>
  );
}
