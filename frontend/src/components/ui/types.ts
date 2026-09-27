import type { ReactNode } from "react";

/** One choice in a Dropdown, ChoiceChips or RadioGroup. */
export type Option = { value: string; label: ReactNode; disabled?: boolean };
