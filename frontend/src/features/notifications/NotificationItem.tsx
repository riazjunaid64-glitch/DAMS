import { cx } from "../../components/ui/cx.ts";
import { IconBell, IconCalendar, IconMapPin, IconPhone, IconUserPlus } from "../../components/ui/icons.tsx";
import { notificationVisual, notificationWhen, type NotificationKind, type NotificationTile } from "./notificationPresentation.ts";
import type { NotificationItem as Item } from "./types.ts";

const tileClass: Record<NotificationTile, string> = {
  red: "bg-danger-soft text-danger",
  blue: "bg-info-soft text-info",
  gold: "bg-gold-soft text-gold-text",
  grey: "bg-steel-soft text-steel",
};

function TileIcon({ kind }: { kind: NotificationKind }) {
  if (kind === "call") return <IconPhone size={18} />;
  if (kind === "lead") return <IconUserPlus size={18} />;
  if (kind === "followup") return <IconCalendar size={18} />;
  if (kind === "visit") return <IconMapPin size={18} />;
  return <IconBell size={18} />;
}

/** One notification: icon, title, detail, and how long ago. Unread rows carry a gold dot. */
export function NotificationRow({ item, onOpen, busy = false }: { item: Item; onOpen: () => void; busy?: boolean }) {
  const visual = notificationVisual(item);
  return (
    <button
      type="button"
      disabled={busy}
      onClick={onOpen}
      className={cx(
        "flex w-full items-start gap-3 px-4 py-3 text-left font-ui transition-colors hover:bg-page",
        "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary disabled:opacity-60",
        !item.isRead && "bg-gold-soft",
      )}
    >
      <span aria-hidden="true" className={cx("mt-3.5 size-2 shrink-0 rounded-full", item.isRead ? "bg-transparent" : "bg-gold")} />
      {!item.isRead && <span className="sr-only">Unread</span>}
      <span className={cx("flex size-9 shrink-0 items-center justify-center rounded-field", tileClass[visual.tile])}>
        <TileIcon kind={visual.kind} />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-sm font-extrabold text-ink">{item.title}</span>
        <span className="mt-0.5 block text-small text-ink-2">{item.message}</span>
      </span>
      <time dateTime={item.createdAt} className="shrink-0 text-small text-ink-muted">{notificationWhen(item.createdAt)}</time>
    </button>
  );
}
