import { api } from "../../api/api";
import type { AuditEntry, BookingWorkspace, Commission, CommissionPreview, CommissionRebateSummary, PagedResult, Partner, Rebate } from "./types";

const root = "/api/finance/commissions-rebates";

export async function apiError(response: Response, fallback: string) {
  const payload = await response.json().catch(() => null) as { message?:string } | null;
  return new Error(payload?.message ?? fallback);
}

async function json<T>(path:string, options?:RequestInit):Promise<T> {
  const response = await api(`${root}${path}`, options);
  if (!response.ok) throw await apiError(response, "The financial workflow request could not be completed.");
  return await response.json() as T;
}

export const commissionRebateApi = {
  summary: () => json<CommissionRebateSummary>("/summary"),
  partners: (search="", isActive?:boolean, skip=0, take=20, signal?:AbortSignal) => {
    const q = new URLSearchParams({skip:String(skip),take:String(take),includeTotal:"true"}); if(search)q.set("search",search); if(isActive!==undefined)q.set("isActive",String(isActive));
    return json<PagedResult<Partner>>(`/partners?${q}`, {signal});
  },
  /** One partner by id, or null when there is no such partner (a stale link). */
  partner: async (id:number, signal?:AbortSignal) => {
    const response = await api(`${root}/partners/${id}`, {signal});
    if (response.status === 404) return null;
    if (!response.ok) throw await apiError(response, "The partner could not be loaded.");
    return await response.json() as Partner;
  },
  savePartner: (body:unknown, id?:number) => json<Partner>(id?`/partners/${id}`:"/partners", {method:id?"PUT":"POST",body:JSON.stringify(body)}),
  partnerStatus: (id:number, body:unknown) => json<Partner>(`/partners/${id}/status`, {method:"PATCH",body:JSON.stringify(body)}),
  commissions: (status="", skip=0, take=20, signal?:AbortSignal) => { const q=new URLSearchParams({skip:String(skip),take:String(take),includeTotal:"true"});if(status)q.set("status",status);return json<PagedResult<Commission>>(`/commissions?${q}`, {signal}); },
  rebates: (status="", skip=0, take=20, signal?:AbortSignal) => { const q=new URLSearchParams({skip:String(skip),take:String(take),includeTotal:"true"});if(status)q.set("status",status);return json<PagedResult<Rebate>>(`/rebates?${q}`, {signal}); },
  workspace: (bookingId:number) => json<BookingWorkspace>(`/bookings/${bookingId}`),
  bookingAudit: (bookingId:number, beforeId?:number, take=50) => {
    const q = new URLSearchParams({take:String(take)}); if(beforeId!==undefined)q.set("beforeId",String(beforeId));
    return json<PagedResult<AuditEntry>>(`/bookings/${bookingId}/audit?${q}`);
  },
  createCommission: (bookingId:number, body:unknown) => json<BookingWorkspace>(`/bookings/${bookingId}/commissions`, {method:"POST",body:JSON.stringify(body)}),
  /** What the rules give this partner on the booking, without saving (the edit keeps the commission's own adjustment). */
  previewCommission: (bookingId:number, partnerId:number, commissionId?:number) => json<CommissionPreview>(`/bookings/${bookingId}/commission-preview?partnerId=${partnerId}${commissionId?`&commissionId=${commissionId}`:""}`),
  updateCommission: (bookingId:number, commissionId:number, body:unknown) => json<BookingWorkspace>(`/bookings/${bookingId}/commissions/${commissionId}`, {method:"PUT",body:JSON.stringify(body)}),
  commissionStatus: (bookingId:number, commissionId:number, body:unknown) => json<BookingWorkspace>(`/bookings/${bookingId}/commissions/${commissionId}/status`, {method:"POST",body:JSON.stringify(body)}),
  payout: (bookingId:number, commissionId:number, body:unknown) => json<BookingWorkspace>(`/bookings/${bookingId}/commissions/${commissionId}/payouts`, {method:"POST",body:JSON.stringify(body)}),
  reversePayout: (bookingId:number, commissionId:number, payoutId:number, body:unknown) => json<BookingWorkspace>(`/bookings/${bookingId}/commissions/${commissionId}/payouts/${payoutId}/reversals`, {method:"POST",body:JSON.stringify(body)}),
  createRebate: (bookingId:number, body:unknown) => json<BookingWorkspace>(`/bookings/${bookingId}/rebates`, {method:"POST",body:JSON.stringify(body)}),
  updateRebate: (bookingId:number, rebateId:number, body:unknown) => json<BookingWorkspace>(`/bookings/${bookingId}/rebates/${rebateId}`, {method:"PUT",body:JSON.stringify(body)}),
  rebateStatus: (bookingId:number, rebateId:number, body:unknown) => json<BookingWorkspace>(`/bookings/${bookingId}/rebates/${rebateId}/status`, {method:"POST",body:JSON.stringify(body)}),
  disburseRebate: (bookingId:number, rebateId:number, body:unknown) => json<BookingWorkspace>(`/bookings/${bookingId}/rebates/${rebateId}/disbursements`, {method:"POST",body:JSON.stringify(body)}),
  reverseDisbursement: (bookingId:number, rebateId:number, disbursementId:number, body:unknown) => json<BookingWorkspace>(`/bookings/${bookingId}/rebates/${rebateId}/disbursements/${disbursementId}/reversals`, {method:"POST",body:JSON.stringify(body)}),
  uploadEvidence: async (ownerType:string, ownerId:number, file:File) => {
    const form = new FormData(); form.append("file",file);
    return json<unknown>(`/evidence/${ownerType}/${ownerId}`, {method:"POST",body:form});
  },
  openEvidence: async (id:number) => {
    const response = await api(`${root}/evidence/${id}/file`);
    if (!response.ok) throw await apiError(response,"Evidence could not be opened.");
    const url = URL.createObjectURL(await response.blob());
    window.open(url,"_blank","noopener,noreferrer");
    window.setTimeout(()=>URL.revokeObjectURL(url),60_000);
  },
};
