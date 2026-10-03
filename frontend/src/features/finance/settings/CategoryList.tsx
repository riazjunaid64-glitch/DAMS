import { useMemo, useState, type ReactNode } from "react";
import {
  ActionsMenu,
  Button,
  ConfirmDialog,
  DataTable,
  EmptyState,
  FilterBar,
  IconFolder,
  IconPencil,
  StatusBadge,
  useIsPhone,
  useToast,
  type DataTableColumn,
} from "../../../components/ui";
import { ListFooter } from "../ListParts.tsx";
import { clampPage, pageRows, PAGE_SIZE } from "../lists.ts";
import { DELETE_MESSAGE, filterCategories, OPENING_FILTERS, removalOf, STATUS_FILTER, type CategoryFilters, type Removal } from "./rules.ts";

export type CategoryRow = { id: number; name: string; isActive: boolean };

type Props<T extends CategoryRow> = {
  /** Every category, retired ones too, in the server's order; null until the first read answers. */
  rows: readonly T[] | null;
  loading: boolean;
  /** "expense categories": for the empty states and the table's caption. */
  noun: string;
  /** How many records are filed under the category; it decides Retire or Delete. */
  usage: (row: T) => number;
  /** The text the search looks in. Keep it stable (module level), so filtering runs only when it must. */
  searchText: (row: T) => readonly (string | null)[];
  /** The Retire question's sentence, for a category used this many times. */
  retireMessage: (usage: number) => string;
  /** The server retires a used category and deletes an unused one; `category` is null once deleted. */
  remove: (id: number) => Promise<{ category: unknown }>;
  /** The data columns; the list adds the pencil and ⋯ at the end. */
  columns: DataTableColumn<T>[];
  /** A phone card, given the row's pencil and ⋯. */
  phoneCard: (row: T, actions: ReactNode) => ReactNode;
  minWidth: number;
  onEdit: (row: T) => void;
  /** A category was retired or deleted (or that failed): read the lists again. */
  onChanged: () => void;
};

/** The name in bold, with a grey Retired badge once the category is off new entries. */
export function CategoryName({ name, isActive }: { name: string; isActive: boolean }) {
  return (
    <span className="inline-flex min-w-0 flex-wrap items-center gap-2">
      <b className="min-w-0 break-words font-extrabold text-ink">{name}</b>
      {!isActive && <StatusBadge status="Retired" />}
    </span>
  );
}

/**
 * One category tab: search and Status (opening on Active), the table or phone cards cut into pages of
 * twenty in the browser, a pencil and a ⋯ menu on each row, and the Retire / Delete question. Every
 * category is already loaded, so filtering and paging never ask the server.
 */
export function CategoryList<T extends CategoryRow>({
  rows, loading, noun, usage, searchText, retireMessage, remove, columns, phoneCard, minWidth, onEdit, onChanged,
}: Props<T>) {
  const isPhone = useIsPhone();
  const toast = useToast();
  const [filters, setFilters] = useState<CategoryFilters>(OPENING_FILTERS);
  const [page, setPage] = useState(1);
  const [shown, setShown] = useState(PAGE_SIZE);
  const [removing, setRemoving] = useState<{ row: T; removal: Removal } | null>(null);
  const [busy, setBusy] = useState(false);

  const matching = useMemo(() => filterCategories(rows ?? [], filters, searchText), [rows, filters, searchText]);
  // A retire (under Active) or a delete can empty the last page. The list already shows the new last
  // page; storing it as well means the list growing again does not jump back to a page the user left.
  const current = clampPage(page, matching.length);
  if (current !== page) setPage(current);
  const visible = isPhone ? matching.slice(0, shown) : pageRows(matching, current);

  const changeFilters = (changes: Partial<CategoryFilters>) => {
    setFilters((now) => ({ ...now, ...changes }));
    setPage(1);
    setShown(PAGE_SIZE);
  };

  const confirmRemove = async (row: T) => {
    setBusy(true);
    try {
      const result = await remove(row.id);
      // The server has the last word: anything filed against the category since the list loaded
      // turns a delete into a retire.
      toast.success(`${row.name} ${result.category ? "retired" : "deleted"}.`);
    } catch (failure) {
      toast.error(failure instanceof Error ? failure.message : "The category could not be removed.");
    } finally {
      setBusy(false);
      setRemoving(null);
      onChanged();
    }
  };

  const actions = (row: T) => {
    const removal = removalOf(usage(row), row.isActive);
    return (
      <span className="inline-flex items-center justify-end gap-2">
        <Button variant="outline" iconOnly icon={<IconPencil size={18} />} aria-label={`Edit ${row.name}`} onClick={() => onEdit(row)} />
        {removal ? (
          <ActionsMenu
            trigger="dots"
            aria-label={`More for ${row.name}`}
            items={[removal === "retire"
              ? { label: "Retire", onSelect: () => setRemoving({ row, removal }) }
              : { label: "Delete", danger: true, onSelect: () => setRemoving({ row, removal }) }]}
          />
        ) : (
          // A retired category in use has nothing to offer; the space keeps the pencils in line.
          <span aria-hidden="true" className="size-11 md:size-9" />
        )}
      </span>
    );
  };

  const allColumns: DataTableColumn<T>[] = [
    ...columns,
    { key: "actions", header: <span className="sr-only">Actions</span>, align: "right", render: actions },
  ];
  const nothingYet = rows !== null && rows.length === 0;
  const retiring = removing?.removal === "retire";

  return (
    <div className="flex flex-col gap-3 md:gap-4">
      <FilterBar
        search={{ value: filters.search, onSearch: (search) => changeFilters({ search }), placeholder: "Search categories" }}
        filters={STATUS_FILTER}
        values={{ status: filters.status }}
        defaults={{ status: OPENING_FILTERS.status }}
        onChange={(changes) => changeFilters({ status: changes.status ?? filters.status })}
        onReset={() => changeFilters(OPENING_FILTERS)}
      />
      <DataTable
        columns={allColumns}
        rows={visible}
        rowKey={(row) => row.id}
        phoneCard={(row) => phoneCard(row, actions(row))}
        loading={loading}
        loadingCount={8}
        minWidth={minWidth}
        caption={noun.charAt(0).toUpperCase() + noun.slice(1)}
        empty={<EmptyState icon={<IconFolder size={26} />} title={nothingYet ? `No ${noun} yet. Add one.` : `No ${noun} match.`} />}
      />
      {rows !== null && (
        <ListFooter
          total={matching.length}
          isPhone={isPhone}
          page={current}
          onPage={setPage}
          shown={shown}
          onLoadMore={() => setShown((count) => count + PAGE_SIZE)}
        />
      )}
      {removing && (
        <ConfirmDialog
          open
          onClose={() => !busy && setRemoving(null)}
          onConfirm={() => void confirmRemove(removing.row)}
          title={`${retiring ? "Retire" : "Delete"} ${removing.row.name}?`}
          message={retiring ? retireMessage(usage(removing.row)) : DELETE_MESSAGE}
          confirmLabel={retiring ? "Retire" : "Delete"}
          danger={!retiring}
          loading={busy}
        />
      )}
    </div>
  );
}
