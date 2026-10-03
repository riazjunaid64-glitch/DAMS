import { Button, Card, StatusBadge } from "../../../components/ui";
import { formatDay } from "../../../lib/dates.ts";
import { FinancialYearCard } from "../FinancialYearCard.tsx";
import type { FinanceSettings } from "../whtTypes.ts";

type Props = {
  settings: FinanceSettings;
  /** The settings are being read again, so the version on screen is about to change. */
  reloading: boolean;
  /** The rates check is being saved. */
  checking: boolean;
  /** Marks the rates as checked (true) or clears the check (false). */
  onCheck: (checked: boolean) => void;
  onSaved: () => void;
};

/** Finance settings > Financial year: the year and go-live date, and whether an accountant checked the tax rates. */
export function FinancialYearTab({ settings, reloading, checking, onCheck, onSaved }: Props) {
  const checkedAt = settings.whtRatesConfirmedAt;
  const by = settings.whtRatesConfirmedByName;
  return (
    <div className="grid items-start gap-4 md:grid-cols-2 md:gap-5">
      {/* Not keyed by the version: Mark as checked and Clear check save the same settings row, and a new
          version must not throw away a month or go-live date typed here but not saved yet. After its own
          save the card already holds what was saved, and it always sends the version it is given. */}
      <FinancialYearCard settings={settings} onSaved={onSaved} disabled={reloading} />
      <Card title="Tax rates check">
        <div className="flex flex-col items-start gap-3">
          <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
            <StatusBadge status={checkedAt ? "Checked by accountant" : "Not checked yet"} />
            {checkedAt && <span className="text-sm text-ink-2">{formatDay(checkedAt)}{by ? ` by ${by}` : ""}</span>}
          </div>
          {checkedAt ? (
            <Button variant="outline" loading={checking} disabled={reloading} onClick={() => onCheck(false)}>Clear check</Button>
          ) : (
            <Button loading={checking} disabled={reloading} onClick={() => onCheck(true)}>Mark as checked</Button>
          )}
        </div>
      </Card>
    </div>
  );
}
