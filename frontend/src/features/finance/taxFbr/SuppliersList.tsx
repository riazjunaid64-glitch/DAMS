import { Button, DataTable, EmptyState, IconFile, Notice, StatusBadge, type DataTableColumn } from "../../../components/ui";
import { CardFigure, ListFooter } from "../ListParts.tsx";
import { clampPage, pageRows } from "../lists.ts";
import { formatRs, type WhtVendorLine } from "../whtTypes.ts";
import { sectionLabel, supplierKey, supplierTaxId } from "./rules.ts";

type Props = {
  /** The whole s.165 statement; null until it has answered, and again after a failure. */
  rows: readonly WhtVendorLine[] | null;
  loading: boolean;
  error: string | null;
  /** A From / To range is set, which changes what "empty" means. */
  ranged: boolean;
  isPhone: boolean;
  page: number;
  shown: number;
  onPage: (page: number) => void;
  onLoadMore: () => void;
  onRetry: () => void;
};

/** A line, with the position it came in: one supplier can have several lines, and the order is the server's. */
type Line = WhtVendorLine & { index: number };

const money = "whitespace-nowrap tabular-nums";

/**
 * The s.165 statement: what was withheld from each supplier, highest tax first as the server sorts it.
 * The status is the one the tax was withheld under on the day, so a supplier whose status or section
 * changed has a line for each. Nothing here is edited.
 */
export function SuppliersList({ rows, loading, error, ranged, isPhone, page, shown, onPage, onLoadMore, onRetry }: Props) {
  const total = rows?.length ?? 0;
  const lines: Line[] = (rows ?? []).map((line, index) => ({ ...line, index }));
  const current = clampPage(page, total);
  const visible = isPhone ? lines.slice(0, shown) : pageRows(lines, current);

  const columns: DataTableColumn<Line>[] = [
    { key: "supplier", header: "Supplier", render: (line) => <b className="font-extrabold text-ink">{line.vendorName}</b> },
    { key: "taxId", header: "NTN / CNIC", render: (line) => <span className="whitespace-nowrap text-ink-2">{supplierTaxId(line)}</span> },
    { key: "status", header: "Status", render: (line) => <StatusBadge status={line.filerStatus} /> },
    { key: "section", header: "Section", render: (line) => <span className="whitespace-nowrap font-bold text-ink">{line.taxSection || "—"}</span> },
    { key: "gross", header: "Paid in total", align: "right", render: (line) => <span className={`${money} text-ink-2`}>{formatRs(line.grossAmount)}</span> },
    { key: "tax", header: "Tax withheld", align: "right", render: (line) => <b className={`${money} font-extrabold text-gold-text`}>{formatRs(line.whtAmount)}</b> },
    { key: "net", header: "Paid to supplier", align: "right", render: (line) => <span className={`${money} text-ink-2`}>{formatRs(line.netPaid)}</span> },
  ];

  const phoneCard = (line: Line) => (
    <div className="rounded-card border border-line bg-card p-4 font-ui">
      <div className="flex items-start justify-between gap-3">
        <b className="min-w-0 break-words font-extrabold text-ink">{line.vendorName}</b>
        <StatusBadge status={line.filerStatus} />
      </div>
      <p className="m-0 mt-0.5 text-small text-ink-muted">{supplierTaxId(line)} · {sectionLabel(line.taxSection)}</p>
      <div className="mt-3 grid grid-cols-3 gap-2">
        <CardFigure label="Paid in total">{formatRs(line.grossAmount)}</CardFigure>
        <CardFigure label="Tax withheld" gold>{formatRs(line.whtAmount)}</CardFigure>
        <CardFigure label="To supplier">{formatRs(line.netPaid)}</CardFigure>
      </div>
    </div>
  );

  return (
    <div className="flex flex-col gap-3">
      {error && <Notice tone="red" role="alert" title={error} action={<Button variant="outline" onClick={onRetry}>Try again</Button>} />}
      {!error && (
        <DataTable
          columns={columns}
          rows={visible}
          rowKey={(line) => supplierKey(line, line.index)}
          phoneCard={phoneCard}
          loading={loading}
          loadingCount={4}
          minWidth={900}
          caption="Tax withheld by supplier"
          empty={<EmptyState icon={<IconFile size={26} />} title={ranged ? "No tax was withheld in this period." : "No tax was withheld."} />}
        />
      )}
      {!error && rows !== null && <ListFooter total={total} isPhone={isPhone} page={current} onPage={onPage} shown={shown} onLoadMore={onLoadMore} />}
    </div>
  );
}
