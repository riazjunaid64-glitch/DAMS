import { useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { Button, IconChevronLeft, IconPrinter, useIsPhone } from "../../components/ui";

type Props = {
  title: string;
  subtitle?: string;
  /** Where Back goes: the booking, or the Bookings list for the blank form. */
  onBack: () => void;
  backLabel: string;
  children: ReactNode;
};

/**
 * The page around a printed paper: Back, the title, one Print or save PDF button, and the paper at A4
 * width. On a phone the paper is shrunk to fit the screen and the button is fixed above the bottom
 * bar. Printing shows the paper alone (the `no-print` parts drop out; the shrink is undone in print).
 */
export function PaperPage({ title, subtitle, onBack, backLabel, children }: Props) {
  const isPhone = useIsPhone();
  const print = () => window.print();

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 pb-28 font-ui md:px-8 md:py-7 md:pb-7">
      <header className="no-print mx-auto flex w-full max-w-[210mm] items-center gap-4 md:rounded-card md:border md:border-line md:bg-card md:p-3.5">
        {!isPhone && (
          <Button iconOnly variant="outline" icon={<IconChevronLeft size={18} />} aria-label={backLabel} onClick={onBack} />
        )}
        <div className="min-w-0 flex-1">
          <h1 className="m-0 text-[22px] leading-tight font-extrabold text-ink md:text-section">{title}</h1>
          {subtitle && <p className="m-0 mt-1 text-small text-ink-muted md:mt-0.5">{subtitle}</p>}
        </div>
        {!isPhone && (
          <Button icon={<IconPrinter size={16} />} onClick={print}>Print or save PDF</Button>
        )}
      </header>

      {isPhone ? <FittedPaper>{children}</FittedPaper> : children}

      {isPhone && (
        <div className="no-print fixed inset-x-0 bottom-[calc(64px+env(safe-area-inset-bottom))] z-20 border-t border-line bg-card px-4 py-3">
          <Button size="lg" fullWidth icon={<IconPrinter size={18} />} onClick={print}>Print or save PDF</Button>
        </div>
      )}
    </div>
  );
}

/** Shows the A4 paper scaled down to the screen width, keeping the page as tall as the shrunk paper. */
function FittedPaper({ children }: { children: ReactNode }) {
  const box = useRef<HTMLDivElement>(null);
  const paper = useRef<HTMLDivElement>(null);
  const [fit, setFit] = useState({ scale: 1, height: 0 });

  useLayoutEffect(() => {
    const measure = () => {
      if (!box.current || !paper.current) return;
      const width = paper.current.offsetWidth;
      const scale = width > 0 ? Math.min(1, box.current.clientWidth / width) : 1;
      setFit((old) => (old.scale === scale && old.height === paper.current!.offsetHeight ? old : { scale, height: paper.current!.offsetHeight }));
    };
    measure();
    window.addEventListener("resize", measure);
    const observer = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(measure);
    if (paper.current) observer?.observe(paper.current);
    return () => {
      window.removeEventListener("resize", measure);
      observer?.disconnect();
    };
  }, []);

  return (
    <div ref={box} className="paper-fit-box overflow-hidden rounded-card shadow-sm" style={{ height: fit.height ? fit.height * fit.scale : undefined }}>
      <div ref={paper} className="paper-fit" style={{ width: "210mm", transform: `scale(${fit.scale})`, transformOrigin: "top left" }}>
        {children}
      </div>
    </div>
  );
}
