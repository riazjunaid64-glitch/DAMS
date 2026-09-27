import type { ReactNode } from "react";
import { cx } from "./cx.ts";
import { labelClass } from "./styles.ts";

export type FieldBaseProps = {
  label?: ReactNode;
  required?: boolean;
  /** Small grey line under the field. Hidden while an error is shown. */
  helper?: ReactNode;
  /** Red message under the field; also turns the field's border red. */
  error?: ReactNode;
};

type FieldShellProps = FieldBaseProps & {
  /** Id of the control, so the label is tied to it. */
  htmlFor?: string;
  /** Id for the helper / error line, referenced by the control's aria-describedby. */
  messageId?: string;
  /** Group controls (radios, chips) get a legend instead of a label. */
  as?: "div" | "fieldset";
  className?: string;
  children: ReactNode;
};

/** Label above, control, then helper or error below — the frame every form field shares. */
export function FieldShell({ label, required, helper, error, htmlFor, messageId, as = "div", className, children }: FieldShellProps) {
  const Root = as;
  const labelContent = label && (
    <>
      {label}
      {required && <span aria-hidden="true"> *</span>}
    </>
  );
  return (
    <Root className={cx("flex min-w-0 flex-col gap-2 border-0 p-0 font-ui", className)}>
      {labelContent && (as === "fieldset"
        ? <legend className={cx(labelClass, "mb-2 p-0")}>{labelContent}</legend>
        : <label htmlFor={htmlFor} className={labelClass}>{labelContent}</label>)}
      {children}
      {error ? (
        <p id={messageId} role="alert" className="text-small font-bold text-danger">{error}</p>
      ) : helper ? (
        <p id={messageId} className="text-small text-ink-muted">{helper}</p>
      ) : null}
    </Root>
  );
}
