import type { CSSProperties } from "react";
import { RECEIPT_CONFIG } from "../config/receiptConfig.ts";

/** Swap the failed <img> for its text fallback sibling. */
function showFallback(e: React.SyntheticEvent<HTMLImageElement>) {
  e.currentTarget.style.display = "none";
  const fb = e.currentTarget.nextElementSibling as HTMLElement | null;
  if (fb) fb.style.display = "flex";
}

/** Floria Heights wordmark logo, with a text fallback if the image can't load. */
export function CompanyLogo({ height = 56, fallbackStyle }: { height?: number; fallbackStyle?: CSSProperties }) {
  return (
    <>
      {RECEIPT_CONFIG.companyLogoSrc ? (
        <img
          src={RECEIPT_CONFIG.companyLogoSrc}
          alt={`${RECEIPT_CONFIG.companyName} ${RECEIPT_CONFIG.companyNameAccent}`}
          style={{ height: `${height}px`, width: "auto", display: "block" }}
          onError={showFallback}
        />
      ) : null}
      <div
        style={{
          display: RECEIPT_CONFIG.companyLogoSrc ? "none" : "block",
          fontSize: "30px",
          fontWeight: 800,
          letterSpacing: "1px",
          lineHeight: 1,
          ...fallbackStyle,
        }}
      >
        {RECEIPT_CONFIG.companyName}{" "}
        <span style={{ fontWeight: 400, letterSpacing: "4px" }}>{RECEIPT_CONFIG.companyNameAccent}</span>
      </div>
    </>
  );
}

/** Seven Ventures logo, with a text fallback if the image can't load. */
export function DeveloperLogo({ height = 40, fallbackStyle }: { height?: number; fallbackStyle?: CSSProperties }) {
  return (
    <>
      {RECEIPT_CONFIG.developerLogoSrc ? (
        <img
          src={RECEIPT_CONFIG.developerLogoSrc}
          alt={RECEIPT_CONFIG.developerName}
          style={{ height: `${height}px`, width: "auto", display: "block" }}
          onError={showFallback}
        />
      ) : null}
      <div
        style={{
          display: RECEIPT_CONFIG.developerLogoSrc ? "none" : "block",
          fontSize: "16px",
          fontWeight: 700,
          ...fallbackStyle,
        }}
      >
        {RECEIPT_CONFIG.developerName}
      </div>
    </>
  );
}

/**
 * Standard two-column receipt header: company logo on the left,
 * "A Project By" + developer logo on the right.
 */
export function ReceiptBrandHeader() {
  return (
    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", paddingBottom: "10px" }}>
      <div>
        <CompanyLogo height={56} />
      </div>
      <div style={{ display: "flex", flexDirection: "column", alignItems: "flex-end", gap: "2px" }}>
        <div style={{ fontSize: "10px", color: "#6b7280", letterSpacing: "1px" }}>{RECEIPT_CONFIG.projectByLabel}</div>
        <DeveloperLogo height={40} />
      </div>
    </div>
  );
}
