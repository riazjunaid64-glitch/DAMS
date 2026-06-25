import type { ReactNode } from "react";

type SectionProps = {
  id?: string;
  eyebrow?: string;
  title?: string;
  description?: string;
  children: ReactNode;
  className?: string;
  align?: "center" | "left";
};

export default function Section({
  id,
  eyebrow,
  title,
  description,
  children,
  className,
  align = "center",
}: SectionProps) {
  const alignment = align === "center" ? "text-center" : "text-left";

  return (
    <section id={id} className={`relative ${className ?? ""}`}>
      {(eyebrow || title || description) && (
        <div className={`mb-12 space-y-4 ${alignment}`}>
          {eyebrow && (
            <div className="inline-flex items-center gap-2">
              <span className="h-px w-8 bg-gradient-to-r from-transparent to-[var(--accent)]" />
              <p className="text-xs font-semibold uppercase tracking-[0.2em] text-[var(--accent)]">
                {eyebrow}
              </p>
              <span className="h-px w-8 bg-gradient-to-l from-transparent to-[var(--accent)]" />
            </div>
          )}
          {title && (
            <h2 className="text-3xl font-bold text-[var(--text-heading)] sm:text-4xl lg:text-5xl">
              {title}
            </h2>
          )}
          {description && (
            <p className={`${align === "center" ? "mx-auto" : ""} max-w-2xl text-base text-[var(--text-secondary)] sm:text-lg leading-relaxed`}>
              {description}
            </p>
          )}
        </div>
      )}
      {children}
    </section>
  );
}
