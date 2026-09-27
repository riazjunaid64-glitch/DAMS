import type { InputHTMLAttributes, TextareaHTMLAttributes } from "react";
import { TextArea, TextField } from "../components/ui";

type BaseProps = {
  label: string;
  hint?: string;
  /** Validation message shown in red below the field. When set, the field gets an error style. */
  error?: string;
};

type InputProps = BaseProps &
  Omit<InputHTMLAttributes<HTMLInputElement>, "size" | "prefix"> & {
    as?: "input";
  };

type TextareaProps = BaseProps &
  TextareaHTMLAttributes<HTMLTextAreaElement> & {
    as: "textarea";
  };

type FieldProps = InputProps | TextareaProps;

/** The field API existing screens use, drawn by the shared TextField / TextArea. `hint` shows as helper text. */
export default function Field(props: FieldProps) {
  if (props.as === "textarea") {
    const { hint, as: _as, ...rest } = props;
    void _as;
    return <TextArea helper={hint} {...rest} />;
  }
  const { hint, as: _as, ...rest } = props;
  void _as;
  return <TextField helper={hint} {...rest} />;
}
