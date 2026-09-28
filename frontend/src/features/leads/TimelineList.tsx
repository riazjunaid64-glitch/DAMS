import { Button, Card, cx } from "../../components/ui";
import { formatDayHeading, formatTime } from "../../lib/dates.ts";
import { timelineDays, timelineDot } from "./leadPage.ts";
import type { TimelineItem } from "./types.ts";

const DOT = { navy: "bg-primary", gold: "bg-gold", grey: "bg-ink-faint" } as const;

/** What happened on a lead, newest first, under each Karachi day. */
export function TimelineList({ items, onShowOlder, loadingOlder }: {
  items: TimelineItem[];
  /** Set while older entries remain. */
  onShowOlder?: () => void;
  loadingOlder?: boolean;
}) {
  return (
    <Card>
      <div className="flex flex-col gap-5">
        {timelineDays(items, (value) => formatDayHeading(value)).map(({ day, items: entries }) => (
          <section key={day}>
            <h3 className="m-0 mb-2 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">{day}</h3>
            <ol className="m-0 list-none p-0">
              {entries.map((item) => (
                <li key={item.id} className="grid grid-cols-[auto_1fr] gap-x-3 md:grid-cols-[72px_auto_1fr]">
                  <span className="hidden pt-0.5 text-small text-ink-muted md:block">{formatTime(item.occurredAt)}</span>
                  <span className="relative flex justify-center pt-1.5 before:absolute before:top-5 before:bottom-0 before:w-px before:bg-line-soft">
                    <span className={cx("relative size-2.5 rounded-full", DOT[timelineDot(item)])} />
                  </span>
                  <div className="min-w-0 pb-4">
                    <p className="m-0 text-sm font-bold text-ink">{item.summary}</p>
                    {item.notes && <p className="m-0 mt-0.5 whitespace-pre-wrap break-words text-small text-ink-2">{item.notes}</p>}
                    <p className="m-0 mt-0.5 text-small text-ink-muted">
                      {item.performedByName ?? (item.isSystemGenerated ? "System" : "Staff")}
                      <span className="md:hidden"> · {formatTime(item.occurredAt)}</span>
                    </p>
                  </div>
                </li>
              ))}
            </ol>
          </section>
        ))}
      </div>
      {onShowOlder && (
        <div className="mt-2 flex justify-center">
          <Button variant="outline" loading={loadingOlder} onClick={onShowOlder}>Show older</Button>
        </div>
      )}
    </Card>
  );
}
