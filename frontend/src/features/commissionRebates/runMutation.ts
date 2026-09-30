import type { BookingWorkspace } from "./types.ts";

/**
 * How a popup hands its request to the panel: the panel runs it, keeps the workspace the server
 * answers with (or re-reads it when the booking moved meanwhile) and tells the booking page when the
 * change reaches the customer's figures. Throws with the server's message when it is refused, so the
 * popup can show it and stay open.
 */
export type RunMutation = (operation: () => Promise<BookingWorkspace>, affectsBooking?: boolean) => Promise<BookingWorkspace>;
