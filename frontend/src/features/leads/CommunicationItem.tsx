import { Card, IconBuilding, IconCalendar, IconMail, IconMapPin, IconPhone, IconUsers, IconWhatsapp, StatusBadge } from "../../components/ui";
import { formatWhen } from "../../lib/dates.ts";
import { channelLabel, contactVerb, reachBadge } from "./labels.ts";
import type { Communication } from "./types.ts";

function ChannelIcon({ channel }: { channel: string }) {
  switch (channel) {
    case "Phone": return <IconPhone size={18} />;
    case "Whatsapp":
    case "Sms": return <IconWhatsapp size={18} />;
    case "Email": return <IconMail size={18} />;
    case "OfficeVisit": return <IconBuilding size={18} />;
    case "SiteVisit": return <IconMapPin size={18} />;
    default: return <IconUsers size={18} />;
  }
}

/** One logged conversation: how, who, what was said, and the follow-up it set. */
export function CommunicationItem({ item }: { item: Communication }) {
  const badge = reachBadge(item);
  return (
    <Card className="flex gap-3 md:gap-4">
      <span className="flex size-9 shrink-0 items-center justify-center rounded-field bg-page text-ink-2">
        <ChannelIcon channel={item.channel} />
      </span>
      <div className="min-w-0 flex-1">
        <p className="m-0 flex flex-wrap items-center gap-2 text-body font-extrabold text-ink">
          {channelLabel(item.channel)}
          {badge && <StatusBadge status={badge.label} tone={badge.tone}>{badge.label}</StatusBadge>}
        </p>
        <p className="m-0 mt-0.5 text-small text-ink-muted">
          {[contactVerb(item.channel, item.direction), item.employeeName, formatWhen(item.occurredAt)].filter(Boolean).join(" · ")}
        </p>
        <p className="m-0 mt-2 whitespace-pre-wrap break-words text-sm text-ink">{item.summary}</p>
        {item.customerResponse && (
          <div className="mt-3 rounded-field bg-page p-3">
            <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">Customer said</p>
            <p className="m-0 mt-0.5 whitespace-pre-wrap break-words text-sm text-ink">{item.customerResponse}</p>
          </div>
        )}
        {item.nextAction && (
          <StatusBadge status="next" tone="gold" className="mt-3 h-auto! min-h-6 max-w-full gap-1.5 py-1 whitespace-normal!">
            <IconCalendar size={14} />
            <span>Next: {[item.nextAction, item.nextActionAt && formatWhen(item.nextActionAt)].filter(Boolean).join(" · ")}</span>
          </StatusBadge>
        )}
      </div>
    </Card>
  );
}
