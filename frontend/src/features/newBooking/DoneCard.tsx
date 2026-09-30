import { Button, IconCheck, IconPrinter } from "../../components/ui";

type Props = {
  reference: string;
  unitNumber: string;
  customerName: string;
  /** Only when money was received with the form. */
  paymentId: number | null;
  onPrintForm: () => void;
  onPrintReceipt: () => void;
  onOpen: () => void;
};

/** What the person sees once the booking exists. Print receipt is offered only when money was received. */
export function DoneCard({ reference, unitNumber, customerName, paymentId, onPrintForm, onPrintReceipt, onOpen }: Props) {
  return (
    <div className="mx-auto mt-8 w-full max-w-[460px] px-4 font-ui md:mt-12">
      <div className="flex flex-col items-center gap-3 rounded-card border border-line bg-card px-6 py-8 text-center">
        <span aria-hidden="true" className="flex size-12 items-center justify-center rounded-full bg-success-soft text-success"><IconCheck size={24} /></span>
        <h1 className="m-0 text-section font-extrabold text-ink md:text-[20px]">Booking {reference} created</h1>
        <p className="m-0 text-body text-ink-2">Unit {unitNumber} is now booked for {customerName}.</p>
        <div className="mt-2 flex w-full flex-col gap-2.5 md:flex-row md:justify-center">
          <Button variant="outline" icon={<IconPrinter size={16} />} onClick={onPrintForm}>Print application form</Button>
          {paymentId !== null && <Button variant="outline" icon={<IconPrinter size={16} />} onClick={onPrintReceipt}>Print receipt</Button>}
          <Button onClick={onOpen}>Open booking</Button>
        </div>
      </div>
    </div>
  );
}
