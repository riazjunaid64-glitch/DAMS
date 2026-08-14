import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { User } from "../App";
import { api } from "../api/api";
import Button from "../lib/Button";
import Container from "../lib/Container";

type Loan = {
  id:number; name:string; lenderName:string|null; financeAccountId:number; financeAccountName:string;
  isActive:boolean; openingBalance:number; drawnPrincipal:number; repaidPrincipal:number;
  interestPaid:number; currentBalance:number; transactionCount:number; concurrencyToken:string;
};
type LoanAccount = { id:number; name:string; accountHolderName:string; isActive:boolean; linkedLoanId:number|null; linkedLoanName:string|null };
type CashAccount = { id:number; name:string; accountHolderName:string; isActive:boolean };
type LoanTransaction = {
  id:number; loanId:number; type:"Drawdown"|"Repayment"; principalAmount:number; interestAmount:number;
  totalCashMovement:number; date:string; financeAccountId:number; financeAccountName:string;
  reference:string|null; note:string|null; runningBalance:number; createdAt:string; updatedAt:string; concurrencyToken:string;
};
type Statement = { loan:Loan; items:LoanTransaction[]; hasMore:boolean };
type LoanForm = { id:number|null; name:string; lenderName:string; financeAccountId:string; isActive:boolean; concurrencyToken:string };
type TransactionForm = {
  id:number|null; type:"Drawdown"|"Repayment"; principalAmount:string; interestAmount:string;
  date:string; financeAccountId:string; reference:string; note:string; concurrencyToken:string;
};

const money = (value:number) => `Rs ${value.toLocaleString("en-PK", { minimumFractionDigits:2, maximumFractionDigits:2 })}`;
const today = () => {
  const value=new Date(), pad=(part:number)=>String(part).padStart(2,"0");
  return `${value.getFullYear()}-${pad(value.getMonth()+1)}-${pad(value.getDate())}`;
};
const emptyLoan = ():LoanForm => ({id:null,name:"",lenderName:"",financeAccountId:"",isActive:true,concurrencyToken:""});
const emptyTransaction = (type:"Drawdown"|"Repayment"):TransactionForm => ({
  id:null,type,principalAmount:"",interestAmount:"",date:today(),financeAccountId:"",reference:"",note:"",concurrencyToken:""
});

export default function FinanceLoansPage({user}:{user:User|null}) {
  const navigate=useNavigate();
  const [loans,setLoans]=useState<Loan[]>([]);
  const [loanAccounts,setLoanAccounts]=useState<LoanAccount[]>([]);
  const [cashAccounts,setCashAccounts]=useState<CashAccount[]>([]);
  const [selectedId,setSelectedId]=useState<number|null>(null);
  const [statement,setStatement]=useState<Statement|null>(null);
  const [loading,setLoading]=useState(true);
  const [loadingStatement,setLoadingStatement]=useState(false);
  const [error,setError]=useState<string|null>(null);
  const [loanForm,setLoanForm]=useState<LoanForm|null>(null);
  const [transactionForm,setTransactionForm]=useState<TransactionForm|null>(null);
  const [saving,setSaving]=useState(false);

  const loadLoans=useCallback(async()=>{
    setLoading(true);setError(null);
    try {
      const [loanResponse,liabilityResponse,cashResponse]=await Promise.all([
        api("/api/finance/loans?includeInactive=true"),
        api("/api/finance/loans/account-options?includeInactive=true"),
        api("/api/finance/accounts/options?includeInactive=true&cashLikeOnly=true")
      ]);
      if(!loanResponse.ok)throw new Error((await loanResponse.json().catch(()=>null))?.message??"Loans could not be loaded.");
      const rows=await loanResponse.json() as Loan[];
      setLoans(rows);
      setSelectedId(current=>current&&rows.some(row=>row.id===current)?current:(rows[0]?.id??null));
      if(liabilityResponse.ok)setLoanAccounts(await liabilityResponse.json());
      if(cashResponse.ok)setCashAccounts(await cashResponse.json());
    } catch(caught) {
      setError(caught instanceof Error?caught.message:"Loans could not be loaded.");
    } finally { setLoading(false); }
  },[]);

  const loadStatement=useCallback(async(id:number,skip=0)=>{
    setLoadingStatement(true);setError(null);
    try {
      const response=await api(`/api/finance/loans/${id}/statement?skip=${skip}&take=100`);
      if(!response.ok)throw new Error((await response.json().catch(()=>null))?.message??"Loan statement could not be loaded.");
      const next=await response.json() as Statement;
      setStatement(current=>skip>0&&current?.loan.id===id?{...next,items:[...current.items,...next.items]}:next);
    } catch(caught) {
      setError(caught instanceof Error?caught.message:"Loan statement could not be loaded.");
    } finally { setLoadingStatement(false); }
  },[]);

  useEffect(()=>{if(user?.role!=="Admin"){navigate("/");return;}void loadLoans();},[user,navigate,loadLoans]);
  useEffect(()=>{if(selectedId)void loadStatement(selectedId);else setStatement(null);},[selectedId,loadStatement]);

  const selected=statement?.loan??loans.find(row=>row.id===selectedId)??null;
  const totalLeaving=useMemo(()=>{
    if(!transactionForm||transactionForm.type!=="Repayment")return 0;
    return (Number(transactionForm.principalAmount)||0)+(Number(transactionForm.interestAmount)||0);
  },[transactionForm]);

  const saveLoan=async()=>{
    if(!loanForm||saving)return;
    if(!loanForm.name.trim()||!loanForm.financeAccountId){setError("Enter a loan name and select its liability account.");return;}
    setSaving(true);setError(null);
    try {
      const response=await api(loanForm.id?`/api/finance/loans/${loanForm.id}`:"/api/finance/loans",{
        method:loanForm.id?"PUT":"POST",
        body:JSON.stringify({name:loanForm.name.trim(),lenderName:loanForm.lenderName.trim()||null,
          financeAccountId:Number(loanForm.financeAccountId),isActive:loanForm.isActive,concurrencyToken:loanForm.concurrencyToken})
      });
      if(!response.ok)throw new Error((await response.json().catch(()=>null))?.message??"Loan could not be saved.");
      const saved=await response.json() as Loan;
      setLoanForm(null);setSelectedId(saved.id);await loadLoans();
    } catch(caught) { setError(caught instanceof Error?caught.message:"Loan could not be saved."); }
    finally { setSaving(false); }
  };

  const saveTransaction=async()=>{
    if(!selected||!transactionForm||saving)return;
    const principal=Number(transactionForm.principalAmount||0), interest=Number(transactionForm.interestAmount||0);
    if(!Number.isFinite(principal)||!Number.isFinite(interest)||principal<0||interest<0){setError("Enter valid non-negative principal and interest amounts.");return;}
    if(transactionForm.type==="Drawdown"&&principal<=0){setError("Enter the principal received.");return;}
    if(transactionForm.type==="Repayment"&&principal+interest<=0){setError("A repayment must contain principal, interest, or both.");return;}
    if(!transactionForm.financeAccountId){setError("Select the cash or bank account used.");return;}
    setSaving(true);setError(null);
    try {
      const path=transactionForm.id
        ?`/api/finance/loans/${selected.id}/transactions/${transactionForm.id}`
        :`/api/finance/loans/${selected.id}/transactions`;
      const response=await api(path,{method:transactionForm.id?"PUT":"POST",body:JSON.stringify({
        type:transactionForm.type,principalAmount:principal,
        interestAmount:transactionForm.type==="Drawdown"?0:interest,date:transactionForm.date,
        financeAccountId:Number(transactionForm.financeAccountId),reference:transactionForm.reference.trim()||null,
        note:transactionForm.note.trim()||null,concurrencyToken:transactionForm.concurrencyToken
      })});
      if(!response.ok)throw new Error((await response.json().catch(()=>null))?.message??"Loan movement could not be saved.");
      setTransactionForm(null);await loadLoans();await loadStatement(selected.id);
    } catch(caught) { setError(caught instanceof Error?caught.message:"Loan movement could not be saved."); }
    finally { setSaving(false); }
  };

  const removeTransaction=async(row:LoanTransaction)=>{
    if(!selected||!confirm(`Delete this ${row.type.toLowerCase()}? The loan and bank balances will both be recalculated.`))return;
    setError(null);
    const response=await api(`/api/finance/loans/${selected.id}/transactions/${row.id}?concurrencyToken=${encodeURIComponent(row.concurrencyToken)}`,{method:"DELETE"});
    if(!response.ok){setError((await response.json().catch(()=>null))?.message??"Loan movement could not be deleted.");return;}
    await loadLoans();await loadStatement(selected.id);
  };

  const editTransaction=(row:LoanTransaction)=>setTransactionForm({
    id:row.id,type:row.type,principalAmount:String(row.principalAmount),interestAmount:String(row.interestAmount),
    date:row.date.slice(0,10),financeAccountId:String(row.financeAccountId),reference:row.reference??"",
    note:row.note??"",concurrencyToken:row.concurrencyToken
  });

  if(user?.role!=="Admin")return null;
  return <Container className="py-8">
    <header className="mb-6 flex flex-wrap items-end justify-between gap-3">
      <div><Link to="/finance" className="text-sm text-[var(--accent)]">← Finance dashboard</Link><h1 className="mt-1 text-3xl font-bold text-[var(--text-heading)]">Loans</h1><p className="text-sm text-[var(--text-muted)]">Actual drawdowns, principal repayments and interest paid — no forecasts or calculated schedules.</p></div>
      <div className="flex gap-2"><Link to="/finance/reports"><Button variant="outline">Financial Reports</Button></Link><Button onClick={()=>setLoanForm(emptyLoan())}>Add loan</Button></div>
    </header>
    {error&&<p role="alert" className="mb-4 rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">{error}</p>}
    <div className="grid gap-5 lg:grid-cols-[320px_minmax(0,1fr)]">
      <aside className="space-y-3">
        {loans.map(loan=><button key={loan.id} onClick={()=>setSelectedId(loan.id)} className={`w-full rounded-2xl border p-4 text-left transition ${selectedId===loan.id?"border-[var(--accent)] bg-[var(--surface-glass)]":"border-[var(--border)] bg-[var(--surface)] hover:border-[var(--accent)]/60"}`}>
          <div className="flex items-start justify-between gap-2"><div><p className="font-semibold text-[var(--text-heading)]">{loan.name}</p><p className="text-xs text-[var(--text-muted)]">{loan.lenderName??loan.financeAccountName}</p></div><span className={`text-xs ${loan.isActive?"text-emerald-400":"text-[var(--text-muted)]"}`}>{loan.isActive?"Active":"Inactive"}</span></div>
          <p className="mt-3 text-xs text-[var(--text-muted)]">Currently owed</p><p className="text-xl font-bold">{money(loan.currentBalance)}</p>
        </button>)}
        {loading&&<p className="rounded-2xl border border-[var(--border)] p-6 text-center text-sm text-[var(--text-muted)]">Loading loans…</p>}
        {!loading&&!loans.length&&<div className="rounded-2xl border border-dashed border-[var(--border)] p-6 text-center"><p className="font-semibold">No loans linked yet</p><p className="mt-1 text-sm text-[var(--text-muted)]">Add a loan and connect its existing Liability account.</p></div>}
      </aside>
      <main>
        {selected?<>
          <section className="mb-5 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5">
            <div className="flex flex-wrap items-start justify-between gap-3"><div><p className="text-sm text-[var(--text-muted)]">{selected.financeAccountName}</p><h2 className="text-2xl font-bold text-[var(--text-heading)]">{selected.name}</h2>{selected.lenderName&&<p className="text-sm text-[var(--text-muted)]">Lender: {selected.lenderName}</p>}</div><div className="flex gap-2"><Button variant="outline" onClick={()=>setLoanForm({id:selected.id,name:selected.name,lenderName:selected.lenderName??"",financeAccountId:String(selected.financeAccountId),isActive:selected.isActive,concurrencyToken:selected.concurrencyToken})}>Edit loan</Button><Button variant="outline" disabled={!selected.isActive} onClick={()=>setTransactionForm(emptyTransaction("Drawdown"))}>Money received</Button><Button disabled={!selected.isActive} onClick={()=>setTransactionForm(emptyTransaction("Repayment"))}>Record repayment</Button></div></div>
            <div className="mt-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-4"><Stat label="Currently owed" value={money(selected.currentBalance)} strong/><Stat label="Principal drawn" value={money(selected.drawnPrincipal)}/><Stat label="Principal repaid" value={money(selected.repaidPrincipal)}/><Stat label="Interest paid" value={money(selected.interestPaid)}/></div>
            {selected.openingBalance!==0&&<p className="mt-3 text-xs text-[var(--text-muted)]">Includes {money(selected.openingBalance)} brought forward as the liability account opening balance.</p>}
          </section>
          <section className="overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)]">
            <div className="border-b border-[var(--border)] p-4"><h3 className="font-semibold text-[var(--text-heading)]">Movement history</h3><p className="text-xs text-[var(--text-muted)]">Running balance is principal still owed after each movement. Interest never changes it.</p></div>
            <div className="overflow-x-auto"><table className="w-full min-w-[850px] text-sm"><thead><tr className="border-b border-[var(--border)] text-left text-[var(--text-muted)]">{["Date / movement","Principal","Interest","Bank movement","Running balance","Actions"].map(label=><th key={label} className="p-3">{label}</th>)}</tr></thead><tbody>{statement?.items.map(row=><tr key={row.id} className="border-b border-[var(--border)] align-top"><td className="p-3"><p className={`font-semibold ${row.type==="Drawdown"?"text-emerald-300":"text-[var(--text-heading)]"}`}>{row.type==="Drawdown"?"Money received":"Repayment"}</p><p className="text-xs text-[var(--text-muted)]">{new Date(row.date).toLocaleDateString("en-GB")} · {row.financeAccountName}</p>{row.reference&&<p className="text-xs text-[var(--text-muted)]">Ref: {row.reference}</p>}{row.note&&<p className="mt-1 max-w-sm text-xs text-[var(--text-muted)]">{row.note}</p>}</td><td className="p-3">{money(row.principalAmount)}<p className="text-xs text-[var(--text-muted)]">{row.type==="Drawdown"?"owed ↑":"owed ↓"}</p></td><td className="p-3">{row.interestAmount?money(row.interestAmount):"—"}{row.interestAmount>0&&<p className="text-xs text-amber-300">P&L cost</p>}</td><td className={`p-3 font-semibold ${row.type==="Drawdown"?"text-emerald-300":"text-rose-300"}`}>{row.type==="Drawdown"?"+":"−"}{money(row.totalCashMovement)}</td><td className="p-3 font-bold">{money(row.runningBalance)}</td><td className="p-3"><div className="flex gap-3"><button className="text-[var(--accent)]" onClick={()=>editTransaction(row)}>Correct</button><button className="text-rose-300" onClick={()=>void removeTransaction(row)}>Delete</button></div></td></tr>)}</tbody></table></div>
            {loadingStatement&&<p className="p-6 text-center text-sm text-[var(--text-muted)]">Loading statement…</p>}
            {!loadingStatement&&!statement?.items.length&&<p className="p-8 text-center text-sm text-[var(--text-muted)]">No movements recorded for this loan.</p>}
            {statement?.hasMore&&<div className="p-4 text-center"><Button variant="outline" disabled={loadingStatement} onClick={()=>void loadStatement(selected.id,statement.items.length)}>Load older movements</Button></div>}
          </section>
        </>:<div className="rounded-2xl border border-dashed border-[var(--border)] p-12 text-center text-[var(--text-muted)]">Select a loan to view its statement.</div>}
      </main>
    </div>
    {loanForm&&<Modal title={loanForm.id?"Edit loan":"Add loan"} close={()=>!saving&&setLoanForm(null)}><div className="space-y-4"><Field label="Loan name" value={loanForm.name} set={value=>setLoanForm({...loanForm,name:value})}/><Field label="Lender name (optional)" value={loanForm.lenderName} set={value=>setLoanForm({...loanForm,lenderName:value})}/><Select label="Loan liability account" value={loanForm.financeAccountId} set={value=>setLoanForm({...loanForm,financeAccountId:value})}><option value="">Select existing Liability account</option>{loanAccounts.filter(account=>(account.isActive||String(account.id)===loanForm.financeAccountId)&&(!account.linkedLoanId||account.linkedLoanId===loanForm.id)).map(account=><option key={account.id} value={account.id}>{account.name} · {account.accountHolderName}{account.isActive?"":" (Inactive)"}</option>)}</Select><label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={loanForm.isActive} onChange={event=>setLoanForm({...loanForm,isActive:event.target.checked})}/> Active — allow new movements</label><p className="rounded-xl bg-[var(--surface-glass)] p-3 text-xs text-[var(--text-muted)]">The selected account carries principal owed on the Balance Sheet. Any opening balance already on it is treated as principal brought forward.</p><div className="flex justify-end gap-2"><Button variant="ghost" disabled={saving} onClick={()=>setLoanForm(null)}>Cancel</Button><Button disabled={saving} onClick={()=>void saveLoan()}>{saving?"Saving…":"Save loan"}</Button></div></div></Modal>}
    {transactionForm&&selected&&<Modal title={`${transactionForm.id?"Correct":"Record"} ${transactionForm.type.toLowerCase()} · ${selected.name}`} close={()=>!saving&&setTransactionForm(null)}><div className="space-y-4"><Select label="Movement" value={transactionForm.type} set={value=>setTransactionForm({...transactionForm,type:value as "Drawdown"|"Repayment",interestAmount:value==="Drawdown"?"":transactionForm.interestAmount})}><option value="Drawdown">Money received (drawdown)</option><option value="Repayment">Repayment</option></Select><div className={`grid gap-3 ${transactionForm.type==="Repayment"?"sm:grid-cols-2":""}`}><Field label={transactionForm.type==="Drawdown"?"Principal received":"Principal portion"} type="number" value={transactionForm.principalAmount} set={value=>setTransactionForm({...transactionForm,principalAmount:value})}/>{transactionForm.type==="Repayment"&&<Field label="Interest portion" type="number" value={transactionForm.interestAmount} set={value=>setTransactionForm({...transactionForm,interestAmount:value})}/>}</div>{transactionForm.type==="Repayment"&&<div className="flex items-center justify-between rounded-xl border border-amber-500/25 bg-amber-500/[0.07] p-4"><div><p className="text-sm font-semibold text-amber-200">Total leaving the bank</p><p className="text-xs text-[var(--text-muted)]">Principal + interest</p></div><strong className="text-xl">{money(totalLeaving)}</strong></div>}<Field label="Date" type="date" value={transactionForm.date} set={value=>setTransactionForm({...transactionForm,date:value})}/><Select label={transactionForm.type==="Drawdown"?"Received in account":"Paid from account"} value={transactionForm.financeAccountId} set={value=>setTransactionForm({...transactionForm,financeAccountId:value})}><option value="">Select cash or bank account</option>{cashAccounts.filter(account=>account.isActive||String(account.id)===transactionForm.financeAccountId).map(account=><option key={account.id} value={account.id}>{account.name} · {account.accountHolderName}{account.isActive?"":" (Inactive)"}</option>)}</Select><Field label="Reference (optional)" value={transactionForm.reference} set={value=>setTransactionForm({...transactionForm,reference:value})}/><label className="block text-sm text-[var(--text-muted)]">Note (optional)<textarea className="mt-1 min-h-24 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3" value={transactionForm.note} onChange={event=>setTransactionForm({...transactionForm,note:event.target.value})}/></label><div className="flex justify-end gap-2"><Button variant="ghost" disabled={saving} onClick={()=>setTransactionForm(null)}>Cancel</Button><Button disabled={saving} onClick={()=>void saveTransaction()}>{saving?"Saving…":transactionForm.id?"Save correction":"Record movement"}</Button></div></div></Modal>}
  </Container>;
}

function Stat({label,value,strong=false}:{label:string;value:string;strong?:boolean}){return <div className="rounded-xl bg-[var(--surface-glass)] p-4"><p className="text-xs text-[var(--text-muted)]">{label}</p><p className={`${strong?"text-xl":"text-lg"} font-bold`}>{value}</p></div>}
function Field({label,value,set,type="text"}:{label:string;value:string;set:(value:string)=>void;type?:string}){return <label className="block text-sm text-[var(--text-muted)]">{label}<input min={type==="number"?"0":undefined} step={type==="number"?"0.01":undefined} className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3" type={type} value={value} onChange={event=>set(event.target.value)}/></label>}
function Select({label,value,set,children}:{label:string;value:string;set:(value:string)=>void;children:React.ReactNode}){return <label className="block text-sm text-[var(--text-muted)]">{label}<select className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3" value={value} onChange={event=>set(event.target.value)}>{children}</select></label>}
function Modal({title,close,children}:{title:string;close:()=>void;children:React.ReactNode}){return <div className="fixed inset-0 z-50 flex items-center justify-center p-4"><button aria-label="Close" className="absolute inset-0 bg-black/60" onClick={close}/><div className="relative max-h-[92dvh] w-full max-w-2xl overflow-y-auto rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6"><div className="mb-5 flex items-center justify-between gap-3"><h2 className="text-xl font-bold">{title}</h2><button aria-label="Close dialog" onClick={close}>✕</button></div>{children}</div></div>}
