import { useRef, useState, type KeyboardEvent } from "react";
import { cx } from "./cx.ts";
import { IconChevronLeft, IconChevronRight, IconImage } from "./icons.tsx";

export type Photo = {
  id: string | number;
  src: string;
  alt?: string;
  isCover?: boolean;
};

export type PhotoSliderProps = {
  photos: readonly Photo[];
  /** Photo shown first. */
  initialIndex?: number;
  /** How the image fills the frame; `contain` for the full-size viewer. */
  fit?: "cover" | "contain";
  className?: string;
};

const SWIPE_PX = 40;

const arrowClass =
  "absolute top-1/2 flex size-11 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full bg-card/95 text-ink shadow-menu hover:bg-card focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary md:size-10";

/** Big image with round left / right arrows and a "1 / 5" counter. Swipe on phone, arrow keys when focused. */
export function PhotoSlider({ photos, initialIndex = 0, fit = "cover", className }: PhotoSliderProps) {
  const [index, setIndex] = useState(() => Math.min(Math.max(initialIndex, 0), Math.max(photos.length - 1, 0)));
  const swipeFrom = useRef<number | null>(null);
  const count = photos.length;
  const current = photos[Math.min(index, count - 1)];
  const go = (step: number) => setIndex((i) => (i + step + count) % count);

  if (!current) {
    return (
      <div className={cx("flex aspect-[16/9] items-center justify-center rounded-card bg-track text-ink-faint", className)}>
        <IconImage size={32} />
      </div>
    );
  }

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
      event.preventDefault();
      go(event.key === "ArrowRight" ? 1 : -1);
    }
  };

  return (
    <div
      role="group"
      aria-roledescription="carousel"
      aria-label={`Photo ${index + 1} of ${count}`}
      tabIndex={0}
      onKeyDown={onKeyDown}
      onPointerDown={(event) => { swipeFrom.current = event.clientX; }}
      onPointerUp={(event) => {
        if (swipeFrom.current === null || count < 2) return;
        const dx = event.clientX - swipeFrom.current;
        swipeFrom.current = null;
        if (Math.abs(dx) >= SWIPE_PX) go(dx < 0 ? 1 : -1);
      }}
      className={cx("relative aspect-[16/9] touch-pan-y select-none overflow-hidden rounded-card bg-track font-ui outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary", className)}
    >
      <img
        src={current.src}
        alt={current.alt ?? ""}
        draggable={false}
        className={cx("size-full", fit === "contain" ? "object-contain" : "object-cover")}
      />
      {count > 1 && (
        <>
          <button type="button" aria-label="Previous photo" onClick={() => go(-1)} className={cx(arrowClass, "left-3")}>
            <IconChevronLeft size={20} />
          </button>
          <button type="button" aria-label="Next photo" onClick={() => go(1)} className={cx(arrowClass, "right-3")}>
            <IconChevronRight size={20} />
          </button>
        </>
      )}
      <span aria-hidden="true" className="absolute right-3 bottom-3 rounded-full bg-ink/70 px-2.5 py-1 text-label font-extrabold text-white">
        {index + 1} / {count}
      </span>
    </div>
  );
}
