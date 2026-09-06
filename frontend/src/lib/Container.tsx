import type { ReactNode } from "react";

type ContainerProps = {
  children: ReactNode;
  className?: string;
  /**
   * `prose` (the default) caps the page at a comfortable reading measure — right for the marketing
   * and detail pages, where a line of text spanning a wide monitor is tiring to read.
   *
   * `wide` is for pages whose content is a table or a dashboard. Those are read by comparing values
   * across a row, not by reading a line, so width helps rather than hurts — and the app shell has
   * already spent 15rem of the viewport on the sidebar before the page gets any. At 1920px the prose
   * cap leaves a table only 1200 of the 1680px available, stranding ~200px of empty gutter on each
   * side ON TOP of the padding. This keeps a ceiling so the columns do not sprawl on a 4K display,
   * but sets it above what the shell actually offers on a normal desktop.
   */
  size?: "prose" | "wide";
};

const maxWidth = { prose: "max-w-7xl", wide: "max-w-[1600px]" } as const;

export default function Container({ children, className, size = "prose" }: ContainerProps) {
  return (
    <div className={`mx-auto w-full ${maxWidth[size]} px-5 sm:px-8 lg:px-10 ${className ?? ""}`}>
      {children}
    </div>
  );
}
