import type { ApplicationFormData } from "../components/ApplicationForm.tsx";

/** Format an ISO date as dd-Mon-yyyy (e.g. 07-Jun-2026). Empty string for missing dates. */
export function formatDate(iso: string | null | undefined): string {
  if (!iso) return "";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  const day = String(d.getDate()).padStart(2, "0");
  const month = d.toLocaleString("en-US", { month: "short" });
  return `${day}-${month}-${d.getFullYear()}`;
}

/** Shape returned by GET/POST /api/Booking (the fields used to fill the Application Form). */
export interface BookingForForm {
  bookingReference?: string;
  bookingDate?: string;
  customerName?: string;
  customerPhone?: string;
  customerFatherName?: string | null;
  customerCnic?: string | null;
  customerEmail?: string | null;
  customerAddress?: string | null;
  customerDateOfBirth?: string | null;
  customerNationality?: string | null;
  customerOccupation?: string | null;
  customerWhatsapp?: string | null;
  unitNumber?: string;
  unitType?: string;
  unitFloorNumber?: number;
  unitSize?: number;
  agreedSalePrice?: number;
  bookingAmountRequired?: number;
  bookingAmountReceived?: number;
  serialNo?: string | null;
  apartmentCategory?: string | null;
  tower?: string | null;
  isCorner?: boolean;
  pricePerSft?: number | null;
  discountPercent?: number | null;
  referenceId?: string | null;
  paymentThrough?: string | null;
  applicationPaymentType?: string | null;
  applicationAmountReceived?: number | null;
  applicationDate?: string | null;
  nextOfKinName?: string | null;
  nextOfKinRelation?: string | null;
  nextOfKinContact?: string | null;
  nextOfKinCnic?: string | null;
  nextOfKinDob?: string | null;
  nextOfKinAddress?: string | null;
}

/** Maps a booking record to the printable Application Form fields. */
export function bookingToApplicationForm(b: BookingForForm): ApplicationFormData {
  const totalDown = b.bookingAmountRequired ?? null;
  const received = b.applicationAmountReceived ?? b.bookingAmountReceived ?? null;
  const remaining =
    totalDown != null ? Math.max(0, totalDown - (received ?? 0)) : null;

  return {
    serialNo: b.serialNo || b.bookingReference || "",
    date: formatDate(b.bookingDate),
    apartmentCategory: b.apartmentCategory || b.unitType || "",
    isCorner: b.isCorner ?? false,
    apartmentNumber: b.unitNumber ?? "",
    floor: b.unitFloorNumber ?? "",
    size: b.unitSize ?? "",
    tower: b.tower ?? "",

    fullName: b.customerName ?? "",
    guardianName: b.customerFatherName ?? "",
    cnic: b.customerCnic ?? "",
    dob: formatDate(b.customerDateOfBirth),
    nationality: b.customerNationality ?? "",
    occupation: b.customerOccupation ?? "",
    mailingAddress: b.customerAddress ?? "",
    contact: b.customerPhone ?? "",
    email: b.customerEmail ?? "",
    whatsapp: b.customerWhatsapp ?? "",

    kinName: b.nextOfKinName ?? "",
    kinRelation: b.nextOfKinRelation ?? "",
    kinContact: b.nextOfKinContact ?? "",
    kinCnic: b.nextOfKinCnic ?? "",
    kinDob: formatDate(b.nextOfKinDob),
    kinAddress: b.nextOfKinAddress ?? "",

    pricePerSft: b.pricePerSft ?? "",
    discountPercent: b.discountPercent ?? "",
    totalDownPayment: totalDown ?? "",
    remainingDownPayment: remaining ?? "",
    referenceId: b.referenceId ?? "",
    amountReceived: received ?? "",
    paymentType: b.applicationPaymentType ?? null,
    through: b.paymentThrough ?? "",
    officeDate: formatDate(b.applicationDate || b.bookingDate),
  };
}
