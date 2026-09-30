import { useEffect, useMemo, useState } from "react";
import { api } from "../../api/api.ts";
import { Button, Dropdown, EmptyState, IconBuilding, IconCheck, SearchBar, TextField, Toggle, cx, type Option } from "../../components/ui";
import { floorName } from "../../lib/floors.ts";
import { formatSqFt, UNIT_TYPES } from "../../components/project/unitList.ts";
import { formatPkr } from "../../utils/currency.ts";
import { withUnit, type BookingDraft, type DraftErrors } from "./draft.ts";
import {
  NO_FILTERS, UNIT_PAGE_SIZE, availableUnits, filterUnits, firstUnits, floorFilterOptions, groupByFloor, typeFilterOptions, type PickerUnit, type UnitFilters,
} from "./units.ts";

type Props = {
  draft: BookingDraft;
  errors: DraftErrors;
  projects: { id: number; projectName: string }[];
  onChange: (draft: BookingDraft) => void;
};

const tick = (selected: boolean) => (
  <span aria-hidden="true" className={cx("flex size-[22px] shrink-0 items-center justify-center rounded-full border", selected ? "border-primary bg-primary text-white" : "border-line-input bg-card")}>
    {selected && <IconCheck size={13} />}
  </span>
);

/**
 * Step 1 · Unit. All of the project's units load once (about 500 for a big project) and the search,
 * floor and type filters run on the device, so narrowing the list is instant. Only Available units
 * are listed, grouped by floor, 20 at a time.
 */
export function UnitStep({ draft, errors, projects, onChange }: Props) {
  const [loaded, setLoaded] = useState<{ projectId: string; units: PickerUnit[] } | null>(null);
  const [failed, setFailed] = useState<string | null>(null);
  const [filters, setFilters] = useState<UnitFilters>(NO_FILTERS);
  const [shownCount, setShownCount] = useState(UNIT_PAGE_SIZE);

  useEffect(() => {
    if (!draft.projectId) return;
    let current = true;
    void (async () => {
      try {
        const response = await api(`/api/Unit/project/${draft.projectId}`, undefined, false);
        if (!response.ok) throw new Error();
        const rows = await response.json() as PickerUnit[];
        if (current) { setLoaded({ projectId: draft.projectId, units: availableUnits(rows) }); setFailed(null); }
      } catch {
        if (current) setFailed("The units of this project could not be loaded.");
      }
    })();
    return () => { current = false; };
  }, [draft.projectId]);

  const units = useMemo(() => (loaded?.projectId === draft.projectId ? loaded.units : []), [loaded, draft.projectId]);
  const loading = draft.projectId !== "" && loaded?.projectId !== draft.projectId && !failed;
  const searching = filters.query.trim() !== "";
  const matches = useMemo(() => filterUnits(units, filters), [units, filters]);
  const groups = useMemo(() => groupByFloor(matches), [matches]);
  const page = useMemo(() => firstUnits(groups, shownCount), [groups, shownCount]);
  const shown = page.reduce((sum, entry) => sum + entry.shown.length, 0);
  const floorOptions: Option[] = useMemo(() => [{ value: "", label: "All floors" }, ...floorFilterOptions(units)], [units]);
  const typeOptions: Option[] = useMemo(() => [{ value: "", label: "All types" }, ...typeFilterOptions(units, UNIT_TYPES)], [units]);

  const changeFilters = (change: Partial<UnitFilters>) => { setFilters((current) => ({ ...current, ...change })); setShownCount(UNIT_PAGE_SIZE); };
  const changeProject = (projectId: string) => {
    const project = projects.find((p) => String(p.id) === projectId);
    setFilters(NO_FILTERS);
    setShownCount(UNIT_PAGE_SIZE);
    onChange({ ...withUnit(draft, null), projectId, projectName: project?.projectName ?? "" });
  };

  const row = (unit: PickerUnit, showFloor: boolean) => (
    <li key={unit.id}>
      <button
        type="button"
        aria-pressed={draft.unit?.id === unit.id}
        onClick={() => onChange(withUnit(draft, unit))}
        className="flex min-h-12 w-full cursor-pointer items-center gap-3 border-0 border-t border-line-soft bg-transparent px-3.5 py-2 text-left font-ui first:border-t-0 hover:bg-page focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary"
      >
        {tick(draft.unit?.id === unit.id)}
        <span className="min-w-0">
          <span className="block text-body font-extrabold text-ink">Unit {unit.unitNumber}</span>
          {showFloor && <span className="block text-small text-ink-muted">{floorName(unit.floorName, unit.floorNumber)}</span>}
        </span>
        <span className="min-w-0 flex-1 text-small text-ink-2">{unit.unitType} · {formatSqFt(unit.size)}</span>
        <span className="shrink-0 text-body font-extrabold tabular-nums text-ink">{formatPkr(unit.price)}</span>
      </button>
    </li>
  );

  const picked = draft.unit;
  const projectField = (
    <Dropdown
      label="Project"
      required
      placeholder="Select project"
      options={projects.map((project) => ({ value: String(project.id), label: project.projectName }))}
      value={draft.projectId}
      onChange={changeProject}
    />
  );

  return (
    <div className="flex flex-col gap-4">
      {picked ? (
        <div className="flex flex-col gap-1.5">
          {projectField}
          <p className="m-0 mt-2 text-label font-bold uppercase tracking-[0.4px] text-ink-2">Unit <span aria-hidden="true">*</span></p>
          <div className="flex items-center gap-3 rounded-card border-2 border-primary bg-card p-3.5">
            <span aria-hidden="true" className="flex size-10 shrink-0 items-center justify-center rounded-field bg-gold-soft text-gold-text"><IconBuilding size={20} /></span>
            <div className="min-w-0 flex-1">
              <p className="m-0 text-body font-extrabold text-ink">Unit {picked.unitNumber} · {picked.unitType}</p>
              <p className="m-0 text-small text-ink-muted">{floorName(picked.floorName, picked.floorNumber)} · {formatSqFt(picked.size)} · {draft.projectName}</p>
              <p className="m-0 mt-0.5 text-body font-extrabold tabular-nums text-ink">{formatPkr(picked.price)}</p>
            </div>
            <Button variant="outline" size="sm" onClick={() => onChange(withUnit(draft, null))}>Change</Button>
          </div>

          <p className="m-0 mt-3 text-label font-bold uppercase tracking-[0.4px] text-ink-muted">For the application form</p>
          <div className="grid gap-4 md:grid-cols-3">
            <TextField label="Serial no." placeholder="Auto" maxLength={50} value={draft.serialNo} onChange={(event) => onChange({ ...draft, serialNo: event.target.value })} />
            <TextField label="Category" maxLength={100} value={draft.category} onChange={(event) => onChange({ ...draft, category: event.target.value })} />
            <TextField label="Tower" maxLength={50} value={draft.tower} onChange={(event) => onChange({ ...draft, tower: event.target.value })} />
          </div>
          <Toggle checked={draft.isCorner} onChange={(isCorner) => onChange({ ...draft, isCorner })} label="Corner unit" />
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          <div className="grid gap-4 md:grid-cols-2">
            {projectField}
            <div className="flex flex-col gap-2">
              <span className="text-label font-bold uppercase tracking-[0.4px] text-ink-2">Unit <span aria-hidden="true">*</span></span>
              <SearchBar size="lg" value={filters.query} onSearch={(query) => changeFilters({ query })} placeholder="Unit number, e.g. 810" aria-label="Unit number" debounceMs={0} />
            </div>
            <Dropdown label="Floor" disabled={units.length === 0} options={floorOptions} value={filters.floor} onChange={(floor) => changeFilters({ floor })} />
            <Dropdown label="Type" disabled={units.length === 0} options={typeOptions} value={filters.type} onChange={(type) => changeFilters({ type })} />
          </div>

          {errors.unit && <p role="alert" className="m-0 text-small font-bold text-danger">{errors.unit}</p>}

          {!draft.projectId ? (
            <EmptyState title="Choose a project" message="Its available units are listed here." />
          ) : failed ? (
            <EmptyState title={failed} />
          ) : loading ? (
            <p className="m-0 py-8 text-center text-body text-ink-muted">Loading units…</p>
          ) : matches.length === 0 ? (
            <EmptyState title={searching ? `No unit matches “${filters.query.trim()}”` : "No available units"} message={searching ? "Check the number or clear the filters." : "Everything here is booked, or no unit fits these filters."} />
          ) : searching ? (
            <div className="overflow-hidden rounded-card border border-line">
              <p className="m-0 bg-page px-3.5 py-2 text-caption font-extrabold uppercase tracking-[0.4px] text-ink-2" role="status">
                {matches.length} {matches.length === 1 ? "unit matches" : "units match"} “{filters.query.trim()}”
              </p>
              <ul className="m-0 list-none p-0">{matches.slice(0, shownCount).map((unit) => row(unit, true))}</ul>
              {matches.length > shownCount && (
                <div className="flex justify-end border-t border-line-soft px-3.5 py-2.5">
                  <Button variant="link" onClick={() => setShownCount((count) => count + UNIT_PAGE_SIZE)}>Show more</Button>
                </div>
              )}
            </div>
          ) : (
            <div className="overflow-hidden rounded-card border border-line">
              {page.map(({ group, shown: rows }) => (
                <section key={group.floorNumber} aria-label={group.name}>
                  <h3 className="m-0 flex items-center justify-between bg-page px-3.5 py-2 text-caption font-extrabold uppercase tracking-[0.4px] text-ink-2">
                    <span>{group.name}</span>
                    <span>{group.units.length} available</span>
                  </h3>
                  <ul className="m-0 list-none p-0">{rows.map((unit) => row(unit, false))}</ul>
                </section>
              ))}
              <div className="flex items-center justify-between border-t border-line-soft px-3.5 py-2.5 text-small text-ink-muted">
                <span>Showing <b className="text-ink">{shown}</b> of <b className="text-ink">{matches.length}</b></span>
                {matches.length > shown && <Button variant="link" onClick={() => setShownCount((count) => count + UNIT_PAGE_SIZE)}>Show more</Button>}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
