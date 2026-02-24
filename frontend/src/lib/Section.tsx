import type { ReactNode } from "react";

type SectionProps = {
  id?: string;
  eyebrow?: string;
  title?: string;
  description?: string;
  children: ReactNode;
  className?: string;
};

export default function Section({
  id,
  eyebrow,
  title,
  description,
  children,
  className,
}: SectionProps) {
  return (
    <section id={id} className={className ?? ""}>
      {(eyebrow || title || description) && (
        <div className="mb-10 space-y-3 text-center">
          {eyebrow && (
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-amber-400">
              {eyebrow}
            </p>
          )}
          {title && (
            <h2 className="text-3xl font-semibold text-slate-100 sm:text-4xl">
              {title}
            </h2>
          )}
          {description && (
            <p className="mx-auto max-w-2xl text-base text-slate-300 sm:text-lg">
              {description}
            </p>
          )}
        </div>
      )}
      {children}
    </section>
  );
}
