/** Joins class names, skipping the falsy ones a conditional leaves behind. */
export function cx(...classes: (string | false | null | undefined)[]): string {
  return classes.filter(Boolean).join(" ");
}
