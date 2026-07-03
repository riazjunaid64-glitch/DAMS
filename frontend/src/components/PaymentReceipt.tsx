import { RECEIPT_CONFIG } from "../config/receiptConfig.ts";
import { amountInWords } from "../utils/amountInWords.ts";
import { ReceiptBrandHeader } from "./BrandLogos.tsx";

export interface PaymentReceiptData {
  paymentId: number;
  receiptNumber?: string | null;
  paidAt: string;
  receivedByName?: string | null;
  bookingReference: string;
  customerName: string;
  fatherName?: string | null;
  customerPhone?: string | null;
  customerCnic?: string | null;
  customerAddress?: string | null;
  projectName: string;
  unitType: string;
  unitNumber: string;
  block?: string | null;
  floorNumber: number;
  unitSize: number;
  type: string; // "BookingAmount" | "Installment"
  paymentMethod: string; // "Cash" | "BankTransfer" | "Cheque" | "Online"
  paymentReference?: string | null;
  amount: number;
  installmentSequence?: number | null;
  installmentType?: string | null; // "Regular" | "Possession"
}

function formatMoney(n: number) {
  return n.toLocaleString("en-PK", { minimumFractionDigits: 0, maximumFractionDigits: 2 });
}

function formatDate(iso: string) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  const day = String(d.getDate()).padStart(2, "0");
  const month = d.toLocaleString("en-US", { month: "short" });
  return `${day}-${month}-${d.getFullYear()}`;
}

/** Which of the three printed modes (Cash / Pay Order / Online Transfer) is ticked. */
function modeOfPayment(method: string): "Cash" | "Pay Order" | "Online Transfer" {
  switch (method) {
    case "Cash":
      return "Cash";
    case "Cheque":
      return "Pay Order";
    default:
      return "Online Transfer"; // BankTransfer | Online
  }
}

function paymentTowards(r: PaymentReceiptData): string {
  const unit = `${r.unitType}${r.unitNumber ? ` (${r.unitNumber})` : ""}`.trim();
  if (r.type === "BookingAmount") return `Down Payment of ${unit}`;
  if (r.installmentType === "Possession") return `Possession Payment of ${unit}`;
  if (r.installmentSequence != null) return `Installment #${r.installmentSequence} of ${unit}`;
  return `Installment Payment of ${unit}`;
}

function Check({ on }: { on: boolean }) {
  return (
    <span
      style={{
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        width: "16px",
        height: "16px",
        border: "1.5px solid #1f2937",
        borderRadius: "2px",
        marginRight: "6px",
        fontSize: "12px",
        fontWeight: 700,
        lineHeight: 1,
        color: "#111827",
      }}
    >
      {on ? "✓" : ""}
    </span>
  );
}

/** A labeled value rendered as an underlined fill-in field, like the printed form. */
function FillField({ label, value, flex }: { label: string; value?: string | number | null; flex?: number }) {
  return (
    <div style={{ display: "flex", alignItems: "flex-end", gap: "6px", flex: flex ?? 1, minWidth: 0 }}>
      <span style={{ fontWeight: 600, whiteSpace: "nowrap", fontSize: "12.5px" }}>{label}</span>
      <span
        style={{
          flex: 1,
          borderBottom: "1px solid #9ca3af",
          minHeight: "18px",
          padding: "0 4px 1px",
          fontSize: "13px",
          color: "#111827",
          whiteSpace: "nowrap",
          overflow: "hidden",
          textOverflow: "ellipsis",
        }}
      >
        {value ?? ""}
      </span>
    </div>
  );
}

export default function PaymentReceipt({ data }: { data: PaymentReceiptData }) {
  const mode = modeOfPayment(data.paymentMethod);

  return (
    <div
      className="receipt-print"
      style={{
        background: "#ffffff",
        color: "#111827",
        width: "210mm",
        maxWidth: "100%",
        margin: "0 auto",
        padding: "18mm 16mm",
        boxSizing: "border-box",
        fontFamily: "Arial, Helvetica, sans-serif",
        border: "1px solid #e5e7eb",
      }}
    >
      {/* Header */}
      <ReceiptBrandHeader />

      {/* Title */}
      <div
        style={{
          textAlign: "center",
          fontSize: "20px",
          fontWeight: 800,
          letterSpacing: "3px",
          border: "2px solid #111827",
          padding: "6px 0",
          margin: "6px 0 18px",
        }}
      >
        PAYMENT RECEIPT
      </div>

      {/* Body */}
      <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
        <div style={{ display: "flex", gap: "30px" }}>
          <FillField label="Receipt No." value={data.receiptNumber} />
          <FillField label="Date:" value={formatDate(data.paidAt)} />
        </div>

        <div style={{ display: "flex", gap: "30px" }}>
          <FillField label="Apartment Type:" value={data.unitType} flex={2} />
          <div style={{ display: "flex", alignItems: "center", border: "1px solid #9ca3af", padding: "3px 10px", fontSize: "12.5px", fontWeight: 600 }}>
            <Check on={false} /> Corner
          </div>
        </div>

        <div style={{ display: "flex", gap: "30px" }}>
          <FillField label="Apartment Number:" value={data.unitNumber} />
          <FillField label="Floor:" value={data.floorNumber} />
          <FillField label="Size:" value={data.unitSize ? formatMoney(data.unitSize) : ""} />
          <FillField label="Block:" value={data.block} />
        </div>

        <FillField label="Received From Mr/Ms/Mrs:" value={data.customerName} />
        <FillField label="S/o, W/o:" value={data.fatherName} />

        <div style={{ display: "flex", gap: "30px", alignItems: "flex-end" }}>
          <FillField label="Amount Received:" value={data.amount != null ? formatMoney(data.amount) + " /-" : ""} flex={2} />
          <div style={{ display: "flex", alignItems: "center", fontSize: "12.5px", fontWeight: 600 }}>
            <Check on={data.type === "BookingAmount"} /> Down Payment
          </div>
          <div style={{ display: "flex", alignItems: "center", fontSize: "12.5px", fontWeight: 600 }}>
            <Check on={data.type === "Installment"} /> Installment
          </div>
        </div>

        <FillField label="Amount in Words:" value={amountInWords(data.amount, RECEIPT_CONFIG.currencyWord)} />

        <div style={{ display: "flex", gap: "20px", alignItems: "center" }}>
          <span style={{ fontWeight: 600, fontSize: "12.5px" }}>Mode of Payment:</span>
          <div style={{ display: "flex", alignItems: "center", fontSize: "12.5px", fontWeight: 600 }}>
            <Check on={mode === "Cash"} /> Cash
          </div>
          <div style={{ display: "flex", alignItems: "center", fontSize: "12.5px", fontWeight: 600 }}>
            <Check on={mode === "Pay Order"} /> Pay Order
          </div>
          <div style={{ display: "flex", alignItems: "center", fontSize: "12.5px", fontWeight: 600 }}>
            <Check on={mode === "Online Transfer"} /> Online Transfer
          </div>
        </div>

        <div style={{ display: "flex", gap: "30px" }}>
          <FillField label="Payment Reference No.:" value={data.paymentReference} flex={2} />
          <FillField label="Date:" value={formatDate(data.paidAt)} />
        </div>
      </div>

      {/* Office use only band */}
      <div
        style={{
          textAlign: "center",
          fontSize: "11px",
          fontWeight: 700,
          letterSpacing: "2px",
          background: "#f3f4f6",
          border: "1px solid #d1d5db",
          padding: "4px 0",
          margin: "18px 0 14px",
          color: "#6b7280",
        }}
      >
        OFFICE USE ONLY
      </div>

      <FillField label="Payment Towards:" value={paymentTowards(data)} />

      {/* Prepared / Received by */}
      <div style={{ display: "flex", justifyContent: "space-between", marginTop: "36px" }}>
        <div style={{ textAlign: "center", minWidth: "200px" }}>
          <div style={{ borderTop: "1px solid #9ca3af", paddingTop: "4px", fontSize: "12px", fontWeight: 600 }}>
            {data.receivedByName || "\u00A0"}
          </div>
          <div style={{ fontSize: "11px", color: "#6b7280" }}>Received By</div>
        </div>
        <div style={{ textAlign: "center", minWidth: "200px" }}>
          <div style={{ borderTop: "1px solid #9ca3af", paddingTop: "4px", fontSize: "12px", fontWeight: 600 }}>
            {data.receivedByName || "\u00A0"}
          </div>
          <div style={{ fontSize: "11px", color: "#6b7280" }}>Prepared By</div>
        </div>
      </div>

      {/* Footer */}
      <div style={{ marginTop: "26px", borderTop: "2px solid #111827", paddingTop: "8px", textAlign: "center", fontSize: "11px", color: "#374151" }}>
        <div style={{ fontWeight: 600 }}>
          {RECEIPT_CONFIG.phones.join(" | ")} | {RECEIPT_CONFIG.email}
        </div>
        <div>
          {RECEIPT_CONFIG.website} | {RECEIPT_CONFIG.address}
        </div>
      </div>
    </div>
  );
}
