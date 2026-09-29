// @vitest-environment happy-dom
import { act, renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { uploadProof } from "./proofApi";
import { useProofUpload } from "./useProofUpload";

vi.mock("./proofApi", () => ({ uploadProof: vi.fn() }));

const slip = new File(["x"], "transfer-slip.jpg");

beforeEach(() => vi.mocked(uploadProof).mockReset());

describe("useProofUpload", () => {
  it("has nothing to send until a file is picked, and that counts as fine", async () => {
    const { result } = renderHook(() => useProofUpload());
    expect(result.current.hasFile).toBe(false);
    let ok = false;
    await act(async () => { ok = await result.current.upload("CustomerPayment", 5); });
    expect(ok).toBe(true);
    expect(uploadProof).not.toHaveBeenCalled();
  });

  it("sends the picked file to the saved record", async () => {
    vi.mocked(uploadProof).mockResolvedValue();
    const { result } = renderHook(() => useProofUpload());
    act(() => result.current.fieldProps.onPick(slip));
    expect(result.current.fieldProps.file).toMatchObject({ name: "transfer-slip.jpg", uploaded: false });
    let ok = false;
    await act(async () => { ok = await result.current.upload("CustomerPayment", 5); });
    expect(ok).toBe(true);
    expect(uploadProof).toHaveBeenCalledWith("CustomerPayment", 5, slip, expect.any(Function), expect.any(AbortSignal));
    expect(result.current.fieldProps.file?.uploaded).toBe(true);
    expect(result.current.fieldProps.progress).toBeNull();
  });

  it("reports a failed upload without losing the file, so it can be tried again", async () => {
    vi.mocked(uploadProof).mockRejectedValueOnce(new Error("The server said no."));
    const { result } = renderHook(() => useProofUpload());
    act(() => result.current.fieldProps.onPick(slip));
    let ok = true;
    await act(async () => { ok = await result.current.upload("CancellationRefund", 9); });
    expect(ok).toBe(false);
    expect(result.current.fieldProps.error).toBe("The server said no.");
    expect(result.current.hasFile).toBe(true);
    vi.mocked(uploadProof).mockResolvedValueOnce();
    await act(async () => { ok = await result.current.upload("CancellationRefund", 9); });
    expect(ok).toBe(true);
    expect(result.current.fieldProps.error).toBeNull();
  });

  it("clears the file with ×", () => {
    const { result } = renderHook(() => useProofUpload());
    act(() => result.current.fieldProps.onPick(slip));
    act(() => result.current.fieldProps.onRemove?.());
    expect(result.current.hasFile).toBe(false);
  });
});
