import { LoadMore, Pagination } from "../../../components/ui";
import { PAGE_SIZE } from "./rules.ts";

type Props = {
  total: number;
  isPhone: boolean;
  /** Desktop: the page on show. */
  page: number;
  onPage: (page: number) => void;
  /** Phone: how many rows are on screen so far. */
  shown: number;
  onLoadMore: () => void;
};

/**
 * Under both lists. The lists are cut into pages here, not on the server: a desktop has numbered
 * pages and a phone appends twenty more with each Load more, with the count under it either way.
 */
export function ListFooter({ total, isPhone, page, onPage, shown, onLoadMore }: Props) {
  if (total === 0) return null;
  if (!isPhone) return <Pagination page={page} pageSize={PAGE_SIZE} totalCount={total} itemLabel="entries" onPageChange={onPage} />;
  const visible = Math.min(shown, total);
  return (
    <div className="flex flex-col items-center gap-2 font-ui">
      <LoadMore shown={visible} total={total} showCount={false} onLoadMore={onLoadMore} />
      <p className="m-0 text-small text-ink-muted">
        Showing <b className="font-extrabold text-ink">{visible.toLocaleString("en-PK")}</b> of <b className="font-extrabold text-ink">{total.toLocaleString("en-PK")}</b> entries
      </p>
    </div>
  );
}
