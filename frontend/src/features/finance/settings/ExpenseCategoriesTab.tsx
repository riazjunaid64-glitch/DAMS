import { useState, type ReactNode } from "react";
import type { DataTableColumn } from "../../../components/ui";
import { CardFigure } from "../ListParts.tsx";
import * as whtApi from "../whtApi.ts";
import type { ExpenseCategory } from "../whtTypes.ts";
import { CategoryFormDialog } from "./CategoryFormDialog.tsx";
import { CategoryList, CategoryName } from "./CategoryList.tsx";
import { expenseRetireMessage, expenseSearchText, rateText, sectionText, yearlyLimitText } from "./rules.ts";

const usage = (category: ExpenseCategory) => category.usageCount;

/** "153(1)(a)" in bold, or a grey "No tax". */
function Section({ category }: { category: ExpenseCategory }) {
  const text = sectionText(category);
  return text === null ? <span className="text-ink-muted">No tax</span> : <b className="whitespace-nowrap font-extrabold text-ink">{text}</b>;
}

const columns: DataTableColumn<ExpenseCategory>[] = [
  { key: "name", header: "Category", render: (category) => <CategoryName name={category.name} isActive={category.isActive} /> },
  { key: "section", header: "Tax section", render: (category) => <Section category={category} /> },
  { key: "filer", header: "Filer", align: "right", render: (category) => <b className="font-extrabold">{rateText(category, category.filerRate)}</b> },
  { key: "nonFiler", header: "Non-filer", align: "right", render: (category) => <b className="font-extrabold">{rateText(category, category.nonFilerRate)}</b> },
  {
    key: "limit",
    header: "Yearly limit",
    align: "right",
    // "From Rs 1": no allowance, tax from the first rupee.
    render: (category) => (
      <span className={`whitespace-nowrap tabular-nums ${category.isWhtApplicable && category.annualThreshold > 0 ? "text-ink-2" : "text-ink-muted"}`}>
        {yearlyLimitText(category)}
      </span>
    ),
  },
  { key: "used", header: "Used by", align: "right", render: (category) => <span className="tabular-nums text-ink-muted">{category.usageCount.toLocaleString("en-PK")}</span> },
];

const phoneCard = (category: ExpenseCategory, actions: ReactNode) => (
  <div className="rounded-card border border-line bg-card p-4 font-ui">
    <div className="flex items-start justify-between gap-3">
      <CategoryName name={category.name} isActive={category.isActive} />
      <span className="shrink-0 text-small"><Section category={category} /></span>
    </div>
    <div className="mt-3 grid grid-cols-3 gap-2">
      <CardFigure label="Filer">{rateText(category, category.filerRate)}</CardFigure>
      <CardFigure label="Non-filer">{rateText(category, category.nonFilerRate)}</CardFigure>
      <CardFigure label="Yearly limit">{yearlyLimitText(category)}</CardFigure>
    </div>
    <div className="mt-3 flex items-center justify-between gap-3">
      <span className="text-small text-ink-muted">Used by {category.usageCount.toLocaleString("en-PK")}</span>
      {actions}
    </div>
  </div>
);

type Props = {
  /** Every expense category, retired ones too; null until the first read answers. */
  categories: readonly ExpenseCategory[] | null;
  loading: boolean;
  /** The header's Add category was pressed. */
  adding: boolean;
  onAddClose: () => void;
  /** A category was added, saved, retired or deleted. */
  onChanged: () => void;
};

/** Finance settings > Expense categories: the heads expenses are filed under and the tax withheld on each. */
export function ExpenseCategoriesTab({ categories, loading, adding, onAddClose, onChanged }: Props) {
  const [editing, setEditing] = useState<ExpenseCategory | null>(null);

  return (
    <>
      <CategoryList
        rows={categories}
        loading={loading}
        noun="expense categories"
        usage={usage}
        searchText={expenseSearchText}
        retireMessage={expenseRetireMessage}
        remove={whtApi.deleteCategory}
        columns={columns}
        phoneCard={phoneCard}
        minWidth={900}
        onEdit={setEditing}
        onChanged={onChanged}
      />
      {(adding || editing) && (
        <CategoryFormDialog
          key={editing?.id ?? "new"}
          category={editing}
          onClose={() => { setEditing(null); onAddClose(); }}
          onSaved={onChanged}
        />
      )}
    </>
  );
}
