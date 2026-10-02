// @vitest-environment happy-dom
import { useState } from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../../../components/ui/Toast.tsx";
import type { ExpenseCategory } from "../whtTypes.ts";
import { ExpenseDialog } from "./ExpenseDialog.tsx";
import { emptyExpenseForm } from "./format.ts";
import type { ExpenseFormState } from "./types.ts";

const requests: { url: string; body: FormData | null }[] = [];

vi.mock("../../../api/api.ts", () => ({
  api: async (url: string, init?: RequestInit) => {
    requests.push({ url, body: init?.body instanceof FormData ? init.body : null });
    return new Response(JSON.stringify({
      isWhtApplicable: false,
      rate: 0,
      whtAmount: 0,
      netPaid: 0,
      whtApplied: false,
      filerStatus: "Unknown",
      taxSection: null,
      belowThreshold: true,
      annualThreshold: 0,
      yearToDateTotal: 0,
      financialYear: "2026-27",
      notice: null,
    }), { status: 200, headers: { "Content-Type": "application/json" } });
  },
}));

const materials: ExpenseCategory = {
  id: 4,
  name: "Construction materials",
  code: "materials",
  description: null,
  isWhtApplicable: true,
  filerRate: 4,
  nonFilerRate: 8,
  annualThreshold: 0,
  taxSection: "153(1)(a)",
  displayOrder: 1,
  isActive: true,
  usageCount: 1,
  createdAt: "2026-01-01",
  updatedAt: null,
  concurrencyToken: "c",
};

const legacy: ExpenseFormState = {
  ...emptyExpenseForm(),
  id: 12,
  concurrencyToken: "row",
  financeAccountId: "3",
  amount: "850000",
  category: "Site tea",
  legacyCategory: true,
  date: "2026-09-26",
};

function Harness() {
  const [form, setForm] = useState(legacy);
  return (
    <ToastProvider>
      <ExpenseDialog
        form={form}
        projects={[]}
        accounts={[{ id: 3, name: "Meezan Bank", type: 1, accountHolderName: "Adeel Satti", isActive: true }]}
        categories={[materials]}
        vendors={[]}
        lookupsLoading={false}
        onChange={setForm}
        onClose={() => {}}
        onSaved={() => {}}
        onOpenAttachment={() => {}}
        onDownloadAttachment={() => {}}
      />
    </ToastProvider>
  );
}

afterEach(() => {
  cleanup();
  requests.length = 0;
});

describe("Edit expense", () => {
  it("keeps the original free-text category after a managed category is chosen and cleared", async () => {
    render(<Harness />);
    const keep = 'Keep the original text — "Site tea"';

    fireEvent.click(screen.getByRole("combobox", { name: /Category/ }));
    fireEvent.click(screen.getByRole("option", { name: /Construction materials/ }));

    fireEvent.click(screen.getByRole("combobox", { name: /Category/ }));
    expect(screen.getByRole("option", { name: keep })).toBeTruthy();
    fireEvent.click(screen.getByRole("option", { name: keep }));

    const original = screen.getByRole("textbox", { name: /Original category text/ }) as HTMLInputElement;
    expect(original.value).toBe("Site tea");

    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => {
      expect(requests.some((request) => request.url === "/api/Finance/expenses/12/form")).toBe(true);
    });
    const saved = requests.find((request) => request.url === "/api/Finance/expenses/12/form")?.body;
    expect(saved?.get("category")).toBe("Site tea");
    expect(saved?.has("categoryId")).toBe(false);
  });
});
