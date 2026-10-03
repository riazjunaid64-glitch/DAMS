import { useState } from "react";
import { Button, Card, ConfirmDialog, DatePicker, Dropdown, Notice, useToast } from "../../components/ui";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { serverDay, showDay } from "./capitalPartners/rules.ts";
import * as whtApi from "./whtApi.ts";
import { MONTHS, type FinanceSettings } from "./whtTypes.ts";

const MONTH_OPTIONS = MONTHS.map((name, index) => ({ value: String(index + 1), label: name }));

type Props = { settings: FinanceSettings; onSaved: () => Promise<void> };

/**
 * Finance settings > Financial year: the month the year starts in and the go-live date, the first day
 * DAMS records movements for. Once a date is saved it cannot be emptied, and moving it asks first;
 * the server refuses a later date while anything is recorded before it.
 */
export function FinancialYearCard({ settings, onSaved }: Props) {
  const toast = useToast();
  const savedGoLive = serverDay(settings.goLiveDate);
  const [month, setMonth] = useState(String(settings.financialYearStartMonth));
  const [goLive, setGoLive] = useState(savedGoLive ?? "");
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
      toast.success("Financial year saved.");
      await onSaved();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The financial year could not be saved.");
      setConfirming(false);
      setSaving(false);
    }
  };

  const submit = () => {
    if (saving || missing) return;
    if (changed && savedGoLive !== null) setConfirming(true);
    else void save();
  };

  return (
    <Card title="Financial year" className="max-w-xl">
      <form noValidate onSubmit={(event) => { event.preventDefault(); submit(); }} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        <Dropdown
          label="Year starts in"
          required
          disabled={saving}
          options={MONTH_OPTIONS}
          value={month}
          onChange={setMonth}
          helper={`Current year: ${settings.currentFinancialYear}`}
        />
        <DatePicker
          label="Go-live date"
          required={savedGoLive !== null}
          disabled={saving}
          max={pakistanToday()}
          value={goLive}
          onChange={setGoLive}
          helper="Opening balances are the position at the start of this day."
        />
        <div>
          <Button type="submit" loading={saving && !confirming} disabled={saving || missing}>Save</Button>
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
