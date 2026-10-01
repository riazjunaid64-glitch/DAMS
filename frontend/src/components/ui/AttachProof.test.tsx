// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AttachProof } from "./AttachProof.tsx";
import { PROOF_MAX_BYTES, PROOF_TOO_LARGE, PROOF_WRONG_TYPE } from "./proofFile.ts";

function phone(matches: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches, media: query, addEventListener: () => {}, removeEventListener: () => {} }));
}

beforeEach(() => phone(false));
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const fileOf = (name: string, size: number) => {
  const file = new File(["x"], name);
  Object.defineProperty(file, "size", { value: size });
  return file;
};

const input = () => document.querySelector<HTMLInputElement>('input[type="file"]')!;

describe("AttachProof", () => {
  it("is always optional and shows the drop zone when empty", () => {
    render(<AttachProof file={null} onPick={() => {}} />);
    expect(screen.getByText("(optional)")).toBeTruthy();
    expect(screen.getByText("browse")).toBeTruthy();
    expect(screen.getByText("PDF, image, Word or Excel · up to 15 MB")).toBeTruthy();
  });

  it("asks for a tap on a phone", () => {
    phone(true);
    render(<AttachProof file={null} onPick={() => {}} />);
    expect(screen.getByText("upload")).toBeTruthy();
    expect(screen.queryByText("browse")).toBeNull();
  });

  it("hands a good file to onPick", () => {
    const onPick = vi.fn();
    render(<AttachProof file={null} onPick={onPick} />);
    const slip = fileOf("transfer-slip.jpg", 240 * 1024);
    fireEvent.change(input(), { target: { files: [slip] } });
    expect(onPick).toHaveBeenCalledWith(slip);
  });

  it("stops a file over 15 MB in the browser and shows the message", () => {
    const onPick = vi.fn();
    render(<AttachProof file={null} onPick={onPick} />);
    fireEvent.change(input(), { target: { files: [fileOf("big.pdf", PROOF_MAX_BYTES + 1)] } });
    expect(onPick).not.toHaveBeenCalled();
    expect(screen.getByRole("alert").textContent).toBe(PROOF_TOO_LARGE);
  });

  it("stops a wrong type, and picking again replaces the message", () => {
    const onPick = vi.fn();
    render(<AttachProof file={null} onPick={onPick} />);
    fireEvent.change(input(), { target: { files: [fileOf("run.exe", 10)] } });
    expect(screen.getByRole("alert").textContent).toBe(PROOF_WRONG_TYPE);
    fireEvent.change(input(), { target: { files: [fileOf("slip.pdf", 10)] } });
    expect(screen.queryByRole("alert")).toBeNull();
    expect(onPick).toHaveBeenCalledTimes(1);
  });

  it("takes a dropped file, and reads 'Drop the file to upload' while dragging over", () => {
    const onPick = vi.fn();
    render(<AttachProof file={null} onPick={onPick} />);
    const zone = screen.getByRole("button");
    fireEvent.dragOver(zone);
    expect(screen.getByText("Drop the file to upload")).toBeTruthy();
    const slip = fileOf("cheque.png", 1000);
    fireEvent.drop(zone, { dataTransfer: { files: [slip] } });
    expect(onPick).toHaveBeenCalledWith(slip);
    expect(screen.queryByText("Drop the file to upload")).toBeNull();
  });

  it("shows upload progress with a stop button", () => {
    const onRemove = vi.fn();
    render(<AttachProof file={{ name: "transfer-slip.jpg", size: 1000 }} progress={60} onPick={() => {}} onRemove={onRemove} />);
    expect(screen.getByText("Uploading · 60%")).toBeTruthy();
    expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("60");
    fireEvent.click(screen.getByRole("button", { name: "Stop upload" }));
    expect(onRemove).toHaveBeenCalled();
  });

  it("shows a stored file with its size, opens it by name and removes it with ×", () => {
    const onOpen = vi.fn();
    const onRemove = vi.fn();
    render(<AttachProof file={{ name: "transfer-slip.jpg", size: 240 * 1024, uploaded: true, onOpen }} onPick={() => {}} onRemove={onRemove} />);
    expect(screen.getByText("Uploaded · 240 KB")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "transfer-slip.jpg" }));
    expect(onOpen).toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Remove file" }));
    expect(onRemove).toHaveBeenCalled();
  });

  it("says a file picked but not yet sent is ready, not uploaded", () => {
    render(<AttachProof file={{ name: "slip.pdf", size: 2048, uploaded: false }} onPick={() => {}} />);
    expect(screen.getByText("Ready to upload · 2 KB")).toBeTruthy();
    expect(screen.queryByText(/^Uploaded/)).toBeNull();
  });

  it("shows a server error in the zone", () => {
    render(<AttachProof file={null} error="The proof could not be uploaded." onPick={() => {}} />);
    expect(screen.getByRole("alert").textContent).toBe("The proof could not be uploaded.");
  });

  it("does not open when disabled", () => {
    render(<AttachProof file={null} disabled onPick={() => {}} />);
    expect((screen.getByRole("button") as HTMLButtonElement).disabled).toBe(true);
    expect(input().disabled).toBe(true);
  });

  it("takes another rule and shows a * instead of (optional) when required", () => {
    const rule = { extensions: [".pdf", ".png"], maxBytes: 10 * 1024 * 1024, accept: ".pdf,.png", hint: "PDF or PNG · up to 10 MB", tooLarge: "Over 10 MB.", wrongType: "Use PDF or PNG.", empty: "Empty." };
    const onPick = vi.fn();
    render(<AttachProof label="File" required rule={rule} file={null} onPick={onPick} />);
    expect(screen.queryByText("(optional)")).toBeNull();
    expect(screen.getByText("PDF or PNG · up to 10 MB")).toBeTruthy();
    expect(input().accept).toBe(".pdf,.png");
    fireEvent.change(input(), { target: { files: [fileOf("scan.docx", 100)] } });
    expect(screen.getByText("Use PDF or PNG.")).toBeTruthy();
    expect(onPick).not.toHaveBeenCalled();
    const ok = fileOf("scan.png", 100);
    fireEvent.change(input(), { target: { files: [ok] } });
    expect(onPick).toHaveBeenCalledWith(ok);
  });
});
