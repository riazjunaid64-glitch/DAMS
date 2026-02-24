import type { InputHTMLAttributes, TextareaHTMLAttributes } from "react";

type InputProps = InputHTMLAttributes<HTMLInputElement> & {
  label: string;
  as?: "input";
};

type TextareaProps = TextareaHTMLAttributes<HTMLTextAreaElement> & {
  label: string;
  as: "textarea";
};

type FieldProps = InputProps | TextareaProps;

export default function Field(props: FieldProps) {
  const { label, as = "input" } = props;
  const shared =
    "w-full rounded-2xl border border-slate-200/40 bg-white/5 px-4 py-3 text-sm text-white placeholder:text-slate-300/60 focus:border-amber-300 focus:outline-none focus:ring-2 focus:ring-amber-300/30";

  return (
    <label className="flex flex-col gap-2 text-sm font-medium text-slate-200">
      {label}
      {as === "textarea" ? (
        <textarea
          {...props}
          className={`${shared} min-h-[140px] resize-none ${props.className ?? ""}`}
        />
      ) : (
        <input {...props} className={`${shared} ${props.className ?? ""}`} />
      )}
    </label>
  );
}
