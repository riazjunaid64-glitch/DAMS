import { useState, type ReactNode } from "react";
import type { DataTableColumn } from "../../../components/ui";
import { deleteRevenueCategory, type RevenueCategory } from "../revenueCategoryApi.ts";
import { CategoryList, CategoryName } from "./CategoryList.tsx";
import { RevenueFormDialog } from "./RevenueFormDialog.tsx";
import { revenueRetireMessage, revenueSearchText } from "./rules.ts";

const usage = (category: RevenueCategory) => category.revenueCount;
const description = (category: RevenueCategory) => category.description || <span className="text-ink-muted">—</span>;

const columns: DataTableColumn<RevenueCategory>[] = [
  { key: "name", header: "Category", render: (category) => <CategoryName name={category.name} isActive={category.isActive} /> },
  { key: "description", header: "Description", render: (category) => <span className="text-ink-2">{description(category)}</span> },
  { key: "used", header: "Used by", align: "right", render: (category) => <span className="tabular-nums text-ink-muted">{category.revenueCount.toLocaleString("en-PK")}</span> },
];

const phoneCard = (category: RevenueCategory, actions: ReactNode) => (
  <div className="rounded-card border border-line bg-card p-4 font-ui">
    <CategoryName name={category.name} isActive={category.isActive} />
    {category.description && <p className="m-0 mt-1 text-small text-ink-2">{category.description}</p>}
    <div className="mt-3 flex items-center justify-between gap-3">
      <span className="text-small text-ink-muted">Used by {category.revenueCount.toLocaleString("en-PK")}</span>
      {actions}
    </div>
  </div>
);

type Props = {
  /** Every revenue category, retired ones too; null until the first read answers. */
  categories: readonly RevenueCategory[] | null;
  loading: boolean;
  /** The header's Add category was pressed. */
  adding: boolean;
  onAddClose: () => void;
  /** A category was added, saved, retired or deleted. */
  onChanged: () => void;
};

/**
 * Finance settings > Revenue categories: the heads income is filed under, in the order the Profit &
 * Loss lists them (that order is set in the popup, not shown here).
 */
export function RevenueCategoriesTab({ categories, loading, adding, onAddClose, onChanged }: Props) {
  const [editing, setEditing] = useState<RevenueCategory | null>(null);

  return (
    <>
      <CategoryList
        rows={categories}
        loading={loading}
        noun="revenue categories"
        usage={usage}
        searchText={revenueSearchText}
        retireMessage={revenueRetireMessage}
        remove={deleteRevenueCategory}
        columns={columns}
        phoneCard={phoneCard}
        minWidth={720}
        onEdit={setEditing}
        onChanged={onChanged}
      />
      {(adding || editing) && (
        <RevenueFormDialog
          key={editing?.id ?? "new"}
          category={editing}
          onClose={() => { setEditing(null); onAddClose(); }}
          onSaved={onChanged}
        />
      )}
    </>
  );
}
