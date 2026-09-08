import AppSelect from "../lib/AppSelect.tsx";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { User } from "../App";
import { api } from "../api/api";
import { readFinanceAccountsPage } from "../features/finance/accountPageReads";
import Button from "../lib/Button";
import Container from "../lib/Container";

type Account = { id:number; name:string; type:number|string; accountHolderName:string; openingBalance:number; ledgerCode:string|null; displayOrder:number; systemRole:number|string; isSystemAccount:boolean; bankOrWalletName:string|null; description:string|null; isActive:boolean; revenueReceived:number; expensesPaid:number; whtWithheld:number; whtDeposited:number; netMovement:number; currentBalance:number; transactionCount:number; concurrencyToken:string };
// `amount` is the cash effect (net of tax withheld, for expenses); `grossAmount` is the invoice total.
type Transaction = { kind:string; recordId:number; date:string; label:string; reference:string|null; projectName:string; amount:number; grossAmount:number; whtAmount:number };
type Overview = { activeAccounts:number; inactiveAccounts:number; totalBalance:number; holderBalances:{accountHolderName:string;accountCount:number;currentBalance:number}[] };
type Form = { id:number|null; name:string; type:string; accountHolderName:string; openingBalance:string; ledgerCode:string; displayOrder:string; bankOrWalletName:string; description:string; concurrencyToken:string; isSystemAccount:boolean };
type TransactionWithBalance = { t:Transaction; balance:number };
type AccountTotals = Pick<Account,"openingBalance"|"revenueReceived"|"expensesPaid"|"currentBalance">;
const types:Record<number,string> = {1:"Cash",2:"Bank",3:"Mobile Wallet",4:"Other",5:"Liability",6:"Capital",7:"Fixed Asset",8:"Receivable",9:"Work in Progress",10:"Staff Float"};
const enumValues:Record<string,number>={Cash:1,Bank:2,MobileWallet:3,Other:4,Liability:5,Capital:6,FixedAsset:7,Receivable:8,WorkInProgress:9,StaffFloat:10};
const typeValue=(value:number|string)=>typeof value==="number"?value:(enumValues[value]??Number(value));
const typeName=(value:number|string)=>types[typeValue(value)]??"—";
const isCashLike=(value:number|string)=>typeValue(value)<=4;
const groups:[string,number[]][]=[["Cash & Bank",[1,2,3,4]],["Cash held by staff",[10]],["Fixed Assets",[7]],["Work in Progress",[9]],["Receivables",[8]],["Liabilities",[5]],["Capital",[6]]];
const accountColumns=["Account","Type / holder","Opening","Increases","Decreases","Current balance","Status","Actions"];
const emptyForm = ():Form => ({ id:null, name:"", type:"1", accountHolderName:"", openingBalance:"0", ledgerCode:"", displayOrder:"0", bankOrWalletName:"", description:"", concurrencyToken:"", isSystemAccount:false });
const money = (n:number) => `Rs ${n.toLocaleString("en-PK", { maximumFractionDigits:2 })}`;

function sumAccounts(accounts:Account[]):AccountTotals {
  return accounts.reduce<AccountTotals>((total,account)=>({
    openingBalance:total.openingBalance+account.openingBalance,
    revenueReceived:total.revenueReceived+account.revenueReceived,
    expensesPaid:total.expensesPaid+account.expensesPaid,
    currentBalance:total.currentBalance+account.currentBalance,
  }),{openingBalance:0,revenueReceived:0,expensesPaid:0,currentBalance:0});
}

function withRunningBalances(transactions: Transaction[], openingBalance: number): TransactionWithBalance[] {
  let balance = openingBalance;
  return transactions.map((transaction) => {
    balance += transaction.amount;
    return { t: transaction, balance };
  });
}

export default function FinanceAccountsPage({ user }:{user:User|null}) {
  const navigate = useNavigate();
  const [accounts,setAccounts]=useState<Account[]>([]), [loading,setLoading]=useState(true), [search,setSearch]=useState(""), [status,setStatus]=useState("all");
  const [typeFilter,setTypeFilter]=useState(""), [holderFilter,setHolderFilter]=useState("");
  const [form,setForm]=useState<Form|null>(null), [saving,setSaving]=useState(false), [error,setError]=useState<string|null>(null);
  const [selected,setSelected]=useState<Account|null>(null), [transactions,setTransactions]=useState<Transaction[]>([]);
  const [overview,setOverview]=useState<Overview|null>(null);
  const accountsRequest=useRef(0), overviewLoaded=useRef(false);
  const accountsController=useRef<AbortController|null>(null);
  const accountsDebounceTimer=useRef<number|null>(null);

  const loadAccounts=useCallback(async(refreshOverview=false)=>{
    accountsController.current?.abort();
    const controller=new AbortController();accountsController.current=controller;
    const request=++accountsRequest.current;
    setLoading(true);
    try{
      // Until a combined load succeeds, replacing an aborted initial request must still
      // fetch the global overview. Later filter changes only need the filtered account page.
      const next=await readFinanceAccountsPage<Account,Overview>(
        {search,status,typeFilter,holderFilter},refreshOverview||!overviewLoaded.current,
        path=>api(path,{signal:controller.signal}));
      if(!controller.signal.aborted&&request===accountsRequest.current){
        setAccounts(next.items);
        if(next.overview){setOverview(next.overview);overviewLoaded.current=true;}
      }
    }catch{
      if(!controller.signal.aborted&&request===accountsRequest.current)setAccounts([]);
    }finally{
      if(!controller.signal.aborted&&request===accountsRequest.current)setLoading(false);
      if(accountsController.current===controller)accountsController.current=null;
    }
  },[search,status,typeFilter,holderFilter]);

  const refresh=useCallback(async()=>{
    if(accountsDebounceTimer.current!==null){window.clearTimeout(accountsDebounceTimer.current);accountsDebounceTimer.current=null;}
    overviewLoaded.current=false;
    await loadAccounts(true);
  },[loadAccounts]);

  useEffect(()=>{
    if(user?.role!=="Admin"){navigate("/");return;}
    const timer=window.setTimeout(()=>{if(accountsDebounceTimer.current===timer)accountsDebounceTimer.current=null;void loadAccounts();},250);
    accountsDebounceTimer.current=timer;
    return()=>{window.clearTimeout(timer);if(accountsDebounceTimer.current===timer)accountsDebounceTimer.current=null;accountsController.current?.abort();};
  },[user,navigate,loadAccounts]);
  const openDetail=async(a:Account)=>{setSelected(a);setTransactions([]);try{const [detail,tx]=await Promise.all([api(`/api/finance/accounts/${a.id}`),api(`/api/finance/accounts/${a.id}/transactions?take=200`)]);if(detail.ok)setSelected(await detail.json());if(tx.ok)setTransactions((await tx.json()).items);}catch{/* Empty history is shown with a safe fallback. */}};
  const save=async()=>{ if(!form||saving)return; setError(null); if(!form.name.trim()||!form.accountHolderName.trim()){setError("Account name and account holder are required.");return;} const opening=Number(form.openingBalance); if(!Number.isFinite(opening)){setError("Enter a valid opening balance.");return;} setSaving(true); try { const body={name:form.name.trim(),type:Number(form.type),accountHolderName:form.accountHolderName.trim(),openingBalance:opening,ledgerCode:form.ledgerCode.trim()||null,displayOrder:Number(form.displayOrder)||0,bankOrWalletName:form.bankOrWalletName.trim()||null,description:form.description.trim()||null,concurrencyToken:form.concurrencyToken}; const r=await api(form.id?`/api/finance/accounts/${form.id}`:"/api/finance/accounts",{method:form.id?"PUT":"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)}); if(!r.ok){const d=await r.json().catch(()=>null);throw new Error(d?.message??"Account could not be saved.");} setForm(null);await refresh(); } catch(saveError) { setError(saveError instanceof Error?saveError.message:"Account could not be saved. Check your connection and try again."); } finally { setSaving(false); } };
  const edit=(a:Account)=>setForm({id:a.id,name:a.name,type:String(typeValue(a.type)),accountHolderName:a.accountHolderName,openingBalance:String(a.openingBalance),ledgerCode:a.ledgerCode??"",displayOrder:String(a.displayOrder),bankOrWalletName:a.bankOrWalletName??"",description:a.description??"",concurrencyToken:a.concurrencyToken,isSystemAccount:a.isSystemAccount});
  const toggle=async(a:Account)=>{const action=a.isActive?"deactivate":"reactivate";if(!confirm(`${action[0].toUpperCase()+action.slice(1)} ${a.name}? Historical transactions will be preserved.`))return;try{const r=await api(`/api/finance/accounts/${a.id}/status`,{method:"PATCH",headers:{"Content-Type":"application/json"},body:JSON.stringify({isActive:!a.isActive,concurrencyToken:a.concurrencyToken})});if(!r.ok)throw new Error((await r.json().catch(()=>null))?.message??"Status could not be changed.");await refresh();}catch(statusError){alert(statusError instanceof Error?statusError.message:"Status could not be changed. Check your connection and try again.");}};
  const accountGroups=groups.map(([group,values])=>({group,rows:accounts.filter(account=>values.includes(typeValue(account.type)))})).filter(({rows})=>rows.length);
  const allAccountTotals=sumAccounts(accounts);
  return <Container className="py-8">
    <div className="mb-6 flex flex-wrap items-center justify-between gap-3"><div><Link to="/finance" className="text-sm text-[var(--accent)]">← Finance dashboard</Link><h1 className="mt-1 text-2xl font-bold text-[var(--text-heading)]">Finance Accounts</h1><p className="text-sm text-[var(--text-muted)]">Cash, assets, receivables, liabilities and partner capital in the verified chart.</p></div><div className="flex gap-2"><Button onClick={()=>setForm(emptyForm())}>Add Account</Button></div></div>
    <div className="mb-5 grid gap-3 sm:grid-cols-3"><Stat label="Active accounts" value={String(overview?.activeAccounts??0)}/><Stat label="Combined balance" value={money(overview?.totalBalance??0)}/><Stat label="Account holders" value={String(overview?.holderBalances.length??0)}/></div>
    <div className="mb-4 flex flex-wrap gap-3"><input aria-label="Search accounts" className="min-w-[200px] flex-1 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 py-2 text-[var(--text-primary)]" placeholder="Search account, holder, bank or wallet" value={search} onChange={e=>setSearch(e.target.value)}/><AppSelect aria-label="Filter by type" className="rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3 text-[var(--text-primary)]" value={typeFilter} onChange={e=>setTypeFilter(e.target.value)}><option value="">All types</option>{Object.entries(types).map(([value,label])=><option key={value} value={value}>{label}</option>)}</AppSelect><AppSelect aria-label="Filter by holder" className="rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3 text-[var(--text-primary)]" value={holderFilter} onChange={e=>setHolderFilter(e.target.value)}><option value="">All holders</option>{overview?.holderBalances.map(h=><option key={h.accountHolderName} value={h.accountHolderName}>{h.accountHolderName}</option>)}</AppSelect><AppSelect aria-label="Filter by status" className="rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3 text-[var(--text-primary)]" value={status} onChange={e=>setStatus(e.target.value)}><option value="all">All status</option><option value="active">Active</option><option value="inactive">Inactive</option></AppSelect></div>
    <div className="overflow-x-auto rounded-2xl border border-[var(--border)] bg-[var(--surface)]">
      <table className="w-full min-w-[900px] text-sm">
        <caption className="sr-only">Finance accounts grouped by balance-sheet section</caption>
        <thead><AccountColumnHeaders/></thead>
        {accountGroups.map(({group,rows},groupIndex)=>{
          const totals=sumAccounts(rows);
          return <tbody key={group}>
            <tr className="bg-[var(--surface-glass)]">
              <th scope="rowgroup" colSpan={8} className="px-4 py-2 text-left text-xs font-bold uppercase tracking-wider text-[var(--accent)]">{group}</th>
            </tr>
            {groupIndex>0&&<AccountColumnHeaders repeated/>}
            {rows.map(a=><tr key={a.id} className="border-b border-[var(--border)]"><td className="p-4"><button onClick={()=>void openDetail(a)} className="text-left text-[15px] font-black leading-snug tracking-[0.01em] text-sky-300 hover:text-sky-200 hover:underline">{a.name}</button><div className="text-xs font-normal text-[var(--text-muted)]">{a.ledgerCode?`GL ${a.ledgerCode}`:""}{a.bankOrWalletName?` · ${a.bankOrWalletName}`:""}</div></td><td className="p-4">{typeName(a.type)}<div className="text-xs text-[var(--text-muted)]">{a.accountHolderName}</div></td><td className="p-4">{money(a.openingBalance)}</td><td className="p-4 text-emerald-400">{money(a.revenueReceived)}</td><td className="p-4 text-rose-400">{money(a.expensesPaid)}</td><td className="p-4 font-semibold">{money(a.currentBalance)}</td><td className="p-4"><span className={a.isActive?"text-emerald-400":"text-[var(--text-muted)]"}>{a.isActive?"Active":"Inactive"}</span></td><td className="p-4"><div className="flex gap-2"><button className="text-[var(--accent)]" onClick={()=>edit(a)}>Edit</button><button className="text-[var(--text-muted)] disabled:opacity-50" disabled={a.isSystemAccount&&a.isActive} onClick={()=>void toggle(a)}>{a.isSystemAccount&&a.isActive?"Protected":a.isActive?"Deactivate":"Reactivate"}</button></div></td></tr>)}
            <AccountTotalsRow label={`${group} Total`} totals={totals}/>
          </tbody>;
        })}
        {!!accountGroups.length&&<tfoot><AccountTotalsRow label="All Accounts Total" totals={allAccountTotals} grand/></tfoot>}
      </table>
      {loading&&<p className="p-6 text-center text-[var(--text-muted)]">Loading…</p>}{!loading&&!accounts.length&&<p className="p-8 text-center text-[var(--text-muted)]">No finance accounts found.</p>}
    </div>
    {form&&<Modal title={form.id?"Edit Finance Account":"Add Finance Account"} close={()=>!saving&&setForm(null)}><div className="space-y-4">{error&&<p className="rounded-lg bg-rose-500/10 p-3 text-sm text-rose-300">{error}</p>}{form.isSystemAccount&&<p className="rounded-lg border border-amber-500/25 bg-amber-500/[0.07] p-3 text-sm text-amber-200">This system account is protected because financial statements depend on it.</p>}<Field label="Account Name" value={form.name} set={v=>setForm({...form,name:v})} disabled={form.isSystemAccount}/><label className="block text-sm text-[var(--text-muted)]">Account Type<AppSelect disabled={form.isSystemAccount} className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3 text-[var(--text-primary)]" value={form.type} onChange={e=>setForm({...form,type:e.target.value})}>{Object.entries(types).map(([value,label])=><option value={value} key={value}>{label}</option>)}</AppSelect></label><Field label="Account Holder Name" value={form.accountHolderName} set={v=>setForm({...form,accountHolderName:v})}/><Field label="Opening Balance (Rs)" type="number" value={form.openingBalance} set={v=>setForm({...form,openingBalance:v})}/><div className="grid grid-cols-2 gap-3"><Field label="Ledger / GL code" value={form.ledgerCode} set={v=>setForm({...form,ledgerCode:v})} disabled={form.isSystemAccount}/><Field label="Display order" type="number" value={form.displayOrder} set={v=>setForm({...form,displayOrder:v})}/></div><Field label="Bank / Wallet Name (optional)" value={form.bankOrWalletName} set={v=>setForm({...form,bankOrWalletName:v})}/><Field label="Description (optional)" value={form.description} set={v=>setForm({...form,description:v})}/><div className="flex justify-end gap-2"><Button variant="ghost" onClick={()=>setForm(null)} disabled={saving}>Cancel</Button><Button onClick={()=>void save()} disabled={saving}>{saving?"Saving…":"Save"}</Button></div></div></Modal>}
    {selected&&<DetailModal account={selected} transactions={transactions} close={()=>setSelected(null)}/>}
  </Container>;
}
function DetailModal({account,transactions,close}:{account:Account;transactions:Transaction[];close:()=>void}){
  const fullHistory = transactions.length >= account.transactionCount;
  const orderedAsc = [...transactions].sort((a,b)=> a.date===b.date ? a.recordId-b.recordId : (a.date<b.date?-1:1));
  const rows = withRunningBalances(orderedAsc, account.openingBalance);
  return <Modal title={account.name} close={close}>
    <p className="mb-3 text-sm text-[var(--text-muted)]">{typeName(account.type)} · {account.accountHolderName}{account.bankOrWalletName?` · ${account.bankOrWalletName}`:""}{account.isActive?"":" · Inactive"}</p>
    <div className="mb-4 grid grid-cols-2 gap-3"><Stat label="Opening balance" value={money(account.openingBalance)}/><Stat label="Current balance" value={money(account.currentBalance)}/><Stat label={isCashLike(account.type)?"Money in":"Increases"} value={money(account.revenueReceived)}/><Stat label={isCashLike(account.type)?"Money out":"Decreases"} value={money(account.expensesPaid)}/></div>
    {/* Tax withheld from suppliers is sitting inside the balance above but is not the company's
        money — it is owed to FBR until a challan is recorded. */}
    {account.whtWithheld > 0 && <p className="mb-4 rounded-xl border border-amber-500/25 bg-amber-500/[0.07] px-4 py-3 text-sm text-amber-200">Includes {money(account.whtWithheld - account.whtDeposited)} of withholding tax held for FBR ({money(account.whtWithheld)} withheld, {money(account.whtDeposited)} deposited). <Link to="/finance/settings" className="underline">Record a deposit</Link></p>}
    <h3 className="mb-2 font-semibold">Transaction history</h3>
    {!transactions.length
      ? <p className="py-8 text-center text-sm text-[var(--text-muted)]">No transactions assigned to this account.</p>
      : <div className="max-h-80 space-y-2 overflow-y-auto">
          {fullHistory&&<div className="flex items-center justify-between rounded-xl border border-dashed border-[var(--border)] p-3 text-[var(--text-muted)]"><span className="font-medium">Opening balance</span><span>{money(account.openingBalance)}</span></div>}
          {rows.map(({t,balance})=><div key={`${t.kind}-${t.recordId}`} className="flex items-center justify-between rounded-xl border border-[var(--border)] p-3"><div><p className="font-medium">{t.label}</p><p className="text-xs text-[var(--text-muted)]">{new Date(t.date).toLocaleDateString("en-GB")} · {t.projectName}{t.reference?` · ${t.reference}`:""}</p>{t.whtAmount>0&&<p className="text-xs text-amber-400/90">{money(t.grossAmount)} invoiced · {money(t.whtAmount)} tax withheld</p>}</div><div className="text-right"><span className={t.amount>=0?"text-emerald-400":"text-rose-400"}>{t.amount>=0?"+":""}{money(t.amount)}</span>{fullHistory&&<p className="text-xs text-[var(--text-muted)]">Balance {money(balance)}</p>}</div></div>)}
          {!fullHistory&&<p className="pt-1 text-center text-xs text-[var(--text-muted)]">Showing the {transactions.length} most recent transactions; running balance hidden.</p>}
        </div>}
  </Modal>;
}
function AccountColumnHeaders({repeated=false}:{repeated?:boolean}){
  return <tr aria-hidden={repeated||undefined} className="border-y border-[var(--accent)]/25 bg-[var(--accent-glow)] text-left text-xs font-bold uppercase tracking-wide text-[var(--accent-light)]">
    {accountColumns.map(column=>repeated?<td key={column} className="px-4 py-3">{column}</td>:<th scope="col" key={column} className="px-4 py-3">{column}</th>)}
  </tr>;
}
function AccountTotalsRow({label,totals,grand=false}:{label:string;totals:AccountTotals;grand?:boolean}){
  return <tr className={grand?"border-t-2 border-[var(--accent)]/60 bg-[var(--accent-glow-strong)] text-base font-black text-[var(--accent-light)]":"border-b-2 border-[var(--border)] bg-[var(--surface-glass)] font-bold text-[var(--text-heading)]"}>
    <th scope="row" colSpan={2} className={`p-4 text-left ${grand?"uppercase tracking-wide":""}`}>{label}</th>
    <td className="whitespace-nowrap p-4 tabular-nums">{money(totals.openingBalance)}</td>
    <td className="whitespace-nowrap p-4 tabular-nums text-emerald-400">{money(totals.revenueReceived)}</td>
    <td className="whitespace-nowrap p-4 tabular-nums text-rose-400">{money(totals.expensesPaid)}</td>
    <td className="whitespace-nowrap p-4 tabular-nums">{money(totals.currentBalance)}</td>
    <td colSpan={2} aria-hidden="true"/>
  </tr>;
}
function Stat({label,value}:{label:string;value:string}){return <div className="rounded-xl border border-[var(--border)] bg-[var(--surface)] p-4"><p className="text-xs text-[var(--text-muted)]">{label}</p><p className="mt-1 font-semibold text-[var(--text-heading)]">{value}</p></div>}
function Field({label,value,set,type="text",disabled=false}:{label:string;value:string;set:(v:string)=>void;type?:string;disabled?:boolean}){return <label className="block text-sm text-[var(--text-muted)]">{label}<input disabled={disabled} type={type} className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3 text-[var(--text-primary)]" value={value} onChange={e=>set(e.target.value)}/></label>}
function Modal({title,close,children}:{title:string;close:()=>void;children:React.ReactNode}){return <div className="fixed inset-0 z-50 flex items-center justify-center p-4"><button aria-label="Close" className="absolute inset-0 bg-black/60" onClick={close}/><div className="relative max-h-[92dvh] w-full max-w-xl overflow-y-auto rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-2xl"><div className="mb-5 flex justify-between"><h2 className="text-xl font-semibold">{title}</h2><button onClick={close}>✕</button></div>{children}</div></div>}
