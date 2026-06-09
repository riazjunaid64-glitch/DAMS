// Converts a number to words using the Pakistani / South-Asian numbering system
// (Thousand, Lac, Crore, Arab) as used on local developer receipts.
// Example: 1,920,000 -> "Nineteen Lac Twenty Thousand Only".

const ONES = [
  "",
  "One",
  "Two",
  "Three",
  "Four",
  "Five",
  "Six",
  "Seven",
  "Eight",
  "Nine",
  "Ten",
  "Eleven",
  "Twelve",
  "Thirteen",
  "Fourteen",
  "Fifteen",
  "Sixteen",
  "Seventeen",
  "Eighteen",
  "Nineteen",
];

const TENS = [
  "",
  "",
  "Twenty",
  "Thirty",
  "Forty",
  "Fifty",
  "Sixty",
  "Seventy",
  "Eighty",
  "Ninety",
];

function twoOrThreeDigitsToWords(num: number): string {
  if (num === 0) return "";
  if (num < 20) return ONES[num];
  if (num < 100) {
    const tens = Math.floor(num / 10);
    const ones = num % 10;
    return TENS[tens] + (ones ? " " + ONES[ones] : "");
  }
  const hundreds = Math.floor(num / 100);
  const rest = num % 100;
  return ONES[hundreds] + " Hundred" + (rest ? " " + twoOrThreeDigitsToWords(rest) : "");
}

export function amountInWords(amount: number, currency = "Rupees"): string {
  if (amount == null || Number.isNaN(amount)) return "";

  const negative = amount < 0;
  const abs = Math.abs(amount);
  const rupees = Math.floor(abs);
  const paisa = Math.round((abs - rupees) * 100);

  let words = "";

  if (rupees === 0) {
    words = "Zero";
  } else {
    const crore = Math.floor(rupees / 10000000);
    const lac = Math.floor((rupees % 10000000) / 100000);
    const thousand = Math.floor((rupees % 100000) / 1000);
    const hundredsBlock = rupees % 1000;

    const parts: string[] = [];
    if (crore) parts.push(twoOrThreeDigitsToWords(crore) + " Crore");
    if (lac) parts.push(twoOrThreeDigitsToWords(lac) + " Lac");
    if (thousand) parts.push(twoOrThreeDigitsToWords(thousand) + " Thousand");
    if (hundredsBlock) parts.push(twoOrThreeDigitsToWords(hundredsBlock));

    words = parts.join(" ").trim();
  }

  let result = `${words}`;
  if (paisa > 0) {
    result += ` and ${twoOrThreeDigitsToWords(paisa)} Paisa`;
  }
  result += " Only";

  const prefix = currency ? `${currency} ` : "";
  return (negative ? "Minus " : "") + prefix + result;
}
