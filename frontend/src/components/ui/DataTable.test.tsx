// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DataTable } from "./DataTable.tsx";

type Row = { id: string; kind?: "group" | "subtotal" | "total"; name: string; amount: string };

const rows: Row[] = [
  { id: "g", kind: "group", name: "Cash and bank", amount: "" },
  { id: "1", name: "Petty cash", amount: "Rs 12,000" },
  { id: "s", kind: "subtotal", name: "Subtotal", amount: "Rs 12,000" },
  { id: "t", kind: "total", name: "Total", amount: "Rs 12,000" },
];

const columns = [
  { key: "name", header: "Name", render: (row: Row) => row.name },
  { key: "amount", header: "Amount", align: "right" as const, render: (row: Row) => row.amount },
];

afterEach(cleanup);

describe("DataTable", () => {
  it("renders a group header, a subtotal and a total, and does not open the header", () => {
    const onRowClick = vi.fn();
    render(
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => row.id}
        onRowClick={onRowClick}
        phoneCard={(row) => <p>{row.name}</p>}
      />,
    );
    expect(screen.getAllByText("Cash and bank").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Subtotal").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Total").length).toBeGreaterThan(0);
    fireEvent.click(screen.getAllByText("Cash and bank")[0]!);
    expect(onRowClick).not.toHaveBeenCalled();
    fireEvent.click(screen.getAllByText("Petty cash")[0]!);
    expect(onRowClick).toHaveBeenCalledWith(rows[1]);
    const total = screen.getAllByText("Total")[0]!.closest("tr");
    expect(total?.className).toContain("font-extrabold");
  });

  it("shows placeholder rows while loading and keeps the old rows on a refresh", () => {
    const { rerender } = render(<DataTable columns={columns} rows={[]} rowKey={(row) => row.id} loading />);
    expect(document.querySelectorAll("tbody tr")).toHaveLength(8);
    rerender(<DataTable columns={columns} rows={rows} rowKey={(row) => row.id} loading />);
    expect(screen.getAllByText("Petty cash").length).toBeGreaterThan(0);
    expect(screen.getByRole("progressbar", { name: "Loading" })).toBeTruthy();
  });
  it("caps the height inside the card and keeps the heading on top when asked", () => {
    const { rerender } = render(<DataTable columns={columns} rows={rows} rowKey={(row) => row.id} maxHeight="46vh" />);
    const scroller = document.querySelector("table")!.parentElement!;
    expect(scroller.style.maxHeight).toBe("46vh");
    expect(document.querySelector("thead")!.className).toContain("sticky");
    rerender(<DataTable columns={columns} rows={rows} rowKey={(row) => row.id} />);
    expect(document.querySelector("table")!.parentElement!.style.maxHeight).toBe("");
  });

  it("uses tighter cell padding when asked", () => {
    const { rerender } = render(<DataTable columns={columns} rows={rows} rowKey={(row) => row.id} />);
    expect(document.querySelector("tbody td")!.className).toContain("px-4");
    expect(document.querySelector("thead th")!.className).toContain("px-4");
    rerender(<DataTable columns={columns} rows={rows} rowKey={(row) => row.id} dense />);
    expect(document.querySelector("tbody td")!.className).toContain("px-3");
    expect(document.querySelector("tbody td")!.className).not.toContain("px-4");
    expect(document.querySelector("thead th")!.className).toContain("px-3");
  });
});
