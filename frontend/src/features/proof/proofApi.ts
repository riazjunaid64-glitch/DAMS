import { api, apiUpload } from "../../api/api";
import { apiError } from "../commissionRebates/api";

/** The records a proof file can be attached to. */
export type ProofOwnerType = "CustomerPayment" | "CancellationRefund" | "Commission" | "CommissionPayout" | "Rebate" | "RebateDisbursement";

/** What a saved payment or refund carries so a screen can show the file link without another call. */
export interface ProofFile {
  id: number;
  fileName: string;
  fileSize: number;
}

const root = "/api/finance/commissions-rebates/evidence";

/** Stores one proof file on a saved record. Throws with the server's message when it is refused. */
export async function uploadProof(
  ownerType: ProofOwnerType,
  ownerId: number,
  file: File,
  onProgress?: (percent: number) => void,
  signal?: AbortSignal,
): Promise<void> {
  const form = new FormData();
  form.append("file", file);
  const response = await apiUpload(`${root}/${ownerType}/${ownerId}`, form, onProgress, signal);
  if (!response.ok) throw await apiError(response, "The proof could not be uploaded.");
}

/** Opens a stored proof file in a new tab. */
export async function openProof(id: number): Promise<void> {
  const response = await api(`${root}/${id}/file`);
  if (!response.ok) throw await apiError(response, "The proof could not be opened.");
  const url = URL.createObjectURL(await response.blob());
  window.open(url, "_blank", "noopener,noreferrer");
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
