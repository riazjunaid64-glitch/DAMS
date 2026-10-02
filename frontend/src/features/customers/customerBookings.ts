import { formatPkr } from "../../utils/currency.ts";

/** One row of the customer's Bookings tab, as `GET /api/Booking/customer/{id}` sends it. */
export interface CustomerBooking {
  id: number;
  bookingReference: string;
  status: string;
  unitNumber: string;
  projectName: string;
  bookingDate: string;
  netPrice: number;
  /** Money received — Collected on the booking page. */
  collected: number;
  /** What is left to pay — Outstanding on the booking page. */
  outstanding: number;
}

/** "—" for a cancelled booking, because nothing more is owed on it. */
export const stillDue = (booking: Pick<CustomerBooking, "status" | "outstanding">): string =>
  booking.status === "Cancelled" ? "—" : formatPkr(booking.outstanding);
