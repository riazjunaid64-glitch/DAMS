// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { SavedProof } from "./SavedProof";
import { openProof, uploadProof } from "./proofApi";

vi.mock("./proofApi", () => ({ uploadProof: vi.fn(), openProof: vi.fn() }));

const slip = new File(["x"], "slip.pdf", { type: "application/pdf" });
const pick = (file: File) => fireEvent.change(screen.getByLabelText("Attach proof file"), { target: { files: [file] } });

beforeEach(() => {
  vi.mocked(uploadProof).mockReset();
  vi.mocked(openProof).mockReset();
});
afterEach(cleanup);

describe("SavedProof", () => {
  it("shows the file name as a link that opens the proof", async () => {
    render(<SavedProof ownerType="CustomerPayment" ownerId={5} proof={{ id: 9, fileName: "transfer.pdf", fileSize: 10 }} onChanged={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "transfer.pdf" }));
    await waitFor(() => expect(openProof).toHaveBeenCalledWith(9));
    expect(screen.queryByRole("button", { name: "Attach proof" })).toBeNull();
  });

  it("offers Attach proof when there is none, and stores the file on that record", async () => {
    vi.mocked(uploadProof).mockResolvedValue();
    const onChanged = vi.fn();
    render(<SavedProof ownerType="CancellationRefund" ownerId={31} proof={null} onChanged={onChanged} />);
    expect(screen.getByRole("button", { name: "Attach proof" })).toBeTruthy();
    pick(slip);
    await waitFor(() => expect(uploadProof).toHaveBeenCalledWith("CancellationRefund", 31, slip));
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
  });

  it("shows why a refused file or a failed upload did not go up, and can be tried again", async () => {
    vi.mocked(uploadProof).mockRejectedValueOnce(new Error("Network down")).mockResolvedValueOnce();
    const onChanged = vi.fn();
    render(<SavedProof ownerType="CustomerPayment" ownerId={5} onChanged={onChanged} />);

    pick(new File(["x"], "virus.exe"));
    expect((await screen.findByRole("alert")).textContent).toBeTruthy();
    expect(uploadProof).not.toHaveBeenCalled();

    pick(slip);
    expect((await screen.findByText("Network down"))).toBeTruthy();
    expect(onChanged).not.toHaveBeenCalled();

    pick(slip);
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
  });
});
