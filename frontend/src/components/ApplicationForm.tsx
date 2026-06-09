import type { CSSProperties } from "react";
import { RECEIPT_CONFIG } from "../config/receiptConfig.ts";

/** All values that can appear on the printed Application Form. Every field is optional
 *  so the same component renders a blank form (for hand-filling) or a filled one. */
export interface ApplicationFormData {
  serialNo?: string | null;
  date?: string | null;
  apartmentCategory?: string | null;
  isCorner?: boolean;
  apartmentNumber?: string | null;
  floor?: string | number | null;
  size?: string | number | null;
  tower?: string | null;

  fullName?: string | null;
  guardianName?: string | null; // S/o, W/o, D/o
  cnic?: string | null;
  dob?: string | null;
  nationality?: string | null;
  occupation?: string | null;
  mailingAddress?: string | null;
  contact?: string | null;
  email?: string | null;
  whatsapp?: string | null;

  kinName?: string | null;
  kinRelation?: string | null;
  kinContact?: string | null;
  kinCnic?: string | null;
  kinDob?: string | null;
  kinAddress?: string | null;

  pricePerSft?: string | number | null;
  discountPercent?: string | number | null;
  totalDownPayment?: string | number | null;
  remainingDownPayment?: string | number | null;
  referenceId?: string | null;
  amountReceived?: string | number | null;
  paymentType?: "Booking" | "Confirmation" | "LumSum" | string | null;
  through?: string | null;
  officeDate?: string | null;
}

const MAROON = "#5e2b3a";
const BORDER = "1px solid #b4b4b4";
const RADIUS = "7px";

const labelStyle: CSSProperties = {
  fontSize: "11.5px",
  color: "#3f3f46",
  fontWeight: 600,
  whiteSpace: "nowrap",
};

const valueStyle: CSSProperties = {
  fontSize: "12.5px",
  color: "#111827",
  fontWeight: 600,
  overflow: "hidden",
  textOverflow: "ellipsis",
  whiteSpace: "nowrap",
};

function fmt(v: string | number | null | undefined): string {
  if (v == null || v === "") return "";
  if (typeof v === "number") {
    return v.toLocaleString("en-PK", { minimumFractionDigits: 0, maximumFractionDigits: 2 });
  }
  return v;
}

function Cell({
  label,
  value,
  flex = 1,
  tall = false,
}: {
  label: string;
  value?: string | number | null;
  flex?: number;
  tall?: boolean;
}) {
  return (
    <div
      style={{
        flex,
        minWidth: 0,
        border: BORDER,
        borderRadius: RADIUS,
        padding: tall ? "6px 10px" : "0 10px",
        height: tall ? "48px" : "31px",
        display: "flex",
        alignItems: tall ? "flex-start" : "center",
        gap: "6px",
      }}
    >
      <span style={labelStyle}>{label}</span>
      <span style={{ ...valueStyle, whiteSpace: tall ? "normal" : "nowrap" }}>{fmt(value)}</span>
    </div>
  );
}

function CheckSquare({ on }: { on?: boolean }) {
  return (
    <span
      style={{
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        width: "14px",
        height: "14px",
        border: "1.5px solid #333",
        borderRadius: "2px",
        fontSize: "11px",
        fontWeight: 700,
        lineHeight: 1,
        color: "#111",
      }}
    >
      {on ? "✓" : ""}
    </span>
  );
}

function CheckCell({ label, on, flex = 1 }: { label: string; on?: boolean; flex?: number }) {
  return (
    <div
      style={{
        flex,
        border: BORDER,
        borderRadius: RADIUS,
        padding: "0 10px",
        height: "31px",
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        gap: "8px",
      }}
    >
      <span style={labelStyle}>{label}</span>
      <CheckSquare on={on} />
    </div>
  );
}

function SectionTitle({ children }: { children: string }) {
  return (
    <div style={{ margin: "16px 0 10px" }}>
      <div style={{ borderTop: `1px solid ${MAROON}` }} />
      <div
        style={{
          textAlign: "center",
          color: MAROON,
          fontWeight: 700,
          fontSize: "16px",
          letterSpacing: "0.5px",
          marginTop: "8px",
        }}
      >
        {children}
      </div>
    </div>
  );
}

function Row({ children, gap = 14 }: { children: React.ReactNode; gap?: number }) {
  return <div style={{ display: "flex", gap: `${gap}px`, marginBottom: "10px" }}>{children}</div>;
}

/** Floria Heights skyline mark (vector recreation of the logo). */
function FloriaMark() {
  return (
    <svg width="46" height="48" viewBox="0 0 46 48" fill="none" aria-hidden>
      <rect x="2" y="14" width="9" height="32" fill="#2b2b2b" />
      <rect x="13" y="6" width="11" height="40" fill="#1f1f1f" />
      <rect x="26" y="18" width="9" height="28" fill="#2b2b2b" />
      {/* window lines */}
      {[20, 26, 32, 38].map((y) => (
        <line key={`a${y}`} x1="14.5" y1={y} x2="22.5" y2={y} stroke="#fff" strokeWidth="1" />
      ))}
      {[24, 30, 36, 42].map((y) => (
        <line key={`b${y}`} x1="3.5" y1={y} x2="9.5" y2={y} stroke="#fff" strokeWidth="0.8" />
      ))}
      {[26, 32, 38].map((y) => (
        <line key={`c${y}`} x1="27.5" y1={y} x2="33.5" y2={y} stroke="#fff" strokeWidth="0.8" />
      ))}
    </svg>
  );
}

/** Seven Ventures "7V" monogram (vector recreation). */
function SevenVenturesMark() {
  return (
    <svg width="38" height="34" viewBox="0 0 38 34" fill="none" aria-hidden>
      <path d="M5 4 H33 L31 9 H10 L22 33" stroke="#2b2b2b" strokeWidth="2.4" fill="none" strokeLinejoin="round" />
      <path d="M14 12 L22 28 L30 12" stroke={MAROON} strokeWidth="2.2" fill="none" strokeLinejoin="round" />
    </svg>
  );
}

export default function ApplicationForm({ data = {} }: { data?: ApplicationFormData }) {
  return (
    <div
      className="receipt-print"
      style={{
        background: "#ffffff",
        color: "#111827",
        width: "210mm",
        maxWidth: "100%",
        margin: "0 auto",
        border: "1px solid #e5e7eb",
        boxSizing: "border-box",
        fontFamily: "Arial, Helvetica, sans-serif",
        display: "flex",
        flexDirection: "column",
      }}
    >
      <div style={{ padding: "12mm 12mm 8mm" }}>
        {/* ── Header ── */}
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "12px" }}>
          <div style={{ display: "flex", alignItems: "center", gap: "10px", flex: 1 }}>
            <FloriaMark />
            <div style={{ lineHeight: 1 }}>
              <div style={{ fontSize: "24px", fontWeight: 800, letterSpacing: "1px", color: "#1f2937" }}>
                {RECEIPT_CONFIG.companyName}
              </div>
              <div style={{ fontSize: "11px", fontWeight: 500, letterSpacing: "6px", color: "#6b7280", marginTop: "2px" }}>
                {RECEIPT_CONFIG.companyNameAccent}
              </div>
            </div>
          </div>

          <div style={{ flex: 1.5, textAlign: "center" }}>
            <div style={{ fontFamily: "Georgia, 'Times New Roman', serif", fontSize: "29px", color: MAROON, whiteSpace: "nowrap" }}>
              Application Form
            </div>
          </div>

          <div style={{ flex: 1, display: "flex", flexDirection: "column", alignItems: "flex-end" }}>
            <div style={{ fontSize: "9.5px", color: "#6b7280", letterSpacing: "1px" }}>
              {RECEIPT_CONFIG.projectByLabel}
            </div>
            <SevenVenturesMark />
            <div style={{ fontSize: "10px", color: "#3f3f46", letterSpacing: "1px", fontWeight: 600 }}>
              {RECEIPT_CONFIG.developerName}
            </div>
          </div>
        </div>

        <div style={{ borderTop: `1px solid ${MAROON}`, margin: "10px 0 16px" }} />

        {/* ── Applicant ── */}
        <Row>
          <Cell label="Serial No." value={data.serialNo} flex={2} />
          <Cell label="Date:" value={data.date} flex={1} />
        </Row>

        <Row>
          <Cell label="Apartment Category:" value={data.apartmentCategory} flex={3} />
          <CheckCell label="Corner" on={data.isCorner} flex={1} />
        </Row>

        <Row>
          <Cell label="Apartment Number:" value={data.apartmentNumber} flex={1.7} />
          <Cell label="Floor:" value={data.floor} flex={1} />
          <Cell label="Size:" value={data.size} flex={1} />
          <Cell label="Tower:" value={data.tower} flex={1.2} />
        </Row>

        <Row>
          <Cell label="Full Name:" value={data.fullName} />
        </Row>

        <Row>
          <Cell label="S/o, W/o, D/o:" value={data.guardianName} />
        </Row>

        <Row>
          <Cell label="CNIC/NICOP/PASSPORT#:" value={data.cnic} flex={2} />
          <Cell label="D.O.B:" value={data.dob} flex={1} />
        </Row>

        <Row>
          <Cell label="Nationality:" value={data.nationality} />
          <Cell label="Occupation:" value={data.occupation} />
        </Row>

        <Row>
          <Cell label="Mailing Address:" value={data.mailingAddress} tall />
        </Row>

        <Row>
          <Cell label="Contact#:" value={data.contact} />
          <Cell label="Email:" value={data.email} />
        </Row>

        <Row>
          <Cell label="Whatsapp:" value={data.whatsapp} flex={1} />
          <div style={{ flex: 1 }} />
        </Row>

        {/* ── Next of Kin ── */}
        <SectionTitle>NEXT OF KIN(S)</SectionTitle>

        <Row>
          <Cell label="Full Name:" value={data.kinName} flex={2} />
          <Cell label="Relation:" value={data.kinRelation} flex={1} />
        </Row>

        <Row>
          <Cell label="Nominee Contact#:" value={data.kinContact} />
          <Cell label="CNIC#:" value={data.kinCnic} />
        </Row>

        <Row>
          <Cell label="D.O.B:" value={data.kinDob} flex={1} />
          <Cell label="Mailing Address:" value={data.kinAddress} flex={2} tall />
        </Row>

        {/* ── Office Use ── */}
        <SectionTitle>OFFICE USE ONLY</SectionTitle>

        <Row>
          <Cell label="Price Per Sft:" value={data.pricePerSft} />
          <Cell label="Discount %:" value={data.discountPercent} />
        </Row>

        <Row>
          <Cell label="Total Down Payment:" value={data.totalDownPayment} />
          <Cell label="Remaining Down Payment:" value={data.remainingDownPayment} />
        </Row>

        <Row>
          <Cell label="Reference ID:" value={data.referenceId} />
        </Row>

        <Row>
          <Cell label="Amount Received:" value={data.amountReceived} flex={3} />
          <CheckCell label="Booking" on={data.paymentType === "Booking"} flex={1} />
          <CheckCell label="Confirmation" on={data.paymentType === "Confirmation"} flex={1} />
          <CheckCell label="LumSum" on={data.paymentType === "LumSum"} flex={1} />
        </Row>

        <Row>
          <Cell label="Through:" value={data.through} flex={2} />
          <Cell label="Date:" value={data.officeDate} flex={1} />
        </Row>

        {/* ── Signatures ── */}
        <div style={{ display: "flex", justifyContent: "space-between", marginTop: "34px", gap: "40px" }}>
          <div style={{ flex: 1, textAlign: "center" }}>
            <div style={{ borderTop: "1px solid #6b7280", paddingTop: "5px", fontSize: "11.5px", fontWeight: 600, color: "#3f3f46" }}>
              Signatures of Applicant/Nominee
            </div>
          </div>
          <div style={{ flex: 1, textAlign: "center" }}>
            <div style={{ borderTop: "1px solid #6b7280", paddingTop: "5px", fontSize: "11.5px", fontWeight: 600, color: "#3f3f46" }}>
              Official Signatures &amp; Stamp
            </div>
          </div>
        </div>

        <div style={{ textAlign: "center", color: MAROON, fontWeight: 700, fontSize: "12px", marginTop: "14px" }}>
          Terms and Conditions Applicable!
        </div>
      </div>

      {/* ── Footer (full-bleed maroon bar) ── */}
      <div
        style={{
          background: MAROON,
          color: "#ffffff",
          textAlign: "center",
          padding: "9px 12mm",
          fontSize: "12px",
          fontWeight: 700,
          lineHeight: 1.5,
        }}
      >
        <div>
          {RECEIPT_CONFIG.phones.join(" | ")} | {RECEIPT_CONFIG.email}
        </div>
        <div>
          {RECEIPT_CONFIG.website} | {RECEIPT_CONFIG.address}
        </div>
      </div>
    </div>
  );
}
