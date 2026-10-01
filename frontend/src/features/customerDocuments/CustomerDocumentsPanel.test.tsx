// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../../components/ui/Toast.tsx";
import CustomerDocumentsPanel from "./CustomerDocumentsPanel.tsx";
import type { DocumentChecklist, DocumentRequirement } from "./types.ts";

type Call = { url: string; method: string; body: unknown };
type Pending = { url: string; form: FormData; onProgress: (n: number) => void; signal: AbortSignal; resolve: (r: Response) => void; reject: (e: unknown) => void };
let calls: Call[];
let pending: Pending[];
let answers: Record<string, () => { status?: number; body: unknown; blob?: Blob }>;

vi.mock("../../api/api.ts", () => ({
  api: async (url: string, init?: RequestInit) => {
    const method = init?.method ?? "GET";
    calls.push({ url, method, body: typeof init?.body === "string" ? JSON.parse(init.body) : undefined });
    const answer = answers[`${method} ${url}`];
    if (!answer) return new Response(JSON.stringify({ message: "unexpected" }), { status: 404 });
    const { status = 200, body, blob } = answer();
    return blob ? new Response(blob, { status }) : new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
  },
  apiUpload: (url: string, form: FormData, onProgress: (n: number) => void, signal: AbortSignal) =>
    new Promise<Response>((resolve, reject) => {
      pending.push({ url, form, onProgress, signal, resolve, reject });
      signal.addEventListener("abort", () => reject(new DOMException("stopped", "AbortError")));
    }),
}));

const jpg = { id: 11, versionNumber: 2, isCurrent: true, originalFileName: "cnic-front.jpg", contentType: "image/jpeg", fileSize: 240 * 1024, uploadedByName: "admin", uploadedAt: "2026-04-20T07:00:00" };
const old = { id: 10, versionNumber: 1, isCurrent: false, originalFileName: "cnic-front-old.jpg", contentType: "image/jpeg", fileSize: 100 * 1024, uploadedByName: "admin", uploadedAt: "2026-04-18T07:00:00" };
const req = (over: Partial<DocumentRequirement>): DocumentRequirement => ({
  id: 1, name: "CNIC back", isRequired: true, displayOrder: 1, status: "Needed", updatedAt: "2026-04-20T07:00:00",
  concurrencyToken: "tok", versions: [], hasMoreVersions: false, ...over,
});
const checklist: DocumentChecklist = {
  customerId: 5, customerName: "Usman Tariq", stillNeeded: 2, done: 3,
  availableTypes: [{ categoryId: 5, name: "Passport" }, { categoryId: 6, name: "Next-of-Kin CNIC" }],
  requirements: [
    req({ id: 1, name: "CNIC back" }),
    req({ id: 2, name: "Customer photo" }),
    req({ id: 3, name: "CNIC front", status: "Uploaded", latestVersion: jpg, versions: [jpg, old] }),
    req({ id: 4, name: "Proof of address", status: "NotNeeded", notNeededReason: "Lives with parents, no utility bill" }),
  ],
};

const refresh = vi.fn(async () => {});
function show(data: DocumentChecklist | null = checklist, extra: { loading?: boolean; error?: string | null } = {}) {
  return render(
    <ToastProvider>
      <CustomerDocumentsPanel customerId={5} checklist={data} loading={extra.loading ?? false} error={extra.error ?? null} onRefresh={refresh} />
    </ToastProvider>,
  );
}
function phone(on: boolean) {
  window.matchMedia = ((query: string) => ({
    matches: on && query.includes("max-width"), media: query, addEventListener: () => {}, removeEventListener: () => {},
  })) as unknown as typeof window.matchMedia;
}
const rowOf = (name: string) => screen.getByText(name, { selector: "p" }).closest("li")!;
const fileOf = (name: string, size = 1000) => {
  const f = new File(["x"], name);
  Object.defineProperty(f, "size", { value: size });
  return f;
};
const pick = (row: HTMLElement, file: File) => fireEvent.change(row.querySelector<HTMLInputElement>('input[type="file"]')!, { target: { files: [file] } });

beforeEach(() => {
  calls = [];
  pending = [];
  answers = {};
  refresh.mockClear();
  phone(false);
  URL.createObjectURL = vi.fn(() => "blob:preview");
  URL.revokeObjectURL = vi.fn();
});
afterEach(cleanup);

describe("the lists", () => {
  it("shows Still needed and Done with the counts, the badges and one action set per status", () => {
    show();
    expect(within(screen.getByRole("region", { name: "Still needed" })).getByRole("heading").textContent).toBe("Still needed 2");
    expect(within(screen.getByRole("region", { name: "Done" })).getByRole("heading").textContent).toBe("Done 2");
    const needed = rowOf("CNIC back");
    expect(within(needed).getByRole("button", { name: "Not needed" })).toBeTruthy();
    expect(within(needed).getByRole("button", { name: "Upload" })).toBeTruthy();
    const uploaded = rowOf("CNIC front");
    expect(within(uploaded).getByText("Uploaded")).toBeTruthy();
    expect(within(uploaded).getByText("cnic-front.jpg · Apr 20, 2026")).toBeTruthy();
    expect(within(uploaded).getByRole("button", { name: "View" })).toBeTruthy();
    expect(within(uploaded).queryByRole("button", { name: "Upload" })).toBeNull();
    const notNeeded = rowOf("Proof of address");
    expect(within(notNeeded).getByText("Not needed")).toBeTruthy();
    expect(within(notNeeded).getByText("Lives with parents, no utility bill")).toBeTruthy();
    expect(within(notNeeded).getByRole("button", { name: "Upload" })).toBeTruthy();
    expect(within(notNeeded).queryByRole("button", { name: "Not needed" })).toBeNull();
  });

  it("shows an empty state, a loading line and an error", () => {
    show({ ...checklist, requirements: [] });
    expect(screen.getByText("No documents yet")).toBeTruthy();
    cleanup();
    show(null, { loading: true });
    expect(screen.getByText("Loading documents…")).toBeTruthy();
    cleanup();
    show(null, { error: "Unable to load customer documents." });
    expect(screen.getByRole("alert").textContent).toContain("Unable to load customer documents.");
  });
});

describe("uploading from a row", () => {
  it("sends the file with the row's token, shows the percentage and a bar, then reloads", async () => {
    show();
    pick(rowOf("CNIC back"), fileOf("back.jpg"));
    await waitFor(() => expect(pending).toHaveLength(1));
    expect(pending[0].url).toBe("/api/customer-documents/customers/5/requirements/1/upload");
    expect((pending[0].form.get("file") as File).name).toBe("back.jpg");
    expect(pending[0].form.get("concurrencyToken")).toBe("tok");
    expect(screen.getByText("Uploading · 0%")).toBeTruthy();
    pending[0].onProgress(60);
    await waitFor(() => expect(screen.getByText("Uploading · 60%")).toBeTruthy());
    expect(within(rowOf("CNIC back")).getByRole("progressbar").getAttribute("aria-valuenow")).toBe("60");
    expect(within(rowOf("CNIC back")).queryByRole("button", { name: "Upload" })).toBeNull();
    pending[0].resolve(new Response("{}", { status: 200 }));
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.queryByText(/Uploading/)).toBeNull());
  });

  it("stops the upload with the × and goes back to the buttons", async () => {
    show();
    pick(rowOf("CNIC back"), fileOf("back.jpg"));
    await waitFor(() => expect(pending).toHaveLength(1));
    fireEvent.click(screen.getByRole("button", { name: "Stop uploading CNIC back" }));
    await waitFor(() => expect(within(rowOf("CNIC back")).getByRole("button", { name: "Upload" })).toBeTruthy());
    expect(refresh).not.toHaveBeenCalled();
  });

  it("stops a file over 10 MB or of the wrong type in the browser, with a red message and the buttons kept", () => {
    show();
    pick(rowOf("CNIC back"), fileOf("huge.pdf", 11 * 1024 * 1024));
    expect(pending).toHaveLength(0);
    const row = rowOf("CNIC back");
    expect(within(row).getByRole("alert").textContent).toBe("That file is over 10 MB. Choose a smaller one.");
    expect(within(row).getByRole("button", { name: "Upload" })).toBeTruthy();
    pick(row, fileOf("sheet.xlsx"));
    expect(within(row).getByRole("alert").textContent).toBe("This file type isn't allowed. Use PDF, JPG or PNG.");
  });

  it("shows the server's message when it refuses the file", async () => {
    show();
    pick(rowOf("CNIC back"), fileOf("back.jpg"));
    await waitFor(() => expect(pending).toHaveLength(1));
    pending[0].resolve(new Response(JSON.stringify({ message: "This document changed in another browser. Refresh before trying again." }), { status: 409 }));
    await waitFor(() => expect(within(rowOf("CNIC back")).getByRole("alert").textContent).toContain("changed in another browser"));
    expect(refresh).not.toHaveBeenCalled();
  });
});

describe("Not needed", () => {
  it("asks for a reason, sends it with the token, and reloads", async () => {
    answers["POST /api/customer-documents/customers/5/requirements/2/not-needed"] = () => ({ body: {} });
    show();
    fireEvent.click(within(rowOf("Customer photo")).getByRole("button", { name: "Not needed" }));
    expect(screen.getByText("Customer photo not needed?")).toBeTruthy();
    expect(screen.getByText("It moves to Done and stops counting as missing.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Mark not needed" }));
    expect(screen.getByText("Enter why this document is not needed.")).toBeTruthy();
    expect(calls.filter((c) => c.method === "POST")).toHaveLength(0);
    fireEvent.change(screen.getByLabelText(/Reason/), { target: { value: "  Photo is already on the application form.  " } });
    fireEvent.click(screen.getByRole("button", { name: "Mark not needed" }));
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
    expect(calls.find((c) => c.method === "POST")?.body).toEqual({ reason: "Photo is already on the application form.", concurrencyToken: "tok" });
    await waitFor(() => expect(screen.queryByText("Customer photo not needed?")).toBeNull());
  });

  it("keeps the popup open and shows the server's message when it fails", async () => {
    answers["POST /api/customer-documents/customers/5/requirements/2/not-needed"] = () => ({ status: 400, body: { message: "Only a document that is still needed can be marked not needed." } });
    show();
    fireEvent.click(within(rowOf("Customer photo")).getByRole("button", { name: "Not needed" }));
    fireEvent.change(screen.getByLabelText(/Reason/), { target: { value: "x" } });
    fireEvent.click(screen.getByRole("button", { name: "Mark not needed" }));
    await waitFor(() => expect(screen.getByText("Only a document that is still needed can be marked not needed.")).toBeTruthy());
    expect(refresh).not.toHaveBeenCalled();
  });
});

describe("Add document", () => {
  const open = () => fireEvent.click(screen.getByRole("button", { name: "Add document" }));
  const dialog = () => screen.getByRole("dialog");

  it("offers the types from the server plus Other, and keeps Save off until a type and a file are chosen", async () => {
    show();
    open();
    expect(within(dialog()).getByText("Usman Tariq")).toBeTruthy();
    const save = within(dialog()).getByRole("button", { name: "Save" }) as HTMLButtonElement;
    expect(save.disabled).toBe(true);
    fireEvent.click(within(dialog()).getByRole("combobox"));
    expect(screen.getByRole("option", { name: "Passport" })).toBeTruthy();
    expect(screen.getByRole("option", { name: "Next-of-Kin CNIC" })).toBeTruthy();
    expect(screen.getByRole("option", { name: "Other" })).toBeTruthy();
    fireEvent.click(screen.getByRole("option", { name: "Passport" }));
    expect(save.disabled).toBe(true);
    fireEvent.change(dialog().querySelector<HTMLInputElement>('input[type="file"]')!, { target: { files: [fileOf("passport.pdf", 310 * 1024)] } });
    await waitFor(() => expect(save.disabled).toBe(false));
  });

  it("sends the chosen type and file, then reloads and closes", async () => {
    show();
    open();
    fireEvent.click(within(dialog()).getByRole("combobox"));
    fireEvent.click(screen.getByRole("option", { name: "Passport" }));
    fireEvent.change(dialog().querySelector<HTMLInputElement>('input[type="file"]')!, { target: { files: [fileOf("passport.pdf")] } });
    fireEvent.click(within(dialog()).getByRole("button", { name: "Save" }));
    await waitFor(() => expect(pending).toHaveLength(1));
    expect(pending[0].url).toBe("/api/customer-documents/customers/5/documents");
    expect(pending[0].form.get("categoryId")).toBe("5");
    expect(pending[0].form.get("name")).toBeNull();
    pending[0].resolve(new Response("{}", { status: 200 }));
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("asks for a Name when Other is chosen and sends it instead of a type", async () => {
    show();
    open();
    fireEvent.click(within(dialog()).getByRole("combobox"));
    fireEvent.click(screen.getByRole("option", { name: "Other" }));
    fireEvent.change(dialog().querySelector<HTMLInputElement>('input[type="file"]')!, { target: { files: [fileOf("visa.png")] } });
    const save = within(dialog()).getByRole("button", { name: "Save" }) as HTMLButtonElement;
    expect(save.disabled).toBe(true);
    fireEvent.change(within(dialog()).getByLabelText(/Name/), { target: { value: " Visa copy " } });
    await waitFor(() => expect(save.disabled).toBe(false));
    fireEvent.click(save);
    await waitFor(() => expect(pending).toHaveLength(1));
    expect(pending[0].form.get("name")).toBe("Visa copy");
    expect(pending[0].form.get("categoryId")).toBeNull();
  });

  it("shows the server's message, such as a duplicate, and stays open", async () => {
    show();
    open();
    fireEvent.click(within(dialog()).getByRole("combobox"));
    fireEvent.click(screen.getByRole("option", { name: "Passport" }));
    fireEvent.change(dialog().querySelector<HTMLInputElement>('input[type="file"]')!, { target: { files: [fileOf("p.pdf")] } });
    fireEvent.click(within(dialog()).getByRole("button", { name: "Save" }));
    await waitFor(() => expect(pending).toHaveLength(1));
    pending[0].resolve(new Response(JSON.stringify({ message: "This customer already has that document." }), { status: 409 }));
    await waitFor(() => expect(within(dialog()).getByRole("alert").textContent).toContain("already has that document"));
    expect(refresh).not.toHaveBeenCalled();
  });

  it("is full screen on a phone", () => {
    phone(true);
    show();
    open();
    expect(within(dialog()).getByRole("button", { name: "Save" })).toBeTruthy();
  });
});

describe("View", () => {
  beforeEach(() => {
    answers["GET /api/customer-documents/customers/5/requirements/3/versions/11/view"] = () => ({ body: null, blob: new Blob(["img"], { type: "image/jpeg" }) });
    answers["GET /api/customer-documents/customers/5/requirements/3/versions/10/view"] = () => ({ body: null, blob: new Blob(["old"], { type: "image/jpeg" }) });
  });
  const open = () => fireEvent.click(within(rowOf("CNIC front")).getByRole("button", { name: "View" }));

  it("previews the current file inside the popup with its details and the older files", async () => {
    show();
    open();
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("CNIC front", { selector: "h2" })).toBeTruthy();
    await waitFor(() => expect(within(dialog).getByRole("img", { name: "cnic-front.jpg" })).toBeTruthy());
    expect(within(dialog).getByText("240 KB · Uploaded Apr 20, 2026 by admin")).toBeTruthy();
    const older = within(dialog).getByRole("region", { name: "Older files" });
    expect(within(older).getByText("cnic-front-old.jpg · Apr 18, 2026")).toBeTruthy();
    expect(within(dialog).getByRole("button", { name: "Replace file" })).toBeTruthy();
    expect(within(dialog).getByRole("button", { name: "Download" })).toBeTruthy();
    expect(calls.map((c) => c.url)).toContain("/api/customer-documents/customers/5/requirements/3/versions/11/view");
  });

  it("shows an older file when its View is pressed", async () => {
    show();
    open();
    const dialog = screen.getByRole("dialog");
    await waitFor(() => expect(within(dialog).getByRole("img")).toBeTruthy());
    fireEvent.click(within(within(dialog).getByRole("region", { name: "Older files" })).getByRole("button", { name: "View" }));
    await waitFor(() => expect(within(dialog).getByText("100 KB · Uploaded Apr 18, 2026 by admin")).toBeTruthy());
    expect(calls.map((c) => c.url)).toContain("/api/customer-documents/customers/5/requirements/3/versions/10/view");
  });

  it("shows the server's message when the file cannot be opened", async () => {
    answers["GET /api/customer-documents/customers/5/requirements/3/versions/11/view"] = () => ({ status: 404, body: { message: "The stored document is unavailable. Upload the file again." } });
    show();
    open();
    await waitFor(() => expect(within(screen.getByRole("dialog")).getByRole("alert").textContent).toContain("The stored document is unavailable"));
  });

  it("downloads through the file call", async () => {
    answers["GET /api/customer-documents/customers/5/requirements/3/versions/11/file"] = () => ({ body: null, blob: new Blob(["img"], { type: "image/jpeg" }) });
    show();
    open();
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Download" }));
    await waitFor(() => expect(calls.map((c) => c.url)).toContain("/api/customer-documents/customers/5/requirements/3/versions/11/file"));
  });

  it("Replace file sends the new file for the same document and closes the popup", async () => {
    show();
    open();
    const dialog = screen.getByRole("dialog");
    fireEvent.change(dialog.querySelector<HTMLInputElement>('input[type="file"]')!, { target: { files: [fileOf("new-front.png")] } });
    await waitFor(() => expect(pending).toHaveLength(1));
    expect(pending[0].url).toBe("/api/customer-documents/customers/5/requirements/3/upload");
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.getByText("Uploading · 0%")).toBeTruthy();
  });
});
