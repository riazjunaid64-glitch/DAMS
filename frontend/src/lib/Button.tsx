import { Button as SharedButton, type ButtonProps } from "../components/ui";

/**
 * Kept so existing imports keep working; the button itself lives in the shared component library.
 * Existing screens were written against a plain <button>, whose type defaults to "submit", and some
 * of their forms submit through it — so that default is kept here. New code uses Button from
 * components/ui, which defaults to "button".
 */
export default function Button({ type = "submit", ...props }: ButtonProps) {
  return <SharedButton type={type} {...props} />;
}
