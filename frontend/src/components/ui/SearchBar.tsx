import { useEffect, useEffectEvent, useState } from "react";
import { cx } from "./cx.ts";
import { IconClose, IconSearch } from "./icons.tsx";

export type SearchBarProps = {
  /** The applied search. The box follows it when it changes from outside (e.g. Reset). */
  value: string;
  /** Called ~300 ms after typing stops, and at once when cleared. */
  onSearch: (value: string) => void;
  placeholder?: string;
  /** 40px in a desktop filter bar, 44px on phone. */
  size?: "md" | "lg";
  debounceMs?: number;
  "aria-label"?: string;
  className?: string;
};

export function SearchBar({ value, onSearch, placeholder = "Search…", size = "md", debounceMs = 300, "aria-label": ariaLabel, className }: SearchBarProps) {
  const [text, setText] = useState(value);
  const [seen, setSeen] = useState(value);
  const [sent, setSent] = useState(value);
  // Adopt a value changed from outside (e.g. Reset) during render, so the box clears with it. The
  // echo of our own search is skipped: the user may have typed more while it was applied.
  if (value !== seen) {
    setSeen(value);
    if (value !== sent) {
      setSent(value);
      setText(value);
    }
  }

  const search = (next: string) => {
    if (next === sent) return;
    setSent(next);
    onSearch(next);
  };
  const searchAfterPause = useEffectEvent(search);

  useEffect(() => {
    const timer = window.setTimeout(() => searchAfterPause(text.trim()), debounceMs);
    return () => window.clearTimeout(timer);
  }, [text, debounceMs]);

  return (
    <div
      role="search"
      className={cx(
        "flex min-w-0 items-center gap-2 rounded-field border border-line-input bg-card px-3 font-ui transition-[border-color,box-shadow]",
        "focus-within:border-primary focus-within:ring-3 focus-within:ring-primary-ring",
        size === "lg" ? "h-11" : "h-10",
        className,
      )}
    >
      <IconSearch size={16} className="shrink-0 text-ink-2" />
      <input
        type="search"
        value={text}
        onChange={(event) => setText(event.target.value)}
        onKeyDown={(event) => { if (event.key === "Enter") search(text.trim()); }}
        placeholder={placeholder}
        aria-label={ariaLabel ?? placeholder}
        className="h-full min-w-0 flex-1 border-0 bg-transparent p-0 text-sm font-bold text-ink outline-none placeholder:font-bold placeholder:text-ink-faint [&::-webkit-search-cancel-button]:hidden"
      />
      {text && (
        <button
          type="button"
          aria-label="Clear search"
          onClick={() => { setText(""); search(""); }}
          className="-mr-2.5 flex size-11 shrink-0 md:-mr-1.5 md:size-8 cursor-pointer items-center justify-center rounded-lg text-ink-faint hover:bg-page hover:text-ink focus-visible:outline-2 focus-visible:outline-primary"
        >
          <IconClose size={16} />
        </button>
      )}
    </div>
  );
}
