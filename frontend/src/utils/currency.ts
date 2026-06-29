// App-wide currency formatting. The company operates in Pakistani Rupees (PKR),
// matching the printed salary slip (see SalarySlip.tsx).

/** Format an amount as "Rs 50,000" (no decimals unless present). */
export function formatPkr(n: number): string {
  const value = Number.isFinite(n) ? n : 0;
  return `Rs ${value.toLocaleString("en-PK", { minimumFractionDigits: 0, maximumFractionDigits: 2 })}`;
}
