import type { InputHTMLAttributes, TextareaHTMLAttributes } from "react";

type InputProps = InputHTMLAttributes<HTMLInputElement> & {
  label: string;
  as?: "input";
  hint?: string;
};

type TextareaProps = TextareaHTMLAttributes<HTMLTextAreaElement> & {
  label: string;
  as: "textarea";
  hint?: string;
};

type FieldProps = InputProps | TextareaProps;

export default function Field(props: FieldProps) {
  const { label, as = "input", hint } = props;
  const shared =
    "w-full rounded-xl border border-white/[0.08] bg-white/[0.03] px-4 py-3 text-sm text-white placeholder:text-[#6b6b80] transition-all duration-200 focus:border-indigo-500/60 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:bg-white/[0.05] hover:border-white/[0.12]";

  return (
    <label className="flex flex-col gap-1.5 text-sm font-medium text-[#a1a1b5]">
      <span className="flex items-center justify-between">
        <span>{label}</span>
        {hint && <span className="text-xs text-[#6b6b80] font-normal">{hint}</span>}
      </span>
      {as === "textarea" ? (
        <textarea
          {...(props as TextareaProps)}
          className={`${shared} min-h-[120px] resize-none ${props.className ?? ""}`}
        />
      ) : (
        <input {...(props as InputProps)} className={`${shared} ${props.className ?? ""}`} />
      )}
    </label>
  );
}
