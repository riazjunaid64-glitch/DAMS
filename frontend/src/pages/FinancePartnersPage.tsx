import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { User } from "../App";
import { api } from "../api/api";
import Button from "../lib/Button";
import Container from "../lib/Container";
import FinanceAttachmentField from "../components/FinanceAttachmentField";
import { financeApiError, openAttachmentAt, type FinanceAttachmentInfo } from "../api/financeAttachments";
import { pakistanToday } from "../lib/financePeriods";
import { moneyRequest, useIdempotencyKeys } from "../lib/idempotency";

type Partner = { id:number; name:string; cnic:string|null; ntn:string|null; profitSharePercent:number; financeAccountId:number|null; financeAccountName:string|null; isActive:boolean; joinedDate:string|null; exitedDate:string|null; openingBalance:number; contributions:number; withdrawals:number; profitShare:number; lossShare:number; closingBalance:number; concurrencyToken:string };
type CashAccount = { id:number; name:string; accountHolderName:string; isActive:boolean };
type CapitalAccount = CashAccount & { type:string|number };
type Transaction = { id:number; type:string; amount:number; date:string; financeAccountId:number|null; reference:string|null; note:string|null; profitSharePercentSnapshot:number|null; attachment:FinanceAttachmentInfo|null };
type Statement = { partnerId:number; partnerName:string; from:string|null; to:string|null; openingBalance:number; transactions:Transaction[]; closingBalance:number };
type PartnerForm = { id:number|null; name:string; cnic:string; ntn:string; profitSharePercent:string; financeAccountId:string; joinedDate:string; exitedDate:string; concurrencyToken:string };
const TYPES = [ ["Contribution","Contribution"], ["Withdrawal","Withdrawal"], ["ProfitShare","Profit share"], ["LossShare","Loss share"] ];
const money = (value:number) => `Rs ${value.toLocaleString("en-PK", { maximumFractionDigits:2 })}`;
// The Pakistani calendar day, like every other finance screen: the browser's own date is a day ahead
// for anyone east of PKT, and the server rejects a capital movement dated in the future.
const today = pakistanToday;

export default function FinancePartnersPage({user}:{user:User|null}) {
  const navigate=useNavigate();
  const [partners,setPartners]=useState<Partner[]>([]), [accounts,setAccounts]=useState<CashAccount[]>([]);
  const [capitalAccounts,setCapitalAccounts]=useState<CapitalAccount[]>([]), [activeStates,setActiveStates]=useState<Record<number,boolean>>({});
  const [loading,setLoading]=useState(true), [error,setError]=useState<string|null>(null), [savingShares,setSavingShares]=useState(false);
  const [shares,setShares]=useState<Record<number,string>>({});
  const [partnerForm,setPartnerForm]=useState<PartnerForm|null>(null), [savingPartner,setSavingPartner]=useState(false);
  const [transactionPartner,setTransactionPartner]=useState<Partner|null>(null);
  // A statement is its own small screen: the partner it belongs to, the period being asked for and
  // the answer the server gave for that period are kept apart, so re-running it for a new range
  // leaves the modal open with the previous figures on screen until the new ones arrive.
  const [statementPartner,setStatementPartner]=useState<Partner|null>(null), [statement,setStatement]=useState<Statement|null>(null);
  const [statementFrom,setStatementFrom]=useState(""), [statementTo,setStatementTo]=useState("");
  const [statementLoading,setStatementLoading]=useState(false), [statementError,setStatementError]=useState<string|null>(null);
  // Two ranges applied in quick succession must not land out of order: only the newest request may
  // write its result, so a slow earlier reply cannot repaint the modal with a period nobody asked for.
  const statementRequest=useRef(0);
  const [recordingTransaction,setRecordingTransaction]=useState(false);
  const idempotency=useIdempotencyKeys();
  const [form,setForm]=useState({type:"Contribution",amount:"",date:today(),financeAccountId:"",reference:"",note:""});
  // Only ever a new file here: capital movements are recorded, never corrected, so there is no
  // existing attachment to replace — the field's other states would have nothing to describe.
  const [selectedAttachment,setSelectedAttachment]=useState<File|null>(null);
  const viewAttachment=async(partnerId:number,row:Transaction,download:boolean)=>{
    if(!row.attachment)return;
    try {
      await openAttachmentAt(`/api/finance/partners/${partnerId}/transactions/${row.id}/attachment`,row.attachment.fileName,download);
    } catch(caught) { setStatementError(caught instanceof Error?caught.message:"The attachment could not be opened."); }
  };

  const load=useCallback(async()=>{setLoading(true);setError(null);try{const [partnerResponse,accountResponse,allAccountResponse]=await Promise.all([api("/api/finance/partners?includeInactive=true"),api("/api/finance/accounts/options?includeInactive=true&cashLikeOnly=true"),api("/api/finance/accounts/options?includeInactive=true&cashLikeOnly=false")]);if(!partnerResponse.ok)throw new Error((await partnerResponse.json().catch(()=>null))?.message??"Partners could not be loaded.");const rows=await partnerResponse.json() as Partner[];setPartners(rows);setShares(Object.fromEntries(rows.map(row=>[row.id,String(row.profitSharePercent)])));setActiveStates(Object.fromEntries(rows.map(row=>[row.id,row.isActive])));if(accountResponse.ok)setAccounts(await accountResponse.json());if(allAccountResponse.ok){const all=await allAccountResponse.json() as CapitalAccount[];setCapitalAccounts(all.filter(account=>account.type==="Capital"||Number(account.type)===6));}}catch(caught){setError(caught instanceof Error?caught.message:"Partners could not be loaded.");}finally{setLoading(false);}},[]);
  useEffect(()=>{if(user?.role!=="Admin"){navigate("/");return;}void load();},[user,navigate,load]);

  const total=partners.filter(partner=>activeStates[partner.id]).reduce((sum,partner)=>sum+(Number(shares[partner.id])||0),0);
  // Contributions and withdrawals name the bank account the money moved through; the statement
  // reads that name back from the options already loaded for the transaction form.
  const cashAccountNames=useMemo(()=>new Map(accounts.map(account=>[account.id,account.name])),[accounts]);
  const saveShares=async()=>{setSavingShares(true);setError(null);try{const response=await api("/api/finance/partners/shares",{method:"PUT",body:JSON.stringify({partners:partners.map(partner=>({id:partner.id,profitSharePercent:Number(shares[partner.id]),isActive:Boolean(activeStates[partner.id]),concurrencyToken:partner.concurrencyToken}))})});if(!response.ok)throw new Error((await response.json().catch(()=>null))?.message??"Shares could not be saved.");await load();}catch(caught){setError(caught instanceof Error?caught.message:"Shares could not be saved.");}finally{setSavingShares(false);}};
  const savePartner=async()=>{if(!partnerForm)return;const share=Number(partnerForm.profitSharePercent);if(!partnerForm.name.trim()||!Number.isFinite(share)||share<0||share>100){setError("Enter a partner name and a share between 0% and 100%.");return;}setSavingPartner(true);setError(null);try{const existing=partnerForm.id?partners.find(row=>row.id===partnerForm.id):null;const response=await api(partnerForm.id?`/api/finance/partners/${partnerForm.id}`:"/api/finance/partners",{method:partnerForm.id?"PUT":"POST",body:JSON.stringify({name:partnerForm.name.trim(),cnic:partnerForm.cnic.trim()||null,ntn:partnerForm.ntn.trim()||null,profitSharePercent:share,financeAccountId:Number(partnerForm.financeAccountId)||null,isActive:existing?.isActive??false,joinedDate:partnerForm.joinedDate||null,exitedDate:partnerForm.exitedDate||null,concurrencyToken:partnerForm.concurrencyToken})});if(!response.ok)throw new Error((await response.json().catch(()=>null))?.message??"Partner could not be saved.");setPartnerForm(null);await load();}catch(caught){setError(caught instanceof Error?caught.message:"Partner could not be saved.");}finally{setSavingPartner(false);}};
  const loadStatement=useCallback(async(partner:Partner,from:string,to:string)=>{
    if(from&&to&&from>to){setStatementError("The From date cannot be after the To date.");return;}
    const ticket=++statementRequest.current;
    setStatementLoading(true);setStatementError(null);
    try{
      const query=new URLSearchParams();
      if(from)query.set("from",from);
      if(to)query.set("to",to);
      const suffix=query.toString();
      const response=await api(`/api/finance/partners/${partner.id}/statement${suffix?`?${suffix}`:""}`);
      if(!response.ok)throw new Error((await response.json().catch(()=>null))?.message??"Statement could not be loaded.");
      const loaded=await response.json() as Statement;
      if(ticket!==statementRequest.current)return;
      setStatement(loaded);
    }catch(caught){
      if(ticket!==statementRequest.current)return;
      setStatementError(caught instanceof Error?caught.message:"Statement could not be loaded.");
    }finally{
      if(ticket===statementRequest.current)setStatementLoading(false);
    }
  },[]);
  const openStatement=(partner:Partner)=>{setError(null);setStatement(null);setStatementError(null);setStatementFrom("");setStatementTo("");setStatementPartner(partner);void loadStatement(partner,"","");};
  const closeStatement=()=>{statementRequest.current++;setStatementPartner(null);setStatement(null);setStatementError(null);setStatementLoading(false);};
  const saveTransaction=async()=>{if(!transactionPartner||recordingTransaction)return;const amount=Number(form.amount);if(!Number.isFinite(amount)||amount<=0){setError("Enter an amount greater than zero.");return;}const movesCash=form.type==="Contribution"||form.type==="Withdrawal";if(movesCash&&!form.financeAccountId){setError("Choose the cash or bank account used.");return;}setRecordingTransaction(true);setError(null);try{const signature=`capital:${transactionPartner.id}:${form.type}:${amount}:${form.date}`;const body=new FormData();body.append("type",form.type);body.append("amount",String(amount));body.append("date",form.date);if(movesCash&&form.financeAccountId)body.append("financeAccountId",form.financeAccountId);if(form.reference)body.append("reference",form.reference);if(form.note)body.append("note",form.note);if(selectedAttachment)body.append("attachment",selectedAttachment);const response=await api(`/api/finance/partners/${transactionPartner.id}/transactions/form`,moneyRequest(idempotency.key(signature,"capital-movement"),{method:"POST",body}));if(!response.ok)throw new Error(await financeApiError(response,"Transaction could not be saved."));idempotency.release(signature);setTransactionPartner(null);setSelectedAttachment(null);setForm({type:"Contribution",amount:"",date:today(),financeAccountId:"",reference:"",note:""});await load();}catch(caught){setError(caught instanceof Error?caught.message:"Transaction could not be saved.");}finally{setRecordingTransaction(false);}};

  if(user?.role!=="Admin")return null;
  return <Container className="py-8"><div className="mb-6 flex flex-wrap items-end justify-between gap-3"><div><Link to="/finance/reports" className="text-sm text-[var(--accent)]">← Financial reports</Link><h1 className="mt-1 text-3xl font-bold text-[var(--text-heading)]">Capital Partners</h1><p className="text-sm text-[var(--text-muted)]">Partner capital, frozen distribution shares, and double-sided bank movements.</p></div><div className="flex gap-2"><Link to="/finance/accounts"><Button variant="outline">Capital Accounts</Button></Link><Button onClick={()=>setPartnerForm({id:null,name:"",cnic:"",ntn:"",profitSharePercent:"0",financeAccountId:"",joinedDate:"",exitedDate:"",concurrencyToken:""})}>Add partner</Button></div></div>
  {error&&<p className="mb-4 rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">{error}</p>}
  <div className={`mb-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border p-4 ${Math.abs(total-100)<=.01?"border-emerald-500/30 bg-emerald-500/5":"border-rose-500/40 bg-rose-500/10"}`}><div><p className="font-semibold">Active profit shares: {total.toFixed(4)}%</p><p className="text-xs text-[var(--text-muted)]">All active partners must total 100%. Historical ProfitShare transactions keep their original percentage snapshot.</p></div><Button disabled={savingShares||Math.abs(total-100)>.01} onClick={()=>void saveShares()}>{savingShares?"Saving…":"Save shares"}</Button></div>
  <div className="overflow-x-auto rounded-2xl border border-[var(--border)] bg-[var(--surface)]"><table className="w-full min-w-[1200px] text-sm"><thead><tr className="border-b border-[var(--border)] text-left text-[var(--text-muted)]">{["Partner","Active / share %","Opening","Contributions","Withdrawals","Profit share","Loss share","Closing","Actions"].map(label=><th key={label} className="p-3">{label}</th>)}</tr></thead><tbody>{partners.map(partner=><tr key={partner.id} className="border-b border-[var(--border)]"><td className="p-3 font-semibold">{partner.name}<p className="text-xs font-normal text-[var(--text-muted)]">{partner.financeAccountName??"No capital account"}{partner.isActive?"":" · Inactive"}</p></td><td className="p-3"><div className="flex items-center gap-2"><input aria-label={`${partner.name} active`} type="checkbox" checked={Boolean(activeStates[partner.id])} onChange={event=>setActiveStates({...activeStates,[partner.id]:event.target.checked})}/><input aria-label={`${partner.name} profit share`} type="number" step="0.0001" min="0" max="100" className="w-24 rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-2 py-1" value={shares[partner.id]??""} onChange={event=>setShares({...shares,[partner.id]:event.target.value})} disabled={!activeStates[partner.id]}/></div></td><td className="p-3">{money(partner.openingBalance)}</td><td className="p-3 text-emerald-300">{money(partner.contributions)}</td><td className="p-3 text-rose-300">{money(partner.withdrawals)}</td><td className="p-3">{money(partner.profitShare)}</td><td className="p-3">{money(partner.lossShare)}</td><td className="p-3 font-bold">{money(partner.closingBalance)}</td><td className="p-3"><div className="flex gap-2"><button className="text-[var(--accent)]" onClick={()=>setPartnerForm({id:partner.id,name:partner.name,cnic:partner.cnic??"",ntn:partner.ntn??"",profitSharePercent:String(partner.profitSharePercent),financeAccountId:partner.financeAccountId?String(partner.financeAccountId):"",joinedDate:partner.joinedDate?.slice(0,10)??"",exitedDate:partner.exitedDate?.slice(0,10)??"",concurrencyToken:partner.concurrencyToken})}>Edit</button><button className="text-[var(--accent)]" onClick={()=>openStatement(partner)}>Statement</button><button className="text-[var(--accent)]" disabled={!partner.isActive} onClick={()=>{setError(null);setTransactionPartner(partner);}}>Add transaction</button></div></td></tr>)}</tbody></table>{loading&&<p className="p-10 text-center text-[var(--text-muted)]">Loading partners…</p>}</div>
  {partnerForm&&<Modal title={partnerForm.id?`Edit partner · ${partnerForm.name}`:"Add capital partner"} close={()=>!savingPartner&&setPartnerForm(null)}><div className="space-y-4"><Field label="Name" value={partnerForm.name} set={value=>setPartnerForm({...partnerForm,name:value})}/><div className="grid gap-3 sm:grid-cols-2"><Field label="CNIC" value={partnerForm.cnic} set={value=>setPartnerForm({...partnerForm,cnic:value})}/><Field label="NTN" value={partnerForm.ntn} set={value=>setPartnerForm({...partnerForm,ntn:value})}/></div><Field label="Profit share %" type="number" disabled={partnerForm.id!==null} value={partnerForm.profitSharePercent} set={value=>setPartnerForm({...partnerForm,profitSharePercent:value})}/>{partnerForm.id&&<p className="text-xs text-[var(--text-muted)]">Change existing shares together in the table so the active allocation remains exactly 100%.</p>}<Select label="Capital account" value={partnerForm.financeAccountId} set={value=>setPartnerForm({...partnerForm,financeAccountId:value})}><option value="">Select account</option>{capitalAccounts.filter(account=>(account.isActive||String(account.id)===partnerForm.financeAccountId)&&!partners.some(partner=>partner.id!==partnerForm.id&&partner.financeAccountId===account.id)).map(account=><option key={account.id} value={account.id}>{account.name}{account.isActive?"":" (Inactive)"}</option>)}</Select><div className="grid gap-3 sm:grid-cols-2"><Field label="Joined date" type="date" value={partnerForm.joinedDate} set={value=>setPartnerForm({...partnerForm,joinedDate:value})}/><Field label="Exited date" type="date" value={partnerForm.exitedDate} set={value=>setPartnerForm({...partnerForm,exitedDate:value})}/></div>{!partnerForm.id&&<p className="text-xs text-[var(--text-muted)]">New partners start inactive. Activate them and rebalance all active shares together in the table.</p>}<div className="flex justify-end gap-2"><Button variant="ghost" onClick={()=>setPartnerForm(null)}>Cancel</Button><Button disabled={savingPartner} onClick={()=>void savePartner()}>{savingPartner?"Saving…":"Save partner"}</Button></div></div></Modal>}
  {transactionPartner&&<Modal title={`Capital transaction · ${transactionPartner.name}`} close={()=>!recordingTransaction&&setTransactionPartner(null)}><div className="space-y-4"><Select label="Type" value={form.type} set={value=>setForm({...form,type:value})}>{TYPES.map(([value,label])=><option key={value} value={value}>{label}</option>)}</Select><Field label="Amount (Rs)" type="number" value={form.amount} set={value=>setForm({...form,amount:value})}/><Field label="Date" type="date" value={form.date} set={value=>setForm({...form,date:value})}/>{(form.type==="Contribution"||form.type==="Withdrawal")&&<Select label="Cash / bank account" value={form.financeAccountId} set={value=>setForm({...form,financeAccountId:value})}><option value="">Select account</option>{accounts.filter(account=>account.isActive).map(account=><option key={account.id} value={account.id}>{account.name} · {account.accountHolderName}</option>)}</Select>}<Field label="Reference" value={form.reference} set={value=>setForm({...form,reference:value})}/><Field label="Note" value={form.note} set={value=>setForm({...form,note:value})}/><FinanceAttachmentField existing={null} selected={selectedAttachment} removeExisting={false} disabled={recordingTransaction} onSelected={setSelectedAttachment} onRemoveExisting={()=>{}} onViewExisting={()=>{}} onDownloadExisting={()=>{}}/><div className="flex justify-end gap-2"><Button variant="ghost" disabled={recordingTransaction} onClick={()=>{setSelectedAttachment(null);setTransactionPartner(null);}}>Cancel</Button><Button disabled={recordingTransaction} onClick={()=>void saveTransaction()}>{recordingTransaction?"Recording…":"Record transaction"}</Button></div></div></Modal>}
  {statementPartner&&<CapitalStatementModal partner={statementPartner} statement={statement} loading={statementLoading} error={statementError} from={statementFrom} to={statementTo} setFrom={setStatementFrom} setTo={setStatementTo} accountNames={cashAccountNames} onApply={()=>void loadStatement(statementPartner,statementFrom,statementTo)} onAllTime={()=>{setStatementFrom("");setStatementTo("");void loadStatement(statementPartner,"","");}} onViewAttachment={(row,download)=>void viewAttachment(statementPartner.id,row,download)} close={closeStatement}/>}
  </Container>;
}
function Field({label,value,set,type="text",disabled=false}:{label:string;value:string;set:(value:string)=>void;type?:string;disabled?:boolean}){return <label className="block text-sm text-[var(--text-muted)]">{label}<input disabled={disabled} className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 disabled:opacity-60" type={type} value={value} onChange={event=>set(event.target.value)}/></label>}
function Select({label,value,set,children}:{label:string;value:string;set:(value:string)=>void;children:React.ReactNode}){return <label className="block text-sm text-[var(--text-muted)]">{label}<select className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3" value={value} onChange={event=>set(event.target.value)}>{children}</select></label>}
function Modal({title,close,children,size="md"}:{title:React.ReactNode;close:()=>void;children:React.ReactNode;size?:"md"|"lg"}){return <div className="fixed inset-0 z-50 flex items-center justify-center p-4"><button aria-label="Close" className="absolute inset-0 bg-black/60" onClick={close}/><div className={`relative max-h-[92dvh] w-full ${size==="lg"?"max-w-5xl":"max-w-2xl"} overflow-y-auto rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6`}><div className="mb-5 flex items-start justify-between gap-4">{typeof title==="string"?<h2 className="text-xl font-bold">{title}</h2>:title}<button aria-label="Close" className="shrink-0 text-[var(--text-muted)] hover:text-[var(--text-primary)]" onClick={close}>✕</button></div>{children}</div></div>}

const STATEMENT_TYPE_LABELS:Record<string,string>={OpeningBalance:"Committed Opening Balance",Contribution:"Contribution",Withdrawal:"Withdrawal",ProfitShare:"Profit Share",LossShare:"Loss Share"};
const typeLabel=(value:string)=>STATEMENT_TYPE_LABELS[value]??String(value).replace(/([A-Z])/g," $1").trim();
// The calendar day is read out of the text the server sent rather than through `new Date(value)`.
// Every finance date is a Pakistani business date, and a plain parse of a midnight timestamp lands
// on the day before for anyone west of PKT — which would misdate a row, and the period label with it.
const statementDay=(value:string|null|undefined,offsetDays=0)=>{
  const match=value?/^(\d{4})-(\d{2})-(\d{2})/.exec(value):null;
  if(!match)return null;
  const day=new Date(Number(match[1]),Number(match[2])-1,Number(match[3])+offsetDays);
  return Number.isNaN(day.getTime())?null:day;
};
const statementDate=(value:string|null|undefined,offsetDays=0)=>statementDay(value,offsetDays)?.toLocaleDateString("en-GB",{day:"2-digit",month:"short",year:"numeric"})??"—";
const DASH=<span className="font-normal text-[var(--text-muted)]">—</span>;

function CapitalStatementModal({partner,statement,loading,error,from,to,setFrom,setTo,accountNames,onApply,onAllTime,onViewAttachment,close}:{
  partner:Partner;statement:Statement|null;loading:boolean;error:string|null;
  from:string;to:string;setFrom:(value:string)=>void;setTo:(value:string)=>void;
  accountNames:Map<number,string>;onApply:()=>void;onAllTime:()=>void;
  onViewAttachment:(row:Transaction,download:boolean)=>void;close:()=>void;
}){
  // The running balance is rebuilt from the opening figure with the same sign rule the server uses,
  // so the last row always lands on the closing balance the server reported beside it.
  const rows=useMemo(()=>{
    if(!statement)return [];
    let running=statement.openingBalance;
    return statement.transactions.map(row=>{
      const signed=row.type==="Withdrawal"||row.type==="LossShare"?-row.amount:row.amount;
      running=Math.round((running+signed)*100)/100;
      return {...row,signed,balance:running};
    });
  },[statement]);
  // The period is described from what the server answered for, never from the inputs: a date typed
  // but not yet applied must not relabel figures that were calculated for a different range.
  const appliedFrom=statement?.from??null, appliedTo=statement?.to??null;
  const periodLabel=!statement?"…"
    :appliedFrom&&appliedTo?`${statementDate(appliedFrom)} – ${statementDate(appliedTo)}`
    :appliedFrom?`${statementDate(appliedFrom)} onwards`
    :appliedTo?`up to ${statementDate(appliedTo)}`
    :"all time";
  const openingNote=appliedFrom?`As of ${statementDate(appliedFrom,-1)} · before the selected period`:"Before the first recorded movement";
  const closingNote=appliedTo?`As of ${statementDate(appliedTo)} · end of the selected period`:"Every movement recorded to date";
  const closingUp=(statement?.closingBalance??0)>=0;
  const invalidRange=Boolean(from&&to&&from>to);
  const headCell="sticky top-0 z-10 bg-[var(--modal-bg)] px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-[var(--text-muted)] shadow-[inset_0_-1px_0_var(--border)]";
  const dateInput="mt-1 block rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm font-normal normal-case tracking-normal text-[var(--text-primary)]";

  return <Modal size="lg" close={close} title={<div>
    <h2 className="text-xl font-bold text-[var(--text-heading)]">{statement?.partnerName??partner.name} · Capital Statement</h2>
    <div className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
      <span className={`rounded-full border px-2.5 py-0.5 font-semibold ${partner.isActive?"border-emerald-500/30 bg-emerald-500/10 text-emerald-300":"border-[var(--border)] bg-[var(--surface-glass)] text-[var(--text-muted)]"}`}>{partner.isActive?"Active":"Inactive"}</span>
      <span className="text-[var(--text-muted)]">Profit share: {Number(partner.profitSharePercent).toLocaleString("en-PK",{maximumFractionDigits:4})}%</span>
      {partner.financeAccountName&&<span className="text-[var(--text-muted)]">· {partner.financeAccountName}</span>}
    </div>
  </div>}>
    <div className="mb-4 flex flex-wrap items-end justify-between gap-3 rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-3">
      <div className="flex flex-wrap items-end gap-2">
        <label className="block text-[11px] font-semibold uppercase tracking-wide text-[var(--text-muted)]">From<input type="date" max={today()} className={dateInput} value={from} onChange={event=>setFrom(event.target.value)}/></label>
        <label className="block text-[11px] font-semibold uppercase tracking-wide text-[var(--text-muted)]">To<input type="date" max={today()} className={dateInput} value={to} onChange={event=>setTo(event.target.value)}/></label>
        <Button size="sm" disabled={loading||invalidRange} onClick={onApply}>{loading?"Loading…":"Apply"}</Button>
        <Button size="sm" variant="outline" disabled={loading} onClick={onAllTime}>All time</Button>
      </div>
      <p className="text-xs text-[var(--text-muted)]">Showing statement for <span className="font-semibold text-[var(--text-secondary)]">{periodLabel}</span></p>
    </div>
    {error&&<p className="mb-4 rounded-xl border border-rose-500/30 bg-rose-500/10 p-3 text-sm text-rose-300">{error}</p>}
    {invalidRange&&!error&&<p className="mb-4 rounded-xl border border-amber-500/30 bg-amber-500/10 p-3 text-sm text-amber-200">The From date cannot be after the To date.</p>}
    {!statement
      ?<p className="py-20 text-center text-sm text-[var(--text-muted)]">{loading?"Loading statement…":"No statement to show."}</p>
      :<div className={`transition-opacity ${loading?"pointer-events-none opacity-50":""}`}>
      <div className="mb-4 grid gap-3 sm:grid-cols-2">
        <div className="flex items-start gap-3 rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
          <span aria-hidden="true" className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-[var(--border)] bg-[var(--surface)] text-[var(--accent)]"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4"><path d="M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8Z"/><path d="M14 3v5h5"/></svg></span>
          <div className="min-w-0">
            <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--text-muted)]">Opening balance</p>
            <p className="mt-0.5 text-2xl font-bold text-[var(--text-heading)]">{money(statement.openingBalance)}</p>
            <p className="mt-1 text-xs text-[var(--text-muted)]">{openingNote}</p>
          </div>
        </div>
        <div className={`flex items-start gap-3 rounded-2xl border p-4 ${closingUp?"border-emerald-500/25 bg-emerald-500/5":"border-rose-500/25 bg-rose-500/5"}`}>
          <span aria-hidden="true" className={`inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border ${closingUp?"border-emerald-500/30 bg-emerald-500/10 text-emerald-300":"border-rose-500/30 bg-rose-500/10 text-rose-300"}`}><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4"><path d="M3 10 12 4l9 6"/><path d="M5 10v9M19 10v9M9 10v9M15 10v9"/><path d="M3 20h18"/></svg></span>
          <div className="min-w-0">
            <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--text-muted)]">Closing balance</p>
            <p className={`mt-0.5 text-2xl font-bold ${closingUp?"text-emerald-300":"text-rose-300"}`}>{money(statement.closingBalance)}</p>
            <p className="mt-1 text-xs text-[var(--text-muted)]">{closingNote}</p>
          </div>
        </div>
      </div>
      <div className="max-h-[46vh] overflow-auto rounded-2xl border border-[var(--border)]">
        <table className="w-full min-w-[760px] text-sm">
          <thead>
            <tr className="text-left">
              <th className={headCell}>Date</th>
              <th className={headCell}>Particulars</th>
              <th className={headCell}>Account</th>
              <th className={`${headCell} text-right`}>Debit (Decrease)</th>
              <th className={`${headCell} text-right`}>Credit (Increase)</th>
              <th className={`${headCell} text-right`}>Balance</th>
            </tr>
          </thead>
          <tbody>
            <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
              <td className="px-3 py-2.5 text-[var(--text-muted)]">—</td>
              <td className="px-3 py-2.5 font-semibold text-[var(--accent)]">Opening Balance</td>
              <td className="px-3 py-2.5 text-[var(--text-muted)]">—</td>
              <td className="px-3 py-2.5 text-right text-[var(--text-muted)]">—</td>
              <td className="px-3 py-2.5 text-right text-[var(--text-muted)]">—</td>
              <td className="whitespace-nowrap px-3 py-2.5 text-right font-semibold text-[var(--text-primary)]">{money(statement.openingBalance)}</td>
            </tr>
            {rows.map(row=>{
              const decrease=row.signed<0, account=row.financeAccountId!=null?accountNames.get(row.financeAccountId)??null:null;
              return <tr key={row.id} className="border-b border-[var(--border)] align-top last:border-b-0 hover:bg-[var(--surface-glass)]">
                <td className="whitespace-nowrap px-3 py-3 text-[var(--text-secondary)]">{statementDate(row.date)}</td>
                <td className="px-3 py-3">
                  <div className="flex items-start gap-2">
                    <span aria-hidden="true" className={`mt-0.5 inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[11px] font-bold ${decrease?"bg-rose-500/15 text-rose-300":"bg-emerald-500/15 text-emerald-300"}`}>{decrease?"−":"+"}</span>
                    <div className="min-w-0">
                      <p className="font-semibold text-[var(--text-primary)]">{typeLabel(row.type)}</p>
                      {row.reference&&<p className="text-xs text-[var(--text-muted)]">Ref {row.reference}</p>}
                      {row.note&&row.note.trim().toLowerCase()!==typeLabel(row.type).toLowerCase()&&<p className="text-xs text-[var(--text-muted)]">{row.note}</p>}
                      {row.profitSharePercentSnapshot!=null&&<p className="text-xs text-[var(--text-muted)]">{row.profitSharePercentSnapshot}% share snapshot</p>}
                      {row.attachment&&<span className="mt-1.5 inline-flex items-center gap-2 whitespace-nowrap"><button type="button" className="rounded-full border border-indigo-500/20 bg-indigo-500/10 px-2.5 py-1 text-[10px] font-semibold text-indigo-300 hover:bg-indigo-500/20" onClick={()=>onViewAttachment(row,false)}>Attached</button><button type="button" aria-label={`Download ${row.attachment.fileName}`} title={`Download ${row.attachment.fileName}`} className="text-xs font-semibold text-[var(--text-muted)] hover:text-[var(--text-primary)]" onClick={()=>onViewAttachment(row,true)}>↓</button></span>}
                    </div>
                  </div>
                </td>
                <td className="px-3 py-3 text-[var(--text-secondary)]">{account||DASH}</td>
                <td className="whitespace-nowrap px-3 py-3 text-right font-semibold text-rose-300">{decrease?money(Math.abs(row.signed)):DASH}</td>
                <td className="whitespace-nowrap px-3 py-3 text-right font-semibold text-emerald-300">{decrease?DASH:money(row.signed)}</td>
                <td className="whitespace-nowrap px-3 py-3 text-right font-semibold text-[var(--text-primary)]">{money(row.balance)}</td>
              </tr>;
            })}
            {!rows.length&&<tr className="border-b border-[var(--border)]"><td colSpan={6} className="px-3 py-10 text-center text-sm text-[var(--text-muted)]">No capital movements in this period.</td></tr>}
          </tbody>
          <tfoot>
            <tr className="bg-[var(--surface-glass)]">
              <td colSpan={5} className="px-3 py-3 text-right text-sm font-semibold text-[var(--text-secondary)]">Closing Balance</td>
              <td className="whitespace-nowrap px-3 py-3 text-right text-base font-bold text-[var(--accent)]">{money(statement.closingBalance)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
      <p className="mt-3 rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] px-4 py-3 text-xs text-[var(--text-muted)]">
        <span className="font-semibold text-emerald-300">Credit (Increase)</span>: contributions and profit share · <span className="font-semibold text-rose-300">Debit (Decrease)</span>: withdrawals and loss share
      </p>
    </div>}
  </Modal>;
}
