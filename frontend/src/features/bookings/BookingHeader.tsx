import { useState, type ReactNode } from "react";
import {
  BottomSheet,
  Button,
  IconBuilding,
  IconChevronLeft,
  IconClose,
  IconMore,
  IconPhone,
  IconPlus,
  IconPrinter,
  StatusBadge,
  useIsPhone,
} from "../../components/ui";
import { formatDay } from "../../lib/dates.ts";
import { Icons } from "./tokens.tsx";
import type { BookingDetail } from "./detailTypes.ts";
import { formatPhone } from "./format.ts";
import { headerActions, phoneActions, type MainActionKind } from "./headerActions.ts";

type Props = {
  booking: BookingDetail;
  /** The last reload of the booking failed, so its figures are old: nothing that moves money or state is offered. */
  stale: boolean;
  onBack: () => void;
  onPrint: () => void;
  onCancel: () => void;
  onMain: (kind: MainActionKind) => void;
};

const iconFor: Partial<Record<MainActionKind, ReactNode>> = { recordPayment: <IconPlus size={16} /> };

/**
 * The top of the booking page: who and what, and the buttons the current status allows. Desktop lists
 * them in a row; a phone gets one main button and folds the rest into ⋯ (or a printer icon when Print
 * is all that is left). Which buttons exist is decided in `headerActions`, once, for both.
 */
export function BookingHeader({ booking, stale, onBack, onPrint, onCancel, onMain }: Props) {
  const isPhone = useIsPhone();
  const [sheetOpen, setSheetOpen] = useState(false);
  const actions = headerActions(booking);
  const phone = phoneActions(actions);
  const main = actions.main;

  const mainButton = (size: "md" | "lg") => main && (
    <Button
      size={size}
      variant={main.kind === "completeSale" ? "success" : "primary"}
      icon={iconFor[main.kind]}
      disabled={main.disabled || stale}
      onClick={() => onMain(main.kind)}
      className={size === "lg" ? "flex-1" : undefined}
    >
      {main.label}
    </Button>
  );

  const meta = (
    <>
      <span className="inline-flex items-center gap-2 font-extrabold text-ink"><Icons.user className="h-4 w-4 text-ink-muted" />{booking.customerName}</span>
      <a href={`tel:${booking.customerPhone}`} className="inline-flex items-center gap-2 font-extrabold text-primary no-underline hover:underline">
        <IconPhone size={16} className="text-ink-muted" />{formatPhone(booking.customerPhone)}
      </a>
      <span className="inline-flex items-center gap-2 text-ink-2">
        <IconBuilding size={16} className="text-ink-muted" />
        <b className="font-extrabold text-ink">Unit {booking.unitNumber}</b>
        <span>· {booking.projectName}</span>
      </span>
      <span className="text-ink-muted">Booked {formatDay(booking.bookingDate)}</span>
    </>
  );

  return (
    <header className="rounded-card border border-line bg-card p-4 font-ui md:p-5">
      {isPhone ? (
        <div className="flex flex-col gap-1.5">
          <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
            <h1 className="m-0 text-[22px] leading-tight font-extrabold text-ink">{booking.bookingReference}</h1>
            <StatusBadge status={booking.status} />
          </div>
          <div className="flex flex-col items-start gap-1.5 text-body">{meta}</div>
          <div className="mt-2.5 flex items-center gap-2.5">
            {mainButton("lg")}
            {phone.more === "menu" ? (
              <Button iconOnly variant="outline" icon={<IconMore size={18} />} aria-label="More actions" onClick={() => setSheetOpen(true)} />
            ) : (
              <Button iconOnly variant="outline" icon={<IconPrinter size={18} />} aria-label="Print application form" onClick={onPrint} />
            )}
          </div>
        </div>
      ) : (
        <div className="flex items-start gap-4">
          <Button iconOnly variant="outline" icon={<IconChevronLeft size={18} />} aria-label="Back to Bookings" onClick={onBack} />
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2.5">
              <h1 className="m-0 text-page-title font-extrabold text-ink">{booking.bookingReference}</h1>
              <StatusBadge status={booking.status} />
            </div>
            <div className="mt-2 flex flex-wrap items-center gap-x-5 gap-y-1.5 text-body">{meta}</div>
          </div>
          <div className="flex shrink-0 flex-wrap items-center justify-end gap-2.5">
            <Button variant="outline" icon={<IconPrinter size={16} />} onClick={onPrint}>Print application form</Button>
            {actions.cancel && <Button variant="danger" onClick={onCancel}>Cancel booking</Button>}
            {mainButton("md")}
          </div>
        </div>
      )}

      <BottomSheet open={sheetOpen} onClose={() => setSheetOpen(false)} title="More actions" footer={null}>
        <ul className="m-0 flex list-none flex-col p-0">
          {phone.extras.map((extra) => {
            const danger = extra === "cancel";
            return (
              <li key={extra}>
                <button
                  type="button"
                  onClick={() => {
                    setSheetOpen(false);
                    (danger ? onCancel : onPrint)();
                  }}
                  className={`flex h-12 w-full cursor-pointer items-center gap-3 border-0 bg-transparent px-0 text-left text-body font-extrabold focus-visible:outline-2 focus-visible:outline-primary ${danger ? "text-danger" : "text-ink"}`}
                >
                  {danger ? <IconClose size={18} /> : <IconPrinter size={18} className="text-ink-2" />}
                  {danger ? "Cancel booking" : "Print application form"}
                </button>
              </li>
            );
          })}
        </ul>
      </BottomSheet>
    </header>
  );
}
