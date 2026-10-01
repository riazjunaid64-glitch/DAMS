import { useState } from "react";
import {
  BottomSheet, Button, IconBlock, IconChevronLeft, IconIdCard, IconMore, IconPencil, IconPhone, IconPlus, PageHeader, useIsPhone,
} from "../../components/ui";
import { formatDay } from "../../lib/dates.ts";
import { formatPhone } from "../bookings/format.ts";
import { customerBadge, headerActions } from "./customerHeader.ts";
import type { CustomerDetail } from "./customerForm.ts";

type Props = {
  customer: CustomerDetail;
  onBack: () => void;
  onEdit: () => void;
  onBlock: () => void;
  onUnblock: () => void;
  onNewBooking: () => void;
};

/**
 * The top of the customer page: who they are, whether they can still book, and the buttons that
 * follow from it. Desktop lists Edit / Block / New booking (Edit / Unblock when blocked). A phone gets
 * New booking and ⋯ (Edit details, Block customer); a blocked customer gets Unblock and the Edit pencil.
 * Which buttons exist is decided in `headerActions`, once, for both.
 */
export function CustomerHeader({ customer, onBack, onEdit, onBlock, onUnblock, onNewBooking }: Props) {
  const isPhone = useIsPhone();
  const [sheetOpen, setSheetOpen] = useState(false);
  const actions = headerActions(customer);

  const details = (
    <>
      <a href={`tel:${customer.phone}`} className="inline-flex items-center gap-2 font-extrabold text-primary no-underline hover:underline">
        <IconPhone size={16} className="text-ink-muted" />{formatPhone(customer.phone)}
      </a>
      {customer.cnic && <span className="inline-flex items-center gap-2 text-ink-2"><IconIdCard size={16} className="text-ink-muted" />{customer.cnic}</span>}
      <span className="text-ink-muted">Customer since {formatDay(customer.createdAt)}</span>
    </>
  );

  const editPencil = (
    <Button iconOnly variant="outline" icon={<IconPencil size={18} />} aria-label={`Edit ${customer.fullName}`} onClick={onEdit} />
  );

  const desktopActions = (
    <>
      <Button variant="outline" icon={<IconPencil size={16} />} onClick={onEdit}>Edit</Button>
      {actions.block && <Button variant="danger" icon={<IconBlock size={16} />} onClick={onBlock}>Block</Button>}
      {actions.unblock && <Button variant="outline" onClick={onUnblock}>Unblock</Button>}
      {actions.newBooking && <Button icon={<IconPlus size={16} />} onClick={onNewBooking}>New booking</Button>}
    </>
  );

  const phoneActions = (
    <div className="flex items-center gap-2.5">
      {actions.newBooking
        ? <Button size="lg" icon={<IconPlus size={16} />} className="flex-1" onClick={onNewBooking}>New booking</Button>
        : <Button size="lg" variant="outline" className="flex-1" onClick={onUnblock}>Unblock</Button>}
      {actions.phoneMenu
        ? <Button iconOnly variant="outline" icon={<IconMore size={18} />} aria-label="More actions" onClick={() => setSheetOpen(true)} />
        : editPencil}
    </div>
  );

  return (
    <header className="rounded-card border border-line bg-card p-4 font-ui md:p-5">
      <div className="flex items-start gap-4">
        {!isPhone && <Button iconOnly variant="outline" icon={<IconChevronLeft size={18} />} aria-label="Back to Customers" onClick={onBack} />}
        <PageHeader
          className="min-w-0 flex-1 md:items-start"
          title={customer.fullName}
          badge={customerBadge(customer)}
          details={details}
          actions={isPhone ? undefined : desktopActions}
        />
      </div>
      {isPhone && <div className="mt-3">{phoneActions}</div>}

      <BottomSheet open={sheetOpen} onClose={() => setSheetOpen(false)} title="More actions" footer={null}>
        <ul className="m-0 flex list-none flex-col p-0">
          <li>
            <button
              type="button"
              onClick={() => { setSheetOpen(false); onEdit(); }}
              className="flex h-12 w-full cursor-pointer items-center gap-3 border-0 bg-transparent px-0 text-left text-body font-extrabold text-ink focus-visible:outline-2 focus-visible:outline-primary"
            >
              <IconPencil size={18} className="text-ink-2" />Edit details
            </button>
          </li>
          <li>
            <button
              type="button"
              onClick={() => { setSheetOpen(false); onBlock(); }}
              className="flex h-12 w-full cursor-pointer items-center gap-3 border-0 bg-transparent px-0 text-left text-body font-extrabold text-danger focus-visible:outline-2 focus-visible:outline-primary"
            >
              <IconBlock size={18} />Block customer
            </button>
          </li>
        </ul>
      </BottomSheet>
    </header>
  );
}
