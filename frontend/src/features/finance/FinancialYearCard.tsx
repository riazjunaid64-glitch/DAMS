import { useState } from "react";
import { Button, Card, ConfirmDialog, DatePicker, Dropdown, Notice, useToast } from "../../components/ui";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { serverDay, showDay } from "./capitalPartners/rules.ts";
import * as whtApi from "./whtApi.ts";
import { MONTHS, type FinanceSettings } from "./whtTypes.ts";

const MONTH_OPTIONS = MONTHS.map((name, index) => ({ value: String(index + 1), label: name }));

type Props = {
  settings: FinanceSettings;
  onSaved: () => void;
  /** The settings are being read again: Save waits for the new version rather than send a stale one. */
  disabled?: boolean;
};

/**
 * Finance settings > Financial year: the month the year starts in and the go-live date, the first day
 * DAMS records movements for. Once a date is saved it cannot be emptied, and moving it asks first;
 * the server refuses a later date while anything is recorded before it.
 */
export function FinancialYearCard({ settings, onSaved, disabled = false }: Props) {
  const toast = useToast();
  const savedGoLive = serverDay(settings.goLiveDate);
  const savedMonth = String(settings.financialYearStartMonth);
  // What the user changed and has not saved; a field with no draft shows the saved value. When a new
  // version arrives, a draft that now equals the saved value was saved (this card's own Save) and is
  // dropped; one that still differs survives a save made elsewhere on the same settings (Mark as checked).
  const [monthDraft, setMonthDraft] = useState<string | null>(null);
  const [goLiveDraft, setGoLiveDraft] = useState<string | null>(null);
  const [seenVersion, setSeenVersion] = useState(settings.concurrencyToken);
  if (seenVersion !== settings.concurrencyToken) {
    setSeenVersion(settings.concurrencyToken);
    if (monthDraft === savedMonth) setMonthDraft(null);
    if (goLiveDraft === (savedGoLive ?? "")) setGoLiveDraft(null);
  }
  const month = monthDraft ?? savedMonth;
  const goLive = goLiveDraft ?? savedGoLive ?? "";
  const [saving, setSaving] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const changed = goLive !== "" && goLive !== savedGoLive;
  // A saved date cannot be emptied; with none saved yet the year can still be saved without one.
  const missing = savedGoLive !== null && goLive === "";

  const save = async () => {
    if (saving) return;
    setSaving(true);
    setError(null);
    try {
      await whtApi.saveSettings({
        financialYearStartMonth: Number(month),
        goLiveDate: changed ? goLive : null,
        markRatesConfirmed: false,
        clearRatesConfirmation: false,
        concurrencyToken: settings.concurrencyToken,
      });
      toast.success("Finance settings saved.");
      onSaved();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The financial year could not be saved.");
    } finally {
      setConfirming(false);
      setSaving(false);
    }
  };

  const submit = () => {
    if (saving || missing || disabled) return;
    if (changed && savedGoLive !== null) setConfirming(true);
    else void save();
  };

  return (
    <Card title="Financial year">
      <form noValidate onSubmit={(event) => { event.preventDefault(); submit(); }} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        <Dropdown
          label="Year starts in"
          required
          disabled={saving}
          options={MONTH_OPTIONS}
          value={month}
          onChange={setMonthDraft}
          helper={`Current year: ${settings.currentFinancialYear}`}
        />
        <DatePicker
          label="Go-live date"
          required={savedGoLive !== null}
          disabled={saving}
          max={pakistanToday()}
          value={goLive}
          onChange={setGoLiveDraft}
          helper="Opening balances are the position at the start of this day."
        />
        <div>
          <Button type="submit" loading={saving && !confirming} disabled={saving || missing || disabled}>Save</Button>
        </div>
      </form>
      <ConfirmDialog
        open={confirming}
        onClose={() => !saving && setConfirming(false)}
        onConfirm={() => void save()}
        title="Change the go-live date?"
        message={`Reports will start from ${showDay(goLive)} instead of ${showDay(savedGoLive)}. Allowed only when nothing is recorded before the new date.`}
        confirmLabel="Change"
        loading={saving}
      />
    </Card>
  );
}
