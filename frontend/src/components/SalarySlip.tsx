import { useState } from "react";
import { RECEIPT_CONFIG } from "../config/receiptConfig.ts";
import { amountInWords } from "../utils/amountInWords.ts";
import { ReceiptBrandHeader } from "./BrandLogos.tsx";

export interface SalarySlipData {
  employeeName: string;
  jobTitle: string;
  department: string;
  phone: string;
  email?: string | null;
  joinDate: string;
  projectName?: string | null;
  amount: number;
  payDate: string;
  notes?: string | null;
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

function EditableField({
  label,
  value,
  editing,
  onStartEdit,
  onSave,
  type = "text",
}: {
  label: string;
  value: string;
  editing: boolean;
  onStartEdit: () => void;
  onSave: (v: string) => void;
  type?: "text" | "date" | "number";
}) {
  const [draft, setDraft] = useState(value);

  const commit = () => {
    onSave(draft);
  };

  return (
    <div style={{ display: "flex", alignItems: "flex-end", gap: "6px", flex: 1, minWidth: 0 }}>
      <span style={{ fontWeight: 600, whiteSpace: "nowrap", fontSize: "12.5px" }}>{label}</span>
      {editing ? (
        <input
          type={type}
          value={draft}
          onChange={e => setDraft(e.target.value)}
          onBlur={commit}
          onKeyDown={e => { if (e.key === "Enter") commit(); }}
          autoFocus
          style={{
            flex: 1,
            border: "none",
            borderBottom: "2px solid #390217",
            background: "transparent",
            fontSize: "13px",
            color: "#111827",
            outline: "none",
            padding: "0 4px 1px",
          }}
        />
      ) : (
        <span
          style={{
            flex: 1,
            borderBottom: "1px solid #9ca3af",
            minHeight: "18px",
            padding: "0 4px 1px",
            fontSize: "13px",
            color: "#111827",
            display: "flex",
            alignItems: "center",
            gap: "6px",
          }}
        >
          <span style={{ flex: 1 }}>
            {type === "number"
              ? formatMoney(Number(value)) + " /-"
              : type === "date"
                ? formatDate(value)
                : value}
          </span>
          <button
            type="button"
            onClick={() => { setDraft(value); onStartEdit(); }}
            title="Edit"
            style={{
              background: "none",
              border: "none",
              cursor: "pointer",
              padding: "0 2px",
              color: "#390217",
              fontSize: "14px",
              lineHeight: 1,
            }}
          >
            ✎
          </button>
        </span>
      )}
    </div>
  );
}

interface Props {
  data: SalarySlipData;
  editable?: boolean;
  onAmountChange?: (amount: number) => void;
  onDateChange?: (date: string) => void;
}

export default function SalarySlip({ data, editable, onAmountChange, onDateChange }: Props) {
  const [editingAmount, setEditingAmount] = useState(false);
  const [editingDate, setEditingDate] = useState(false);
  const [amountStr, setAmountStr] = useState(String(data.amount));
  const [dateStr, setDateStr] = useState(data.payDate.slice(0, 10));

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
      <ReceiptBrandHeader />

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
        SALARY SLIP
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
        <div style={{ display: "flex", gap: "30px" }}>
          {editable ? (
            <EditableField
              label="Pay Date:"
              value={dateStr}
              editing={editingDate}
              onStartEdit={() => setEditingDate(true)}
              onSave={v => {
                setDateStr(v);
                setEditingDate(false);
                onDateChange?.(v);
              }}
              type="date"
            />
          ) : (
            <FillField label="Pay Date:" value={formatDate(data.payDate)} />
          )}
          <FillField label="Department:" value={data.department} />
        </div>

        <FillField label="Employee Name:" value={data.employeeName} />
        <FillField label="Designation:" value={data.jobTitle} />
        <FillField label="Phone:" value={data.phone} />
        {data.email && <FillField label="Email:" value={data.email} />}
        <FillField label="Join Date:" value={formatDate(data.joinDate)} />
        {data.projectName && <FillField label="Project:" value={data.projectName} />}

        <div style={{ display: "flex", gap: "30px", alignItems: "flex-end" }}>
          {editable ? (
            <EditableField
              label="Salary Amount:"
              value={amountStr}
              editing={editingAmount}
              onStartEdit={() => setEditingAmount(true)}
              onSave={v => {
                setAmountStr(v);
                setEditingAmount(false);
                const n = Number(v);
                if (Number.isFinite(n)) onAmountChange?.(n);
              }}
              type="number"
            />
          ) : (
            <FillField label="Salary Amount:" value={formatMoney(data.amount) + " /-"} flex={2} />
          )}
        </div>

        <FillField label="Amount in Words:" value={amountInWords(data.amount, RECEIPT_CONFIG.currencyWord)} />

        {data.notes && <FillField label="Notes:" value={data.notes} />}
      </div>

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

      <div style={{ display: "flex", justifyContent: "space-between", marginTop: "36px" }}>
        <div style={{ textAlign: "center", minWidth: "200px" }}>
          <div style={{ borderTop: "1px solid #9ca3af", paddingTop: "4px", fontSize: "12px", fontWeight: 600 }}>{"\u00A0"}</div>
          <div style={{ fontSize: "11px", color: "#6b7280" }}>Employee Signature</div>
        </div>
        <div style={{ textAlign: "center", minWidth: "200px" }}>
          <div style={{ borderTop: "1px solid #9ca3af", paddingTop: "4px", fontSize: "12px", fontWeight: 600 }}>{"\u00A0"}</div>
          <div style={{ fontSize: "11px", color: "#6b7280" }}>Authorized By</div>
        </div>
      </div>

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
