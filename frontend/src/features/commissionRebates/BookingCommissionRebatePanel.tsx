import { useCallback, useEffect, useMemo, useRef, useState, type ButtonHTMLAttributes, type FormEvent, type ReactNode } from "react";
import { api } from "../../api/api";
import Button from "../../lib/Button";
import Field from "../../lib/Field";
import { apiError, commissionRebateApi } from "./api";
import { commissionActions, idempotencyKey, isPendingStatus, money, pakistanToday, prettyEnum, rebateActions, trapDialogKeys } from "./state";
import type { AuditEntry, BookingWorkspace, CalculationBasis, CalculationType, Commission, FinanceAccountOption, InstallmentOption, Partner, Rebate, RebateMethod } from "./types";

const input="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2.5 text-sm text-[var(--text-primary)] disabled:opacity-60";
const evidenceAccept=".pdf,.jpg,.jpeg,.png,.webp,.doc,.docx,.xls,.xlsx";
// Mirrors the directory's accepted partner types (CommissionRebateService.Directory).
const partnerTypes=["Broker","Dealer","Agency","Referral Partner","Introducer","Marketing Partner","External Sales Agent","Other"];
// Only the bases a booking screen can state in one line. The enum still carries the rest for
// rule-driven commissions configured on the commission settings page.
const commissionBases:CalculationBasis[]=["NetSalePriceAfterDiscount","AgreedSalePrice","AmountActuallyCollected"];
const rebateBases:CalculationBasis[]=["AgreedSalePrice","NetSalePriceAfterDiscount"];
const basisNames:Record<string,string>={AgreedSalePrice:"Sale Price",NetSalePriceAfterDiscount:"Net Sale Price",AmountActuallyCollected:"Amount Collected",BookingAmountReceived:"Booking Amount Received",ManuallyApprovedAmount:"Manually Approved Amount"};
const calculationTypes=[{v:"FixedAmount",n:"Fixed Amount"},{v:"Percentage",n:"Percentage"}];
const rebateMethods:RebateMethod[]=["OutstandingBalanceReduction","InstallmentAdjustment","CashOrBankPayment","CreditNote","Other"];
const paymentMethods=["Cash","BankTransfer","Cheque","Online"].map(v=>({v,n:prettyEnum(v)}));
// A cancelled or reversed record is closed history: it no longer holds the booking's one live
// rebate, nor its partner's one live commission.
const closedStatuses=["Cancelled","Reversed"];

export default function BookingCommissionRebatePanel({bookingId}:{bookingId:number}){
  const [workspace,setWorkspace]=useState<BookingWorkspace|null>(null),[partners,setPartners]=useState<Partner[]>([]),[accounts,setAccounts]=useState<FinanceAccountOption[]>([]),[installments,setInstallments]=useState<InstallmentOption[]>([]);
  const [loading,setLoading]=useState(true),[busy,setBusy]=useState(false),[error,setError]=useState<string|null>(null),[showAudit,setShowAudit]=useState(false);
  const [olderAudit,setOlderAudit]=useState<AuditEntry[]>([]),[auditHasMore,setAuditHasMore]=useState(false),[auditBusy,setAuditBusy]=useState(false);
  const [showCommission,setShowCommission]=useState(false),[showRebate,setShowRebate]=useState(false),[showPartner,setShowPartner]=useState(false);
  const [payoutFor,setPayoutFor]=useState<Commission|null>(null),[disburseFor,setDisburseFor]=useState<Rebate|null>(null),[editingCommission,setEditingCommission]=useState<Commission|null>(null),[editingRebate,setEditingRebate]=useState<Rebate|null>(null);
  const [commission,setCommission]=useState({...emptyCommissionForm()});
  const [rebate,setRebate]=useState({...emptyRebateForm()});
  const [partnerForm,setPartnerForm]=useState({...emptyPartnerForm()});
  const [payout,setPayout]=useState({financeAccountId:"",amount:"",paymentDate:pakistanToday(),paymentMethod:"BankTransfer",paymentReference:"",notes:"",idempotencyKey:idempotencyKey("commission-payout")});
  const [disbursement,setDisbursement]=useState({method:"OutstandingBalanceReduction" as RebateMethod,amount:"",appliedAt:pakistanToday(),financeAccountId:"",installmentId:"",paymentMethod:"BankTransfer",reference:"",notes:"",idempotencyKey:idempotencyKey("rebate-disbursement")});
  const reversalKeys=useRef(new Map<string,string>()),loadSequence=useRef(0);
  const load=useCallback(async()=>{const request=++loadSequence.current;setLoading(true);setError(null);try{const [w,p,a,s]=await Promise.all([commissionRebateApi.workspace(bookingId),commissionRebateApi.partners("",true,0,100),api("/api/finance/accounts/options"),api(`/api/Booking/${bookingId}/installments`)]);if(!a.ok)throw await apiError(a,"Finance account options could not be loaded.");if(!s.ok)throw await apiError(s,"The installment schedule could not be loaded.");const accountOptions=await a.json() as FinanceAccountOption[];const schedule=await s.json() as {items:InstallmentOption[]};if(request!==loadSequence.current)return;setWorkspace(w);setPartners(p.items);setAccounts(accountOptions);setInstallments(schedule.items);}catch(x){if(request===loadSequence.current)setError(x instanceof Error?x.message:"Commission and rebate details could not be loaded.");}finally{if(request===loadSequence.current)setLoading(false);}},[bookingId]);
  useEffect(()=>{void load();},[load]);
  // The workspace inlines only the newest audit preview; reset any appended older pages whenever it
  // reloads, then pull older rows on demand with the keyset endpoint (beforeId = oldest row shown).
  useEffect(()=>{setOlderAudit([]);setAuditHasMore(workspace?.hasMoreAudit??false);},[workspace]);
  const loadMoreAudit=async()=>{if(!workspace)return;const rows=[...workspace.audit,...olderAudit];const beforeId=rows[rows.length-1]?.id;if(beforeId===undefined)return;setAuditBusy(true);setError(null);try{const page=await commissionRebateApi.bookingAudit(bookingId,beforeId,50);setOlderAudit(c=>[...c,...page.items]);setAuditHasMore(page.hasMore);}catch(x){setError(x instanceof Error?x.message:"Older audit history could not be loaded.");}finally{setAuditBusy(false);}};
  const active=workspace?.bookingStatus!=="Cancelled";

  // Every amount the entry forms preview is derived from the same booking figures the server
  // recalculates on save, so what the user reads before saving is what gets stored.
  const basisValue=useCallback((basis:CalculationBasis)=>{
    if(!workspace)return 0;
    if(basis==="AgreedSalePrice")return workspace.agreedSalePrice;
    if(basis==="NetSalePriceAfterDiscount")return workspace.netSalePrice;
    if(basis==="AmountActuallyCollected")return workspace.amountCollected;
    return 0;
  },[workspace]);
  const preview=useCallback((type:CalculationType,basis:CalculationBasis,rate:string,fixed:string)=>{
    const raw=type==="Percentage"?basisValue(basis)*Number(rate)/100:Number(fixed);
    return Number.isFinite(raw)&&raw>0?Math.round(raw*100)/100:0;
  },[basisValue]);
  const commissionAmount=useMemo(()=>preview(commission.calculationType,commission.calculationBasis,commission.percentageRate,commission.fixedAmount),[commission,preview]);
  const rebateAmount=useMemo(()=>preview(rebate.calculationType,rebate.calculationBasis,rebate.percentageRate,rebate.fixedAmount),[rebate,preview]);
  const netAfterRebate=Math.max(0,(workspace?.netSalePrice??0)-rebateAmount);
  // A partner can hold one live commission per booking, so anyone already on the booking drops out
  // of the picker — the booking itself can carry as many partners as it needs.
  const partnerOptions=useMemo(()=>{
    const taken=new Set((workspace?.commissions??[]).filter(c=>!closedStatuses.includes(c.status)).map(c=>c.partnerId));
    return partners.filter(p=>String(p.id)===commission.partnerId||!taken.has(p.id));
  },[partners,workspace?.commissions,commission.partnerId]);
  const liveRebate=workspace?.rebates.some(r=>!closedStatuses.includes(r.status))??false;

  const execute=async(operation:()=>Promise<BookingWorkspace>)=>{setBusy(true);setError(null);try{setWorkspace(await operation());return true;}catch(x){setError(x instanceof Error?x.message:"Financial action could not be completed.");return false;}finally{setBusy(false);}};

  const savePartner=async(e:FormEvent)=>{e.preventDefault();setBusy(true);setError(null);
    try{
      const created=await commissionRebateApi.savePartner({name:partnerForm.name,partnerType:partnerForm.partnerType,phone:partnerForm.phone||null,email:partnerForm.email||null});
      const page=await commissionRebateApi.partners("",true,0,100);
      setPartners(page.items);setCommission(c=>({...c,partnerId:String(created.id)}));
      setShowPartner(false);setPartnerForm(emptyPartnerForm());
    }catch(x){setError(x instanceof Error?x.message:"The partner could not be saved.");}
    finally{setBusy(false);}};

  const saveCommission=async(e:FormEvent)=>{e.preventDefault();await execute(async()=>{
    const percentage=commission.calculationType==="Percentage";
    const body={
      partnerId:Number(commission.partnerId),attributionId:null,ruleId:null,isManual:true,
      manualReason:commission.notes.trim()||null,manualCalculationType:commission.calculationType,
      // A fixed amount still records a basis so the saved commission reads back against the booking
      // it was agreed on. It never changes the amount.
      manualCalculationBasis:percentage?commission.calculationBasis:"NetSalePriceAfterDiscount",
      manualPercentageRate:percentage?Number(commission.percentageRate):null,
      manualFixedAmount:percentage?null:Number(commission.fixedAmount),
      manualBasisAmount:null,manualEarningCondition:"ManualMilestone",minimumCollectionPercent:null,
      adjustmentAmount:0,adjustmentReason:null,
      ...(editingCommission?{concurrencyToken:editingCommission.concurrencyToken,changeReason:commission.changeReason}:{})
    };
    const result=editingCommission?await commissionRebateApi.updateCommission(bookingId,editingCommission.id,body):await commissionRebateApi.createCommission(bookingId,body);
    setShowCommission(false);setEditingCommission(null);return result;});};

  const saveRebate=async(e:FormEvent)=>{e.preventDefault();await execute(async()=>{
    const percentage=rebate.calculationType==="Percentage";
    const body={
      calculationType:rebate.calculationType,
      calculationBasis:percentage?rebate.calculationBasis:"AgreedSalePrice",
      percentageRate:percentage?Number(rebate.percentageRate):null,
      fixedAmount:percentage?null:Number(rebate.fixedAmount),
      manualBasisAmount:null,adjustmentAmount:0,adjustmentReason:null,
      reason:rebate.reason.trim()||null,notes:null,
      // How the rebate reaches the customer is chosen when it is applied, not here.
      method:editingRebate?editingRebate.method:"OutstandingBalanceReduction",
      ...(editingRebate?{concurrencyToken:editingRebate.concurrencyToken,changeReason:rebate.changeReason}:{})
    };
    const result=editingRebate?await commissionRebateApi.updateRebate(bookingId,editingRebate.id,body):await commissionRebateApi.createRebate(bookingId,body);
    setShowRebate(false);setEditingRebate(null);return result;});};

  const openNewCommission=()=>{setEditingCommission(null);setCommission(emptyCommissionForm());setShowCommission(true);};
  const openNewRebate=()=>{setEditingRebate(null);setRebate(emptyRebateForm());setShowRebate(true);};
  const editCommission=(value:Commission,changeReason:string)=>{setEditingCommission(value);setCommission({partnerId:String(value.partnerId),calculationType:value.calculationType,calculationBasis:commissionBases.includes(value.calculationBasis)?value.calculationBasis:"NetSalePriceAfterDiscount",percentageRate:value.percentageRate?.toString()??"",fixedAmount:value.fixedAmount?.toString()??"",notes:value.manualReason??"",changeReason});setShowCommission(true);};
  const editRebate=(value:Rebate,changeReason:string)=>{setEditingRebate(value);setRebate({calculationType:value.calculationType,calculationBasis:rebateBases.includes(value.calculationBasis)?value.calculationBasis:"AgreedSalePrice",percentageRate:value.percentageRate?.toString()??"",fixedAmount:value.fixedAmount?.toString()??"",reason:value.reason,changeReason});setShowRebate(true);};
  const editCommissionWithReason=(value:Commission)=>{const reason=window.prompt("Reason for correcting this commission")?.trim();if(!reason)return;editCommission(value,reason);};
  const editRebateWithReason=(value:Rebate)=>{const reason=window.prompt("Reason for correcting this rebate")?.trim();if(!reason)return;editRebate(value,reason);};

  // Cancelling is the only status a person still sets by hand: a commission is pending from the
  // moment it is entered, and the payouts move it to Paid on their own.
  const cancelCommission=async(c:Commission)=>{const reason=window.prompt(`Reason for cancelling the commission for ${c.partnerName}`)?.trim();if(!reason)return;await execute(()=>commissionRebateApi.commissionStatus(bookingId,c.id,{targetStatus:"Cancelled",reason,concurrencyToken:c.concurrencyToken}));};
  const cancelRebate=async(r:Rebate)=>{const reason=window.prompt("Reason for cancelling this rebate")?.trim();if(!reason)return;await execute(()=>commissionRebateApi.rebateStatus(bookingId,r.id,{targetStatus:"Cancelled",reason,concurrencyToken:r.concurrencyToken}));};
  const recordPayout=async(e:FormEvent)=>{e.preventDefault();if(!payoutFor)return;await execute(async()=>{const result=await commissionRebateApi.payout(bookingId,payoutFor.id,{financeAccountId:Number(payout.financeAccountId),amount:Number(payout.amount),paymentDate:payout.paymentDate,paymentMethod:payout.paymentMethod,paymentReference:payout.paymentReference||null,idempotencyKey:payout.idempotencyKey,notes:payout.notes||null,commissionConcurrencyToken:payoutFor.concurrencyToken});setPayoutFor(null);return result;});};
  const recordDisbursement=async(e:FormEvent)=>{e.preventDefault();if(!disburseFor)return;await execute(async()=>{const cash=disbursement.method==="CashOrBankPayment",installment=disbursement.method==="InstallmentAdjustment";const result=await commissionRebateApi.disburseRebate(bookingId,disburseFor.id,{method:disbursement.method,amount:Number(disbursement.amount),appliedAt:disbursement.appliedAt,financeAccountId:cash?Number(disbursement.financeAccountId):null,installmentId:installment?Number(disbursement.installmentId):null,paymentMethod:cash?disbursement.paymentMethod:null,reference:disbursement.reference||null,idempotencyKey:disbursement.idempotencyKey,notes:disbursement.notes||null,rebateConcurrencyToken:disburseFor.concurrencyToken});setDisburseFor(null);return result;});};
  const openPayout=(c:Commission)=>{setPayoutFor(c);setPayout({financeAccountId:"",amount:String(c.outstandingAmount),paymentDate:pakistanToday(),paymentMethod:"BankTransfer",paymentReference:"",notes:"",idempotencyKey:idempotencyKey(`commission-${c.id}`)});};
  // The delivery method is locked by the first disbursement, so an already-started rebate opens on
  // the method it is being applied with and cannot switch mid-way.
  const openDisbursement=(r:Rebate)=>{setDisburseFor(r);setDisbursement({method:r.disbursements.length>0?r.method:"OutstandingBalanceReduction",amount:String(r.outstandingAmount),appliedAt:pakistanToday(),financeAccountId:"",installmentId:"",paymentMethod:"BankTransfer",reference:"",notes:"",idempotencyKey:idempotencyKey(`rebate-${r.id}`)});};
  const reversePayout=async(c:Commission,payoutId:number,available:number)=>{const value=window.prompt(`Amount to reverse (maximum ${available.toFixed(2)})`,available.toFixed(2));if(value===null)return;const amount=Number(value);if(!Number.isFinite(amount)||amount<=0){setError("Enter a valid reversal amount greater than zero.");return;}const reason=window.prompt("Reversal reason")?.trim();if(!reason)return;const operation=`commission:${payoutId}:${amount}:${reason}`;const key=reversalKeys.current.get(operation)??idempotencyKey(`commission-reversal-${payoutId}`);reversalKeys.current.set(operation,key);if(await execute(()=>commissionRebateApi.reversePayout(bookingId,c.id,payoutId,{amount,reason,idempotencyKey:key})))reversalKeys.current.delete(operation);};
  const reverseDisbursement=async(r:Rebate,id:number,available:number)=>{const value=window.prompt(`Amount to reverse (maximum ${available.toFixed(2)})`,available.toFixed(2));if(value===null)return;const amount=Number(value);if(!Number.isFinite(amount)||amount<=0){setError("Enter a valid reversal amount greater than zero.");return;}const reason=window.prompt("Reversal reason")?.trim();if(!reason)return;const operation=`rebate:${id}:${amount}:${reason}`;const key=reversalKeys.current.get(operation)??idempotencyKey(`rebate-reversal-${id}`);reversalKeys.current.set(operation,key);if(await execute(()=>commissionRebateApi.reverseDisbursement(bookingId,r.id,id,{amount,reason,idempotencyKey:key})))reversalKeys.current.delete(operation);};
  const upload=async(ownerType:string,ownerId:number,file:File|null)=>{if(!file)return;setBusy(true);setError(null);try{await commissionRebateApi.uploadEvidence(ownerType,ownerId,file);await load();}catch(x){setError(x instanceof Error?x.message:"Evidence could not be uploaded.");}finally{setBusy(false);}};

  if(loading)return <section className="mt-8 rounded-2xl border border-[var(--border)] p-8 text-center text-sm text-[var(--text-muted)]">Loading commissions and rebates…</section>;
  if(!workspace)return <section className="mt-8 rounded-2xl border border-rose-500/30 bg-rose-500/10 p-5 text-sm text-rose-300">{error??"Commission workspace unavailable."}<button className="ml-3 underline" onClick={()=>void load()}>Retry</button></section>;

  const percentCommission=commission.calculationType==="Percentage";
  const percentRebate=rebate.calculationType==="Percentage";
  const cashDisbursement=disbursement.method==="CashOrBankPayment";

  return <section className="mb-8 mt-8 space-y-6" aria-labelledby="commission-rebate-heading">
    <div className="grid gap-3 sm:grid-cols-3 lg:grid-cols-5">
      <Mini label="Sale price" value={money(workspace.agreedSalePrice)}/>
      <Mini label="Net sale price" value={money(workspace.netSalePrice)}/>
      <Mini label="Amount collected" value={money(workspace.amountCollected)}/>
      <Mini label="Rebate credits" value={money(workspace.rebateCredits)}/>
      <Mini label="Booking status" value={prettyEnum(workspace.bookingStatus)}/>
    </div>
    {error&&<p role="alert" className="rounded-xl bg-rose-500/10 p-3 text-sm text-rose-300">{error}</p>}

    <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5 sm:p-6">
      <SectionHeader id="commission-rebate-heading" title="Partner commissions" blurb="Each commission stays Pending until it is fully paid, then becomes Paid on its own."
        action={<Button size="sm" variant="outline" disabled={!active||busy||showCommission} onClick={openNewCommission}>+ Add Commission</Button>}/>
      {showCommission&&<EntryCard title={editingCommission?"Edit commission":"Commission entry"} index={editingCommission?workspace.commissions.findIndex(c=>c.id===editingCommission.id)+1:workspace.commissions.length+1}>
        <form onSubmit={saveCommission} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
              <span>Partner<span className="ml-0.5 text-rose-400">*</span></span>
              <div className="flex gap-2">
                <select required value={commission.partnerId} onChange={e=>setCommission({...commission,partnerId:e.target.value})} className={input}>
                  <option value="">Select partner</option>
                  {partnerOptions.map(p=><option key={p.id} value={String(p.id)}>{p.name} - {p.partnerType}</option>)}
                </select>
                <Button type="button" size="sm" variant="outline" className="shrink-0" disabled={busy} onClick={()=>setShowPartner(true)}>+ Add Partner</Button>
              </div>
            </div>
            <Segmented label="Commission Type" value={commission.calculationType} options={calculationTypes} onChange={v=>setCommission({...commission,calculationType:v as CalculationType,percentageRate:"",fixedAmount:""})}/>
          </div>
          {percentCommission
            ? <div className="grid gap-4 sm:grid-cols-3">
                <UnitField label="Percentage" required suffix="%" max="100" value={commission.percentageRate} onChange={v=>setCommission({...commission,percentageRate:v})}/>
                <BasisSelect label="Calculate On" value={commission.calculationBasis} options={commissionBases} onChange={v=>setCommission({...commission,calculationBasis:v})}/>
                <Readout label="Calculated Commission" value={money(commissionAmount)} tone="accent"/>
              </div>
            : <div className="grid gap-4 sm:grid-cols-3"><UnitField label="Amount" required prefix="Rs" value={commission.fixedAmount} onChange={v=>setCommission({...commission,fixedAmount:v})}/></div>}
          <Field label="Notes (optional)" as="textarea" placeholder="Add any notes about this commission (optional)" value={commission.notes} onChange={e=>setCommission({...commission,notes:e.target.value})}/>
          <FormButtons busy={busy} cancel={()=>{setShowCommission(false);setEditingCommission(null);}}/>
        </form>
      </EntryCard>}
      <div className="mt-5 space-y-4">
        {workspace.commissions.length===0&&!showCommission?<Empty text="No partner commissions on this booking yet."/>
          :workspace.commissions.map((c,i)=><CommissionCard key={c.id} value={c} index={i+1} busy={busy} cancel={()=>void cancelCommission(c)} edit={()=>editCommissionWithReason(c)} pay={()=>openPayout(c)} reverse={(id,amount)=>void reversePayout(c,id,amount)} upload={f=>void upload("Commission",c.id,f)} uploadMovement={(id,f)=>void upload("CommissionPayout",id,f)} onEvidenceError={setError}/>)}
      </div>
    </div>

    <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5 sm:p-6">
      <SectionHeader title="Rebates" blurb="A rebate stays Pending until it has all reached the customer."
        action={<Button size="sm" variant="outline" disabled={!active||busy||showRebate||liveRebate} onClick={openNewRebate}>+ Add Rebate</Button>}/>
      {liveRebate&&!showRebate&&<p className="mt-3 text-xs text-[var(--text-muted)]">A booking carries one live rebate. Cancel or reverse the one below before adding another.</p>}
      {showRebate&&<EntryCard title={editingRebate?"Edit rebate":"Rebate entry"} index={editingRebate?workspace.rebates.findIndex(r=>r.id===editingRebate.id)+1:workspace.rebates.length+1}>
        <form onSubmit={saveRebate} className="space-y-4">
          {percentRebate
            ? <div className="grid gap-4 sm:grid-cols-4">
                <Segmented label="Rebate Type" value={rebate.calculationType} options={calculationTypes} onChange={v=>setRebate({...rebate,calculationType:v as CalculationType,percentageRate:"",fixedAmount:""})}/>
                <UnitField label="Percentage" required suffix="%" max="100" value={rebate.percentageRate} onChange={v=>setRebate({...rebate,percentageRate:v})}/>
                <BasisSelect label="Calculate On" value={rebate.calculationBasis} options={rebateBases} onChange={v=>setRebate({...rebate,calculationBasis:v})}/>
                <Field label="Reason (optional)" placeholder="Enter reason for rebate" value={rebate.reason} onChange={e=>setRebate({...rebate,reason:e.target.value})}/>
              </div>
            : <div className="grid gap-4 sm:grid-cols-3">
                <Segmented label="Rebate Type" value={rebate.calculationType} options={calculationTypes} onChange={v=>setRebate({...rebate,calculationType:v as CalculationType,percentageRate:"",fixedAmount:""})}/>
                <UnitField label="Amount" required prefix="Rs" value={rebate.fixedAmount} onChange={v=>setRebate({...rebate,fixedAmount:v})}/>
                <Field label="Reason (optional)" placeholder="Enter reason for rebate" value={rebate.reason} onChange={e=>setRebate({...rebate,reason:e.target.value})}/>
              </div>}
          <div className={`grid gap-4 ${percentRebate?"sm:grid-cols-2":""}`}>
            {percentRebate&&<Readout label="Calculated Rebate" value={money(rebateAmount)}/>}
            <Readout label="Net Sale Price After Rebate" value={money(netAfterRebate)} tone="hero"/>
          </div>
          <FormButtons busy={busy} cancel={()=>{setShowRebate(false);setEditingRebate(null);}}/>
        </form>
      </EntryCard>}
      <div className="mt-5 space-y-4">
        {workspace.rebates.length===0&&!showRebate?<Empty text="No rebate on this booking yet."/>
          :workspace.rebates.map((r,i)=><RebateCard key={r.id} value={r} index={i+1} netSalePrice={workspace.netSalePrice} busy={busy} cancel={()=>void cancelRebate(r)} edit={()=>editRebateWithReason(r)} disburse={()=>openDisbursement(r)} reverse={(id,amount)=>void reverseDisbursement(r,id,amount)} upload={f=>void upload("Rebate",r.id,f)} uploadMovement={(id,f)=>void upload("RebateDisbursement",id,f)} onEvidenceError={setError}/>)}
      </div>
    </div>

    <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5 sm:p-6">
      <button className="font-semibold text-[var(--accent)]" onClick={()=>setShowAudit(v=>!v)}>{showAudit?"Hide":"Show"} audit history ({workspace.audit.length+olderAudit.length}{workspace.hasMoreAudit||auditHasMore?"+":""})</button>
      {showAudit&&<div className="mt-3 max-h-80 space-y-2 overflow-y-auto">{[...workspace.audit,...olderAudit].map(a=><div key={a.id} className="rounded-xl border border-[var(--border)] p-3 text-sm"><div className="flex justify-between gap-3"><strong>{prettyEnum(a.action)}</strong><time className="text-xs text-[var(--text-muted)]">{new Date(a.occurredAt).toLocaleString()}</time></div><p className="mt-1 text-xs text-[var(--text-muted)]">{a.performedByName??"Admin"}{a.reason?` · ${a.reason}`:""}</p></div>)}{auditHasMore&&<div className="pt-1 text-center"><button className="text-xs font-semibold text-[var(--accent)] underline disabled:opacity-40" disabled={auditBusy} onClick={()=>void loadMoreAudit()}>{auditBusy?"Loading…":"Load older activity"}</button></div>}</div>}
    </div>

    {showPartner&&<Dialog title="Add Partner" close={()=>!busy&&setShowPartner(false)}>
      <form onSubmit={savePartner} className="space-y-3">
        <Field label="Name" required placeholder="Partner name" value={partnerForm.name} onChange={e=>setPartnerForm({...partnerForm,name:e.target.value})}/>
        <Select label="Type" value={partnerForm.partnerType} set={v=>setPartnerForm({...partnerForm,partnerType:v})} options={partnerTypes.map(v=>({v,n:v}))}/>
        <Field label="Phone" placeholder="0300-1234567" value={partnerForm.phone} onChange={e=>setPartnerForm({...partnerForm,phone:e.target.value})}/>
        <Field label="Email (optional)" type="email" placeholder="Enter email address" value={partnerForm.email} onChange={e=>setPartnerForm({...partnerForm,email:e.target.value})}/>
        <FormButtons busy={busy} cancel={()=>setShowPartner(false)} submitText="Save Partner"/>
      </form>
    </Dialog>}

    {payoutFor&&<Dialog title={`Pay ${payoutFor.partnerName}`} close={()=>!busy&&setPayoutFor(null)}><form onSubmit={recordPayout} className="space-y-3"><p className="text-sm text-[var(--text-muted)]">Remaining {money(payoutFor.outstandingAmount)}. The selected finance account will record an outgoing transaction.</p><Select label="Finance account" required value={payout.financeAccountId} set={v=>setPayout({...payout,financeAccountId:v})} options={[{v:"",n:"Select account"},...accounts.map(a=>({v:String(a.id),n:`${a.name} · ${a.accountHolderName}`}))]}/><Field label="Amount" required type="number" min="0.01" max={payoutFor.outstandingAmount} step="0.01" value={payout.amount} onChange={e=>setPayout({...payout,amount:e.target.value})}/><Field label="Payment date" required type="date" max={pakistanToday()} value={payout.paymentDate} onChange={e=>setPayout({...payout,paymentDate:e.target.value})}/><Select label="Method" value={payout.paymentMethod} set={v=>setPayout({...payout,paymentMethod:v})} options={paymentMethods}/><Field label="Reference" required={payout.paymentMethod!=="Cash"} value={payout.paymentReference} onChange={e=>setPayout({...payout,paymentReference:e.target.value})}/><Field label="Notes" value={payout.notes} onChange={e=>setPayout({...payout,notes:e.target.value})}/><FormButtons busy={busy} cancel={()=>setPayoutFor(null)}/></form></Dialog>}

    {disburseFor&&<Dialog title="Apply or pay rebate" close={()=>!busy&&setDisburseFor(null)}>
      <form onSubmit={recordDisbursement} className="space-y-3">
        <p className="text-sm text-[var(--text-muted)]">Remaining {money(disburseFor.outstandingAmount)}.</p>
        <Select label="How the customer receives it" value={disbursement.method} disabled={disburseFor.disbursements.length>0}
          set={v=>setDisbursement({...disbursement,method:v as RebateMethod,financeAccountId:"",installmentId:""})} options={rebateMethods.map(v=>({v,n:prettyEnum(v)}))}/>
        {disburseFor.disbursements.length>0&&<p className="text-xs text-[var(--text-muted)]">The method was set by the first entry and stays the same for the rest of this rebate.</p>}
        <Field label="Amount" required type="number" min="0.01" max={disburseFor.outstandingAmount} step="0.01" value={disbursement.amount} onChange={e=>setDisbursement({...disbursement,amount:e.target.value})}/>
        <Field label="Applied date" required type="date" max={pakistanToday()} value={disbursement.appliedAt} onChange={e=>setDisbursement({...disbursement,appliedAt:e.target.value})}/>
        {cashDisbursement&&<><Select label="Finance account" required value={disbursement.financeAccountId} set={v=>setDisbursement({...disbursement,financeAccountId:v})} options={[{v:"",n:"Select account"},...accounts.map(a=>({v:String(a.id),n:a.name}))]}/><Select label="Payment method" value={disbursement.paymentMethod} set={v=>setDisbursement({...disbursement,paymentMethod:v})} options={paymentMethods}/></>}
        {disbursement.method==="InstallmentAdjustment"&&<Select label="Installment" required value={disbursement.installmentId} set={v=>setDisbursement({...disbursement,installmentId:v})} options={[{v:"",n:"Select installment"},...installments.filter(i=>i.remainingBalance>0).map(i=>({v:String(i.id),n:`${i.type} ${i.sequenceNumber} · ${money(i.remainingBalance)}`}))]}/>}
        <Field label="Reference" required={disbursement.method==="CreditNote"||(cashDisbursement&&disbursement.paymentMethod!=="Cash")} value={disbursement.reference} onChange={e=>setDisbursement({...disbursement,reference:e.target.value})}/>
        <Field label={disbursement.method==="Other"?"Method explanation":"Notes"} required={disbursement.method==="Other"} value={disbursement.notes} onChange={e=>setDisbursement({...disbursement,notes:e.target.value})}/>
        <FormButtons busy={busy} cancel={()=>setDisburseFor(null)}/>
      </form>
    </Dialog>}
  </section>;
}

function emptyCommissionForm(){return {partnerId:"",calculationType:"Percentage" as CalculationType,calculationBasis:"NetSalePriceAfterDiscount" as CalculationBasis,percentageRate:"",fixedAmount:"",notes:"",changeReason:""};}
function emptyRebateForm(){return {calculationType:"FixedAmount" as CalculationType,calculationBasis:"AgreedSalePrice" as CalculationBasis,percentageRate:"",fixedAmount:"",reason:"",changeReason:""};}
function emptyPartnerForm(){return {name:"",partnerType:"Dealer",phone:"",email:""};}
const describe=(type:CalculationType,rate:number|null,fixed:number|null,basis:CalculationBasis)=>
  type==="Percentage"?`${rate??0}% of ${basisNames[basis]??prettyEnum(basis)}`:`Fixed amount ${money(fixed??0)}`;

function SectionHeader({id,title,blurb,action}:{id?:string;title:string;blurb:string;action:ReactNode}){
  return <div className="flex flex-wrap items-start justify-between gap-3">
    <div><h2 id={id} className="text-xl font-semibold text-[var(--text-heading)]">{title}</h2><p className="mt-1 text-sm text-[var(--text-muted)]">{blurb}</p></div>
    {action}
  </div>;
}

function EntryCard({title,index,children}:{title:string;index:number;children:ReactNode}){
  return <div className="mt-5 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-4 sm:p-5">
    <div className="mb-4 flex items-center justify-between gap-3"><h3 className="font-semibold text-[var(--text-heading)]">{title}</h3><span className="rounded-full border border-[var(--border)] px-2 py-0.5 text-xs text-[var(--text-muted)]">#{index}</span></div>
    {children}
  </div>;
}

function Segmented({label,value,options,onChange}:{label:string;value:string;options:{v:string;n:string}[];onChange:(v:string)=>void}){
  return <div className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
    <span>{label}</span>
    <div role="group" aria-label={label} className="grid grid-cols-2 gap-1 rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-1">
      {options.map(o=><button key={o.v} type="button" aria-pressed={value===o.v} onClick={()=>onChange(o.v)}
        className={`rounded-lg px-3 py-2 text-sm font-semibold transition-colors ${value===o.v?"bg-[var(--accent-glow-strong)] text-[var(--accent-warm)]":"text-[var(--text-muted)] hover:text-[var(--text-primary)]"}`}>{o.n}</button>)}
    </div>
  </div>;
}

function UnitField({label,value,onChange,prefix,suffix,required=false,max}:{label:string;value:string;onChange:(v:string)=>void;prefix?:string;suffix?:string;required?:boolean;max?:string}){
  return <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
    <span>{label}{required&&<span className="ml-0.5 text-rose-400">*</span>}</span>
    <span className="flex items-center gap-2 rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 focus-within:border-[var(--accent)]">
      {prefix&&<span className="shrink-0 text-sm text-[var(--text-muted)]">{prefix}</span>}
      <input type="number" min="0.000001" step="any" max={max} required={required} value={value} placeholder="0" onChange={e=>onChange(e.target.value)}
        className="w-full bg-transparent py-3 text-sm text-[var(--text-primary)] focus:outline-none"/>
      {suffix&&<span className="shrink-0 text-sm text-[var(--text-muted)]">{suffix}</span>}
    </span>
  </label>;
}

function BasisSelect({label,value,options,onChange}:{label:string;value:CalculationBasis;options:CalculationBasis[];onChange:(v:CalculationBasis)=>void}){
  return <Select label={label} value={value} set={v=>onChange(v as CalculationBasis)} options={options.map(v=>({v,n:basisNames[v]??prettyEnum(v)}))}/>;
}

function Readout({label,value,tone="plain"}:{label:string;value:string;tone?:"plain"|"accent"|"hero"}){
  const style=tone==="hero"?"bg-[var(--accent-glow)] py-3.5 text-xl font-semibold text-[var(--accent-warm)]"
    :tone==="accent"?"bg-[var(--input-bg)] py-3 text-base font-semibold text-[var(--accent-warm)]"
    :"bg-[var(--input-bg)] py-3 text-sm text-[var(--text-primary)]";
  return <div className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
    <span>{label}</span>
    <output className={`rounded-xl border border-[var(--border)] px-4 ${style}`}>{value}</output>
  </div>;
}

function Select({label,value,set,options,required=false,disabled=false}:{label:string;value:string;set:(v:string)=>void;options:{v:string;n:string}[];required?:boolean;disabled?:boolean}){return <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">{label}<select required={required} disabled={disabled} value={value} onChange={e=>set(e.target.value)} className={input}>{options.map(o=><option key={o.v} value={o.v}>{o.n}</option>)}</select></label>}
function Mini({label,value}:{label:string;value:string}){return <div className="rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3"><p className="text-[10px] uppercase tracking-wide text-[var(--text-muted)]">{label}</p><p className="mt-1 font-semibold text-[var(--text-heading)]">{value}</p></div>}
function FormButtons({busy,cancel,submitText="Save"}:{busy:boolean;cancel:()=>void;submitText?:string}){return <div className="flex justify-end gap-2"><Button type="button" size="sm" variant="ghost" onClick={cancel} disabled={busy}>Cancel</Button><Button type="submit" size="sm" disabled={busy}>{busy?"Saving…":submitText}</Button></div>}
function Empty({text}:{text:string}){return <p className="rounded-xl border border-dashed border-[var(--border)] p-5 text-center text-sm text-[var(--text-muted)]">{text}</p>}
function Dialog({title,close,children}:{title:string;close:()=>void;children:ReactNode}){return <div className="fixed inset-0 z-50 flex items-center justify-center p-4"><button aria-label="Close dialog" className="absolute inset-0 bg-black/70" onClick={close}/><section autoFocus tabIndex={-1} onKeyDown={e=>trapDialogKeys(e,close)} role="dialog" aria-modal="true" aria-label={title} className="relative max-h-[90dvh] w-full max-w-lg overflow-y-auto rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6"><div className="mb-4 flex justify-between"><h3 className="text-lg font-semibold">{title}</h3><button aria-label="Close" onClick={close}>✕</button></div>{children}</section></div>}
function EvidenceList({items,onError}:{items:{id:number;originalFileName:string;fileSize:number}[];onError:(message:string)=>void}){const open=async(id:number)=>{try{await commissionRebateApi.openEvidence(id);}catch(x){onError(x instanceof Error?x.message:"Evidence could not be opened.");}};return <div className="mt-2 flex flex-wrap gap-2">{items.map(e=><button key={e.id} type="button" onClick={()=>void open(e.id)} className="max-w-full truncate rounded-full border border-indigo-500/20 bg-indigo-500/10 px-2.5 py-1 text-xs text-indigo-300" title={e.originalFileName}>{e.originalFileName} · {(e.fileSize/1024).toFixed(0)} KB</button>)}</div>}
function MovementEvidence({id,items,busy,upload,onError}:{id:number;items:{id:number;originalFileName:string;fileSize:number}[];busy:boolean;upload:(id:number,file:File|null)=>void;onError:(message:string)=>void}){return <div><label title="PDF, image, Word, or Excel; maximum 15 MB" className="cursor-pointer text-xs text-[var(--accent)] underline">Attach proof<input className="sr-only" type="file" accept={evidenceAccept} disabled={busy} onChange={e=>upload(id,e.target.files?.[0]??null)}/></label><EvidenceList items={items} onError={onError}/></div>}

function CommissionCard({value:c,index,busy,cancel,edit,pay,reverse,upload,uploadMovement,onEvidenceError}:{value:Commission;index:number;busy:boolean;cancel:()=>void;edit:()=>void;pay:()=>void;reverse:(id:number,amount:number)=>void;upload:(f:File|null)=>void;uploadMovement:(id:number,f:File|null)=>void;onEvidenceError:(message:string)=>void}){
  const a=commissionActions(c.status,c.paidAmount);
  // Correcting the agreed figures is only honest while none of it has been paid; after that the
  // payout has to be reversed first, which is the same rule the server enforces.
  const untouched=c.payouts.length===0;
  return <article className="rounded-xl border border-[var(--border)] bg-[var(--surface)] p-4">
    <div className="flex flex-wrap justify-between gap-3">
      <div>
        <h4 className="font-semibold">{c.partnerName} <Status value={c.status}/></h4>
        <p className="mt-1 text-xs text-[var(--text-muted)]">
          {c.ruleNameSnapshot??describe(c.calculationType,c.percentageRate,c.fixedAmount,c.calculationBasis)}
          {c.allocationPercent!==100&&` · ${c.allocationPercent}% allocation`} · calculated {money(c.calculatedAmount)}
        </p>
        {c.manualReason&&<p className="mt-1 text-xs text-[var(--text-muted)]">{c.manualReason}</p>}
        {c.cancellationOrReversalReason&&<p className="mt-1 text-xs text-rose-300">{c.cancellationOrReversalReason}</p>}
      </div>
      <div className="flex items-start gap-4">
        <div className="grid grid-cols-2 gap-4 text-right text-sm sm:grid-cols-4"><Amount label="Commission" value={c.finalAmount}/><Amount label="Paid" value={c.paidAmount}/><Amount label="Remaining" value={c.outstandingAmount}/>{c.recoveryRequiredAmount>0&&<Amount label="Recovery due" value={c.recoveryRequiredAmount}/>}</div>
        <span className="rounded-full border border-[var(--border)] px-2 py-0.5 text-xs text-[var(--text-muted)]">#{index}</span>
      </div>
    </div>
    <div className="mt-3 flex flex-wrap gap-2">{a.canPay&&<Action text="Record payment" onClick={pay} disabled={busy}/>} {a.canEdit&&untouched&&<Action text="Edit" onClick={edit} disabled={busy}/>} {a.canCancel&&untouched&&<Action text="Cancel" onClick={cancel} disabled={busy}/>}<label className="cursor-pointer rounded-lg border border-[var(--border)] px-3 py-1.5 text-xs">Attach proof (optional)<input className="sr-only" type="file" accept=".pdf,.jpg,.jpeg,.png" disabled={busy} onChange={e=>upload(e.target.files?.[0]??null)}/></label></div>
    <EvidenceList items={c.evidence} onError={onEvidenceError}/>
    {c.payouts.length>0&&<div className="mt-3 space-y-2">{c.payouts.map(p=><div key={p.id} className="rounded-lg border border-[var(--border)] p-2 text-xs text-[var(--text-muted)]"><div className="flex flex-wrap justify-between gap-2"><span>{new Date(p.date).toLocaleDateString()} · {p.financeAccountName} · {p.reference??prettyEnum(p.paymentMethod??"")}</span><span>Original {money(p.amount)}{p.reversedAmount>0?` · Reversed ${money(p.reversedAmount)} · Net ${money(p.amount-p.reversedAmount)}`:""} {p.amount>p.reversedAmount&&<button className="ml-2 text-rose-300 underline" disabled={busy} onClick={()=>reverse(p.id,p.amount-p.reversedAmount)}>Reverse</button>}</span></div><MovementEvidence id={p.id} items={p.evidence} busy={busy} upload={uploadMovement} onError={onEvidenceError}/></div>)}</div>}
  </article>;
}

function RebateCard({value:r,index,netSalePrice,busy,cancel,edit,disburse,reverse,upload,uploadMovement,onEvidenceError}:{value:Rebate;index:number;netSalePrice:number;busy:boolean;cancel:()=>void;edit:()=>void;disburse:()=>void;reverse:(id:number,amount:number)=>void;upload:(f:File|null)=>void;uploadMovement:(id:number,f:File|null)=>void;onEvidenceError:(message:string)=>void}){
  const a=rebateActions(r.status,r.appliedOrPaidAmount);
  const settled=r.finalAmount;
  const untouched=r.disbursements.length===0;
  return <article className="rounded-xl border border-[var(--border)] bg-[var(--surface)] p-4">
    <div className="flex flex-wrap justify-between gap-3">
      <div>
        <h4 className="font-semibold">{r.customerName} <Status value={r.status}/></h4>
        <p className="mt-1 text-xs text-[var(--text-muted)]">
          {describe(r.calculationType,r.percentageRate,r.fixedAmount,r.calculationBasis)} · net sale price after rebate {money(Math.max(0,netSalePrice-settled))}
        </p>
        <p className="mt-1 text-xs text-[var(--text-muted)]">{r.reason} · {r.disbursements.length>0?prettyEnum(r.method):"Method chosen when applied"}</p>
        {r.cancellationOrReversalReason&&<p className="mt-1 text-xs text-rose-300">{r.cancellationOrReversalReason}</p>}
      </div>
      <div className="flex items-start gap-4">
        <div className="grid grid-cols-2 gap-4 text-right text-sm sm:grid-cols-4"><Amount label="Rebate" value={settled}/><Amount label="Given" value={r.appliedOrPaidAmount}/><Amount label="Remaining" value={r.outstandingAmount}/>{r.recoveryRequiredAmount>0&&<Amount label="Recovery due" value={r.recoveryRequiredAmount}/>}</div>
        <span className="rounded-full border border-[var(--border)] px-2 py-0.5 text-xs text-[var(--text-muted)]">#{index}</span>
      </div>
    </div>
    <div className="mt-3 flex flex-wrap gap-2">{a.canDisburse&&<Action text="Apply / pay" onClick={disburse} disabled={busy}/>} {a.canEdit&&untouched&&<Action text="Edit" onClick={edit} disabled={busy}/>} {a.canCancel&&untouched&&<Action text="Cancel" onClick={cancel} disabled={busy}/>}<label className="cursor-pointer rounded-lg border border-[var(--border)] px-3 py-1.5 text-xs">Attach proof (optional)<input className="sr-only" type="file" accept=".pdf,.jpg,.jpeg,.png" disabled={busy} onChange={e=>upload(e.target.files?.[0]??null)}/></label></div>
    <EvidenceList items={r.evidence} onError={onEvidenceError}/>
    {r.disbursements.length>0&&<div className="mt-3 space-y-2">{r.disbursements.map(d=><div key={d.id} className="rounded-lg border border-[var(--border)] p-2 text-xs text-[var(--text-muted)]"><div className="flex flex-wrap justify-between gap-2"><span>{new Date(d.date).toLocaleDateString()} · {prettyEnum(d.rebateMethod??r.method)} · {d.reference??"No reference"}</span><span>Original {money(d.amount)}{d.reversedAmount>0?` · Reversed ${money(d.reversedAmount)} · Net ${money(d.amount-d.reversedAmount)}`:""} {d.amount>d.reversedAmount&&<button className="ml-2 text-rose-300 underline" disabled={busy} onClick={()=>reverse(d.id,d.amount-d.reversedAmount)}>Reverse</button>}</span></div><MovementEvidence id={d.id} items={d.evidence} busy={busy} upload={uploadMovement} onError={onEvidenceError}/></div>)}</div>}
  </article>;
}

function Action({text,...props}:{text:string}&ButtonHTMLAttributes<HTMLButtonElement>){return <button type="button" {...props} className="rounded-lg border border-[var(--border)] px-3 py-1.5 text-xs font-semibold text-[var(--accent)] disabled:opacity-40">{text}</button>}
function Amount({label,value}:{label:string;value:number}){return <span><small className="block text-[var(--text-muted)]">{label}</small><strong>{money(value)}</strong></span>}
function Status({value}:{value:string}){
  const tone=value.includes("Reversal")||value==="Cancelled"?"border-rose-500/30 text-rose-300"
    :isPendingStatus(value)?"border-amber-500/30 text-amber-300":"border-emerald-500/30 text-emerald-300";
  return <span className={`ml-2 rounded-full border px-2 py-0.5 text-[10px] ${tone}`}>{prettyEnum(value)}</span>;
}
