import type { ReactNode } from "react";

/** "Already used by Usman Tariq. Open customer" under the clashing field. */
export function duplicateFieldError(name: string, onOpen: () => void): ReactNode {
  return (
    <>
      Already used by {name}.{" "}
      <button type="button" className="font-extrabold underline" onClick={onOpen}>
        Open customer
      </button>
    </>
  );
}
