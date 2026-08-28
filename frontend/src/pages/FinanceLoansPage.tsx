import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { User } from "../App";
import { api } from "../api/api";
import Button from "../lib/Button";
import Container from "../lib/Container";
import ModalPortal from "../lib/ModalPortal";
import FinanceAttachmentField from "../components/FinanceAttachmentField";
import { financeApiError, openAttachmentAt, type FinanceAttachmentInfo } from "../api/financeAttachments";
import { pakistanToday } from "../lib/financePeriods";
import { moneyRequest, useIdempotencyKeys } from "../lib/idempotency";

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
  attachment:FinanceAttachmentInfo|null;
};
type Statement = { loan:Loan; items:LoanTransaction[]; hasMore:boolean };
type LoanForm = { id:number|null; name:string; lenderName:string; financeAccountId:string; isActive:boolean; concurrencyToken:string };
type TransactionForm = {
  id:number|null; type:"Drawdown"|"Repayment"; principalAmount:string; interestAmount:string;
  date:string; financeAccountId:string; reference:string; note:string; concurrencyToken:string;
  attachment:FinanceAttachmentInfo|null; selectedAttachment:File|null; removeAttachment:boolean;
};
type StatusFilter = "all"|"active"|"closed";

const money = (value:number) => `Rs ${value.toLocaleString("en-PK", { minimumFractionDigits:2, maximumFractionDigits:2 })}`;
/** Table figures carry their unit in the column heading, so the rows print digits only. Signed,
 *  because a balance that ever came back negative must read as negative, not be silently flipped. */
const figure = (value:number) => value.toLocaleString("en-PK", { minimumFractionDigits:2, maximumFractionDigits:2 });
/** For the two columns whose direction is stated by the movement type rather than by the number:
 *  the API sends both as unsigned magnitudes, and the row supplies the sign. */
const magnitude = (value:number) => figure(Math.abs(value));
const longDate = (value:string) => new Date(value).toLocaleDateString("en-GB", { day:"2-digit", month:"short", year:"numeric" });
const today = pakistanToday;
const emptyLoan = ():LoanForm => ({id:null,name:"",lenderName:"",financeAccountId:"",isActive:true,concurrencyToken:""});
const emptyTransaction = (type:"Drawdown"|"Repayment"):TransactionForm => ({
  id:null,type,principalAmount:"",interestAmount:"",date:today(),financeAccountId:"",reference:"",note:"",concurrencyToken:"",
  attachment:null,selectedAttachment:null,removeAttachment:false
});

/** What a repayment was actually made of, so the row says it rather than assuming both halves. */
const movementCaption = (row:LoanTransaction) => {
  if(row.type==="Drawdown")return "Drawdown";
  if(row.principalAmount>0&&row.interestAmount>0)return "Principal + interest";
  return row.interestAmount>0?"Interest only":"Principal only";
};

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
  const [search,setSearch]=useState("");
  const [statusFilter,setStatusFilter]=useState<StatusFilter>("all");
  const idempotency=useIdempotencyKeys();

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

  // Which loan the newest statement request was for. Responses arrive in whatever order the
  // network gives them, so a slow request for loan A must not be allowed to land after a fast one
  // for loan B and leave the screen showing A while the sidebar highlights B.
  const statementRequest=useRef(0);

  const loadStatement=useCallback(async(id:number,skip=0)=>{
    const request=++statementRequest.current;
    setLoadingStatement(true);setError(null);
    try {
      const response=await api(`/api/finance/loans/${id}/statement?skip=${skip}&take=100`);
      if(request!==statementRequest.current)return;
      if(!response.ok)throw new Error((await response.json().catch(()=>null))?.message??"Loan statement could not be loaded.");
      const next=await response.json() as Statement;
      if(request!==statementRequest.current)return;
      setStatement(current=>skip>0&&current?.loan.id===id?{...next,items:[...current.items,...next.items]}:next);
    } catch(caught) {
      if(request!==statementRequest.current)return;
      setError(caught instanceof Error?caught.message:"Loan statement could not be loaded.");
    } finally { if(request===statementRequest.current)setLoadingStatement(false); }
  },[]);

  useEffect(()=>{if(user?.role!=="Admin"){navigate("/");return;}void loadLoans();},[user,navigate,loadLoans]);
  useEffect(()=>{if(selectedId)void loadStatement(selectedId);else setStatement(null);},[selectedId,loadStatement]);

  // Read from the loan list, not from the statement. The list is keyed by the same id the sidebar
  // highlights and is refetched after every save, so the loan on screen, the loan its buttons act
  // on, and the loan whose concurrency token an edit submits are the same row by construction —
  // rather than three things that happen to agree until a response arrives out of order or an edit
  // leaves the statement's copy stale.
  const selected=loans.find(row=>row.id===selectedId)??null;
  // The statement is only this loan's while its id matches; otherwise it is a stale response or
  // one still in flight, and showing its rows under this loan's heading would be a lie.
  const shownStatement=statement&&statement.loan.id===selectedId?statement:null;
  const totalLeaving=useMemo(()=>{
    if(!transactionForm||transactionForm.type!=="Repayment")return 0;
    return (Number(transactionForm.principalAmount)||0)+(Number(transactionForm.interestAmount)||0);
  },[transactionForm]);

  // Search and status narrow what the sidebar lists, never what the page works with: `selected` is
  // still resolved against the full list, so filtering a loan out of view cannot change which loan
  // a movement is recorded against.
  const visibleLoans=useMemo(()=>{
    const term=search.trim().toLowerCase();
    return loans.filter(loan=>
      (statusFilter==="all"||(statusFilter==="active")===loan.isActive)&&
      (!term||loan.name.toLowerCase().includes(term)
        ||(loan.lenderName??"").toLowerCase().includes(term)
        ||loan.financeAccountName.toLowerCase().includes(term)));
  },[loans,search,statusFilter]);

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
      // Always the multipart route, with or without a file: one path means the evidence is saved in
      // the same request as the figures it supports, and there is no second step to half-fail.
      const path=transactionForm.id
        ?`/api/finance/loans/${selected.id}/transactions/${transactionForm.id}/form`
        :`/api/finance/loans/${selected.id}/transactions/form`;
      const body=new FormData();
      body.append("type",transactionForm.type);
      body.append("principalAmount",String(principal));
      body.append("interestAmount",String(transactionForm.type==="Drawdown"?0:interest));
      body.append("date",transactionForm.date);
      body.append("financeAccountId",transactionForm.financeAccountId);
      if(transactionForm.reference.trim())body.append("reference",transactionForm.reference.trim());
      if(transactionForm.note.trim())body.append("note",transactionForm.note.trim());
      body.append("concurrencyToken",transactionForm.concurrencyToken);
      if(transactionForm.selectedAttachment)body.append("attachment",transactionForm.selectedAttachment);
      if(transactionForm.removeAttachment)body.append("removeAttachment","true");
      // New movements only: an edit already carries a row version, and it is the insert that can be
      // duplicated by a retry the operator cannot see the result of.
      const signature=`loan:${selected.id}:${transactionForm.type}:${principal}:${interest}:${transactionForm.date}`;
      const request=transactionForm.id
        ?{method:"PUT",body}
        :moneyRequest(idempotency.key(signature,"loan-movement"),{method:"POST",body});
      const response=await api(path,request);
      if(!response.ok)throw new Error(await financeApiError(response,"Loan movement could not be saved."));
      if(!transactionForm.id)idempotency.release(signature);
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
    note:row.note??"",concurrencyToken:row.concurrencyToken,
    attachment:row.attachment,selectedAttachment:null,removeAttachment:false
  });

  // Failures here belong to one row, not to the statement, so they are shown in the page banner
  // rather than replacing the movement history with an error.
  const viewAttachment=async(row:LoanTransaction,download:boolean)=>{
    if(!selected||!row.attachment)return;
    try {
      await openAttachmentAt(`/api/finance/loans/${selected.id}/transactions/${row.id}/attachment`,row.attachment.fileName,download);
    } catch(caught) { setError(caught instanceof Error?caught.message:"The attachment could not be opened."); }
  };

  // Exports exactly the rows on screen — the same figures with the same signs — so a spreadsheet
  // and the page can never disagree. Movements not yet loaded are not invented here.
  const exportActivity=()=>{
    if(!selected||!shownStatement?.items.length)return;
    const header=["Date","Transaction","Detail","Account","Reference","Note","Principal (Rs)","Interest (Rs)","Cash impact (Rs)","Outstanding principal (Rs)"];
    const lines=shownStatement.items.map(row=>{
      const sign=row.type==="Drawdown"?1:-1;
      return [
        csvText(row.date.slice(0,10)),
        csvText(row.type==="Drawdown"?"Funds Received":"Repayment"),
        csvText(movementCaption(row)),
        csvText(row.financeAccountName),
        csvText(row.reference??""),
        csvText(row.note??""),
        (sign*row.principalAmount).toFixed(2),
        row.interestAmount.toFixed(2),
        (sign*row.totalCashMovement).toFixed(2),
        row.runningBalance.toFixed(2)
      ].join(",");
    });
    // The BOM is what makes Excel read the file as UTF-8 rather than the local code page.
    const csv=`\uFEFF${[header.map(csvText).join(","),...lines].join("\r\n")}\r\n`;
    const url=URL.createObjectURL(new Blob([csv],{type:"text/csv;charset=utf-8"}));
    const link=document.createElement("a");
    link.href=url;
    link.download=`${selected.name.replace(/[^a-z0-9]+/gi,"-").replace(/^-|-$/g,"").toLowerCase()||"loan"}-activity.csv`;
    document.body.appendChild(link);link.click();link.remove();
    URL.revokeObjectURL(url);
  };

  if(user?.role!=="Admin")return null;

  const stats=selected?[
    {label:"Outstanding principal",value:money(selected.currentBalance),hint:undefined as string|undefined},
    {label:"Total borrowed",value:money(selected.drawnPrincipal),hint:undefined as string|undefined},
    {label:"Principal repaid",value:money(selected.repaidPrincipal),hint:undefined as string|undefined},
    {label:"Interest paid",value:money(selected.interestPaid),
      hint:"Interest is a profit & loss cost. It never reduces the principal you owe."}
  ]:[];
  const rows=shownStatement?.items??[];

  return <Container className="py-8">
    <header className="border-b border-[var(--border)] pb-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link to="/finance" className="inline-flex items-center gap-1.5 text-sm font-medium text-[var(--accent)] transition hover:text-[var(--accent-light)]">
            <IconArrowLeft className="h-3.5 w-3.5"/>Back to Finance Dashboard
          </Link>
          <h1 className="mt-2 text-3xl font-bold tracking-tight text-[var(--text-heading)]">Loans</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">Manage company loans, drawdowns, repayments and interest payments.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link to="/finance/reports"><Button variant="outline"><IconReport className="h-4 w-4"/>Financial Reports</Button></Link>
          <Button onClick={()=>setLoanForm(emptyLoan())}><IconPlus className="h-4 w-4"/>Add New Loan</Button>
        </div>
      </div>
    </header>

    {error&&<p role="alert" className="mt-5 rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">{error}</p>}

    <div className="grid gap-6 lg:grid-cols-[320px_minmax(0,1fr)] lg:gap-0">
      <aside className="pt-6 lg:border-r lg:border-[var(--border)] lg:pr-6">
        <h2 className="text-lg font-semibold text-[var(--text-heading)]">Your Loans</h2>
        <div className="mt-4 flex items-center gap-2">
          <div className="relative min-w-0 flex-1">
            <span aria-hidden="true" className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-[var(--text-muted)]"><IconSearch className="h-4 w-4"/></span>
            <input
              type="search" value={search} onChange={event=>setSearch(event.target.value)}
              placeholder="Search loan or lender..." aria-label="Search loans"
              className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] py-2 pl-9 pr-3 text-sm text-[var(--text-primary)] outline-none transition placeholder:text-[var(--text-muted)] focus:border-[var(--border-hover)]"/>
          </div>
          {/* A native select, styled as the mock's button: the platform's own dropdown on every
              device, with only its arrow replaced so the search field keeps its width. */}
          <div className="relative shrink-0">
            <select
              value={statusFilter} onChange={event=>setStatusFilter(event.target.value as StatusFilter)} aria-label="Filter loans by status"
              className="w-full cursor-pointer appearance-none rounded-xl border border-[var(--border)] bg-[var(--surface)] py-2 pl-3 pr-7 text-xs font-semibold text-[var(--text-secondary)] outline-none transition hover:border-[var(--border-hover)] focus:border-[var(--border-hover)]">
              <option value="all">All Status</option>
              <option value="active">Active</option>
              <option value="closed">Closed</option>
            </select>
            <span aria-hidden="true" className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 text-[var(--text-muted)]"><IconChevronDown className="h-3 w-3"/></span>
          </div>
        </div>

        <div className="mt-4 space-y-3">
          {visibleLoans.map(loan=>
            <button
              key={loan.id} type="button" onClick={()=>setSelectedId(loan.id)}
              aria-current={selectedId===loan.id?"true":undefined}
              className={`w-full cursor-pointer rounded-2xl border p-4 text-left transition ${selectedId===loan.id
                ?"border-[var(--accent)] bg-[var(--surface-glass)] shadow-[0_0_18px_var(--accent-glow)]"
                :"border-[var(--border)] bg-[var(--surface)] hover:border-[var(--border-hover)] hover:bg-[var(--surface-hover)]"}`}>
              <div className="flex items-start justify-between gap-2">
                <p className="min-w-0 font-semibold text-[var(--text-heading)]">{loan.name}</p>
                <StatusDot active={loan.isActive}/>
              </div>
              {loan.lenderName&&<p className="mt-2 truncate text-xs text-[var(--text-muted)]">Lender: {loan.lenderName}</p>}
              <p className="mt-0.5 truncate text-xs text-[var(--text-muted)]">Loan account: {loan.financeAccountName}</p>
              <p className="mt-3 text-[10px] font-semibold uppercase tracking-[0.09em] text-[var(--text-muted)]">Outstanding principal</p>
              <p className="mt-0.5 text-lg font-bold tabular-nums text-[var(--text-heading)]">{money(loan.currentBalance)}</p>
            </button>)}

          {loading&&<p className="rounded-2xl border border-[var(--border)] p-6 text-center text-sm text-[var(--text-muted)]">Loading loans…</p>}
          {!loading&&!loans.length&&<div className="rounded-2xl border border-dashed border-[var(--border)] p-6 text-center">
            <p className="font-semibold text-[var(--text-heading)]">No loans linked yet</p>
            <p className="mt-1 text-sm text-[var(--text-muted)]">Add a loan and connect its existing Liability account.</p>
          </div>}
          {!loading&&!!loans.length&&!visibleLoans.length&&<p className="rounded-2xl border border-dashed border-[var(--border)] p-6 text-center text-sm text-[var(--text-muted)]">No loan matches this search or status.</p>}
        </div>
      </aside>

      <main className="min-w-0 pt-6 lg:pl-6">
        {selected?<>
          <section className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-6">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-3">
                  <h2 className="text-2xl font-bold text-[var(--text-heading)]">{selected.name}</h2>
                  <StatusPill active={selected.isActive}/>
                </div>
                <p className="mt-2 text-sm text-[var(--text-muted)]">
                  {selected.lenderName&&<>Lender: {selected.lenderName}<span className="px-2">•</span></>}
                  Loan account: {selected.financeAccountName}
                </p>
              </div>
              <button
                type="button" title="Edit loan details"
                onClick={()=>setLoanForm({id:selected.id,name:selected.name,lenderName:selected.lenderName??"",
                  financeAccountId:String(selected.financeAccountId),isActive:selected.isActive,concurrencyToken:selected.concurrencyToken})}
                className="inline-flex shrink-0 cursor-pointer items-center gap-2 rounded-xl border border-[var(--border)] px-3 py-2 text-xs font-semibold text-[var(--text-secondary)] transition hover:border-[var(--border-hover)] hover:text-[var(--text-heading)]">
                <IconPencil className="h-3.5 w-3.5"/>Edit
              </button>
            </div>

            <div className="mt-7 grid grid-cols-1 gap-y-6 sm:grid-cols-2 xl:grid-cols-4">
              {stats.map((stat,index)=>
                <div key={stat.label} className={`${index%2===1?"sm:border-l sm:border-[var(--border)] sm:pl-5":""} ${index===0?"xl:border-l-0 xl:pl-0":"xl:border-l xl:border-[var(--border)] xl:pl-5"}`}>
                  <p className="flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-[0.09em] text-[var(--text-muted)]">
                    {stat.label}
                    {stat.hint&&<span title={stat.hint} className="inline-flex text-[var(--text-muted)]">
                      <IconInfo className="h-3.5 w-3.5"/><span className="sr-only">{stat.hint}</span>
                    </span>}
                  </p>
                  <p className="mt-1.5 whitespace-nowrap text-xl font-bold tabular-nums text-[var(--text-heading)]">{stat.value}</p>
                </div>)}
            </div>

            {selected.openingBalance!==0&&<p className="mt-5 text-xs text-[var(--text-muted)]">
              Outstanding principal includes {money(selected.openingBalance)} brought forward as the liability account opening balance, which sits outside “Total borrowed”.
            </p>}

            <div className="mt-6 flex flex-wrap items-center gap-3">
              <ActionButton
                tone="primary" icon={<IconCard className="h-5 w-5"/>} title="Record Repayment"
                subtitle="Record principal + interest payment" disabled={!selected.isActive}
                onClick={()=>setTransactionForm(emptyTransaction("Repayment"))}/>
              <ActionButton
                tone="quiet" icon={<IconDownload className="h-5 w-5"/>} title="Receive Loan Funds"
                subtitle="Record a new drawdown" disabled={!selected.isActive}
                onClick={()=>setTransactionForm(emptyTransaction("Drawdown"))}/>
              {!selected.isActive&&<p className="text-xs text-[var(--text-muted)]">This loan is closed — reopen it from Edit to record new movements.</p>}
            </div>
          </section>

          <section className="mt-6 overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)]">
            <div className="flex flex-wrap items-center justify-between gap-3 p-5">
              <div>
                <h3 className="text-lg font-semibold text-[var(--text-heading)]">Loan Activity</h3>
                <p className="mt-0.5 text-xs text-[var(--text-muted)]">All loan transactions in chronological order.</p>
              </div>
              <Button size="sm" variant="outline" disabled={!rows.length} onClick={exportActivity} title="Download the movements shown here as a CSV file">
                <IconDownload className="h-3.5 w-3.5"/>Export
              </Button>
            </div>

            <div className="overflow-x-auto">
              <table className="w-full min-w-[1240px] text-sm">
                <thead>
                  <tr className="border-y border-[var(--border)] bg-[var(--surface-glass)] text-left">
                    {["Date","Transaction","Account","Principal (Rs)","Interest (Rs)","Cash impact (Rs)","Outstanding principal (Rs)","Attachment","Actions"].map(label=>
                      <th key={label} className="whitespace-nowrap px-5 py-3 text-[10px] font-semibold uppercase tracking-[0.09em] text-[var(--text-muted)]">{label}</th>)}
                  </tr>
                </thead>
                <tbody>
                  {rows.map(row=>{
                    const received=row.type==="Drawdown";
                    return <tr key={row.id} className="border-b border-[var(--border)] align-top transition last:border-b-0 hover:bg-[var(--surface-glass)]">
                      <td className="whitespace-nowrap px-5 py-4 text-[var(--text-secondary)]">{longDate(row.date)}</td>
                      <td className="px-5 py-4">
                        <div className="flex items-start gap-3">
                          <span aria-hidden="true" className={`mt-0.5 inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-full ${received?"bg-emerald-500/10 text-emerald-400":"bg-rose-500/10 text-rose-400"}`}>
                            {received?<IconArrowUp className="h-3.5 w-3.5"/>:<IconArrowDown className="h-3.5 w-3.5"/>}
                          </span>
                          <div className="min-w-0">
                            <p className="whitespace-nowrap font-semibold text-[var(--text-heading)]">{received?"Funds Received":"Repayment"}</p>
                            <p className="whitespace-nowrap text-xs text-[var(--text-muted)]">{movementCaption(row)}</p>
                            {row.reference&&<p className="mt-1 text-xs text-[var(--text-muted)]">Ref: {row.reference}</p>}
                            {row.note&&<p className="mt-1 max-w-xs text-xs text-[var(--text-muted)]">{row.note}</p>}
                          </div>
                        </div>
                      </td>
                      <td className="whitespace-nowrap px-5 py-4 text-[var(--text-secondary)]">{row.financeAccountName}</td>
                      <td className={`whitespace-nowrap px-5 py-4 font-medium tabular-nums ${received?"text-emerald-400":"text-rose-400"}`}>
                        {received?"":"-"}{magnitude(row.principalAmount)}
                      </td>
                      <td className="whitespace-nowrap px-5 py-4 tabular-nums">
                        {row.interestAmount>0?<span className="font-medium text-amber-300">{figure(row.interestAmount)}</span>:<span className="text-[var(--text-muted)]">—</span>}
                      </td>
                      <td className={`whitespace-nowrap px-5 py-4 font-medium tabular-nums ${received?"text-emerald-400":"text-rose-400"}`}>
                        {received?"":"-"}{magnitude(row.totalCashMovement)}
                      </td>
                      <td className="whitespace-nowrap px-5 py-4 font-semibold tabular-nums text-[var(--text-heading)]">{figure(row.runningBalance)}</td>
                      <td className="px-5 py-4">
                        {row.attachment
                          ?<span className="inline-flex items-center gap-2 whitespace-nowrap">
                            <button type="button" onClick={()=>void viewAttachment(row,false)}
                              className="inline-flex cursor-pointer items-center gap-1.5 rounded-full border border-[var(--border)] bg-[var(--surface-glass)] px-2.5 py-1 text-[10px] font-semibold text-[var(--accent-light)] transition hover:border-[var(--border-hover)] hover:bg-[var(--surface-glass-hover)]">
                              <IconPaperclip className="h-3 w-3"/>Attached
                            </button>
                            <button type="button" aria-label={`Download ${row.attachment.fileName}`} title={`Download ${row.attachment.fileName}`}
                              onClick={()=>void viewAttachment(row,true)}
                              className="cursor-pointer text-[var(--text-muted)] transition hover:text-[var(--text-primary)]">
                              <IconDownload className="h-3.5 w-3.5"/>
                            </button>
                          </span>
                          :<span className="text-xs text-[var(--text-muted)]">None</span>}
                      </td>
                      <td className="px-5 py-4">
                        <div className="flex items-center gap-3 whitespace-nowrap text-xs font-semibold">
                          <button type="button" className="cursor-pointer text-[var(--accent)] transition hover:text-[var(--accent-light)]" onClick={()=>editTransaction(row)}>Correct</button>
                          <button type="button" className="cursor-pointer text-rose-400 transition hover:text-rose-300" onClick={()=>void removeTransaction(row)}>Delete</button>
                        </div>
                      </td>
                    </tr>;
                  })}
                </tbody>
              </table>
            </div>

            {loadingStatement&&<p className="p-6 text-center text-sm text-[var(--text-muted)]">Loading statement…</p>}
            {!loadingStatement&&!rows.length&&<p className="p-10 text-center text-sm text-[var(--text-muted)]">No movements recorded for this loan.</p>}
            {shownStatement?.hasMore&&<div className="border-t border-[var(--border)] p-4 text-center">
              <Button variant="outline" size="sm" disabled={loadingStatement} onClick={()=>void loadStatement(selected.id,rows.length)}>Load older movements</Button>
            </div>}
          </section>
        </>:<div className="rounded-2xl border border-dashed border-[var(--border)] p-12 text-center text-[var(--text-muted)]">Select a loan to view its activity.</div>}
      </main>
    </div>

    {loanForm&&<Modal title={loanForm.id?"Edit loan":"Add new loan"} close={()=>!saving&&setLoanForm(null)}>
      <div className="space-y-4">
        <Field label="Loan name" value={loanForm.name} set={value=>setLoanForm({...loanForm,name:value})}/>
        <Field label="Lender name (optional)" value={loanForm.lenderName} set={value=>setLoanForm({...loanForm,lenderName:value})}/>
        <Select label="Loan liability account" value={loanForm.financeAccountId} set={value=>setLoanForm({...loanForm,financeAccountId:value})}>
          <option value="">Select existing Liability account</option>
          {loanAccounts.filter(account=>(account.isActive||String(account.id)===loanForm.financeAccountId)&&(!account.linkedLoanId||account.linkedLoanId===loanForm.id))
            .map(account=><option key={account.id} value={account.id}>{account.name} · {account.accountHolderName}{account.isActive?"":" (Inactive)"}</option>)}
        </Select>
        <label className="flex items-center gap-2 text-sm text-[var(--text-secondary)]">
          <input type="checkbox" className="accent-[var(--accent)]" checked={loanForm.isActive} onChange={event=>setLoanForm({...loanForm,isActive:event.target.checked})}/> Active — allow new movements
        </label>
        <p className="rounded-xl bg-[var(--surface-glass)] p-3 text-xs text-[var(--text-muted)]">The selected account carries principal owed on the Balance Sheet. Any opening balance already on it is treated as principal brought forward.</p>
        <div className="flex justify-end gap-2">
          <Button variant="ghost" disabled={saving} onClick={()=>setLoanForm(null)}>Cancel</Button>
          <Button disabled={saving} onClick={()=>void saveLoan()}>{saving?"Saving…":"Save loan"}</Button>
        </div>
      </div>
    </Modal>}

    {transactionForm&&selected&&<Modal
      title={`${transactionForm.id?"Correct":"Record"} ${transactionForm.type==="Drawdown"?"loan funds received":"repayment"} · ${selected.name}`}
      close={()=>!saving&&setTransactionForm(null)}>
      <div className="space-y-4">
        <Select label="Movement" value={transactionForm.type} set={value=>setTransactionForm({...transactionForm,type:value as "Drawdown"|"Repayment",interestAmount:value==="Drawdown"?"":transactionForm.interestAmount})}>
          <option value="Drawdown">Funds received (drawdown)</option>
          <option value="Repayment">Repayment</option>
        </Select>
        <div className={`grid gap-3 ${transactionForm.type==="Repayment"?"sm:grid-cols-2":""}`}>
          <Field label={transactionForm.type==="Drawdown"?"Principal received":"Principal portion"} type="number"
            value={transactionForm.principalAmount} set={value=>setTransactionForm({...transactionForm,principalAmount:value})}/>
          {transactionForm.type==="Repayment"&&<Field label="Interest portion" type="number"
            value={transactionForm.interestAmount} set={value=>setTransactionForm({...transactionForm,interestAmount:value})}/>}
        </div>
        {transactionForm.type==="Repayment"&&<div className="flex items-center justify-between rounded-xl border border-amber-500/25 bg-amber-500/[0.07] p-4">
          <div>
            <p className="text-sm font-semibold text-amber-200">Total leaving the bank</p>
            <p className="text-xs text-[var(--text-muted)]">Principal + interest</p>
          </div>
          <strong className="text-xl tabular-nums">{money(totalLeaving)}</strong>
        </div>}
        <Field label="Date" type="date" value={transactionForm.date} set={value=>setTransactionForm({...transactionForm,date:value})}/>
        <Select label={transactionForm.type==="Drawdown"?"Received in account":"Paid from account"} value={transactionForm.financeAccountId}
          set={value=>setTransactionForm({...transactionForm,financeAccountId:value})}>
          <option value="">Select cash or bank account</option>
          {cashAccounts.filter(account=>account.isActive||String(account.id)===transactionForm.financeAccountId)
            .map(account=><option key={account.id} value={account.id}>{account.name} · {account.accountHolderName}{account.isActive?"":" (Inactive)"}</option>)}
        </Select>
        <Field label="Reference (optional)" value={transactionForm.reference} set={value=>setTransactionForm({...transactionForm,reference:value})}/>
        <label className="block text-sm text-[var(--text-muted)]">Note (optional)
          <textarea className="mt-1 min-h-24 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-[var(--text-primary)] outline-none transition focus:border-[var(--border-hover)]"
            value={transactionForm.note} onChange={event=>setTransactionForm({...transactionForm,note:event.target.value})}/>
        </label>
        <FinanceAttachmentField
          existing={transactionForm.attachment} selected={transactionForm.selectedAttachment} removeExisting={transactionForm.removeAttachment} disabled={saving}
          onSelected={file=>setTransactionForm(current=>current?{...current,selectedAttachment:file}:current)}
          onRemoveExisting={remove=>setTransactionForm(current=>current?{...current,removeAttachment:remove}:current)}
          onViewExisting={()=>{const row=shownStatement?.items.find(item=>item.id===transactionForm.id);if(row)void viewAttachment(row,false);}}
          onDownloadExisting={()=>{const row=shownStatement?.items.find(item=>item.id===transactionForm.id);if(row)void viewAttachment(row,true);}}/>
        <div className="flex justify-end gap-2">
          <Button variant="ghost" disabled={saving} onClick={()=>setTransactionForm(null)}>Cancel</Button>
          <Button disabled={saving} onClick={()=>void saveTransaction()}>{saving?"Saving…":transactionForm.id?"Save correction":"Record movement"}</Button>
        </div>
      </div>
    </Modal>}
  </Container>;
}

/** A cell a spreadsheet cannot read as a formula: operator-entered text starting with =, +, - or @
 *  is prefixed so Excel treats it as the words it is. Numbers never pass through here. */
function csvText(value:string){
  const guarded=/^[=+\-@]/.test(value)?`'${value}`:value;
  return `"${guarded.replace(/"/g,'""')}"`;
}

function StatusDot({active}:{active:boolean}){
  return <span className={`inline-flex shrink-0 items-center gap-1.5 text-[10px] font-semibold uppercase tracking-[0.08em] ${active?"text-emerald-400":"text-[var(--text-muted)]"}`}>
    <span aria-hidden="true" className={`h-1.5 w-1.5 rounded-full ${active?"bg-emerald-400":"bg-[var(--text-muted)]"}`}/>
    {active?"Active":"Closed"}
  </span>;
}

function StatusPill({active}:{active:boolean}){
  return <span className={`inline-flex items-center rounded-md border px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.08em] ${active
    ?"border-emerald-500/30 bg-emerald-500/10 text-emerald-400"
    :"border-[var(--border)] bg-[var(--surface-glass)] text-[var(--text-muted)]"}`}>{active?"Active":"Closed"}</span>;
}

function ActionButton({icon,title,subtitle,onClick,disabled,tone}:{
  icon:React.ReactNode; title:string; subtitle:string; onClick:()=>void; disabled?:boolean; tone:"primary"|"quiet";
}){
  const primary=tone==="primary";
  return <button
    type="button" onClick={onClick} disabled={disabled}
    className={`inline-flex cursor-pointer items-center gap-3 rounded-xl px-5 py-3 text-left transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-offset-[var(--bg-primary)] disabled:cursor-not-allowed disabled:opacity-40 ${primary
      ?"bg-gradient-to-r from-[var(--btn-primary-start)] to-[var(--btn-primary-end)] text-[var(--btn-primary-text)] shadow-[var(--btn-primary-shadow)] hover:shadow-lg focus-visible:ring-[var(--accent)] active:scale-[0.98]"
      :"border border-[var(--border)] bg-[var(--surface-glass)] text-[var(--text-heading)] hover:border-[var(--border-hover)] hover:bg-[var(--surface-glass-hover)] focus-visible:ring-[var(--accent-glow)] active:scale-[0.98]"}`}>
    <span aria-hidden="true" className={primary?undefined:"text-[var(--accent)]"}>{icon}</span>
    <span className="min-w-0">
      <span className="block text-sm font-semibold">{title}</span>
      <span className={`block text-[11px] ${primary?"opacity-75":"text-[var(--text-muted)]"}`}>{subtitle}</span>
    </span>
  </button>;
}

function Field({label,value,set,type="text"}:{label:string;value:string;set:(value:string)=>void;type?:string}){
  return <label className="block text-sm text-[var(--text-muted)]">{label}
    <input min={type==="number"?"0":undefined} step={type==="number"?"0.01":undefined}
      className="mt-1 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-[var(--text-primary)] outline-none transition focus:border-[var(--border-hover)]"
      type={type} value={value} onChange={event=>set(event.target.value)}/>
  </label>;
}

function Select({label,value,set,children}:{label:string;value:string;set:(value:string)=>void;children:React.ReactNode}){
  return <label className="block text-sm text-[var(--text-muted)]">{label}
    <select className="mt-1 w-full cursor-pointer rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-3 text-[var(--text-primary)] outline-none transition focus:border-[var(--border-hover)]"
      value={value} onChange={event=>set(event.target.value)}>{children}</select>
  </label>;
}

function Modal({title,close,children}:{title:string;close:()=>void;children:React.ReactNode}){
  return <ModalPortal>
    <div className="fixed inset-0 z-[100] flex items-center justify-center p-4">
      <button aria-label="Close" className="absolute inset-0 bg-black/60 backdrop-blur-sm" onClick={close}/>
      <div className="relative max-h-[92dvh] w-full max-w-2xl overflow-y-auto rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-[var(--shadow-lg)]">
        <div className="mb-5 flex items-start justify-between gap-3">
          <h2 className="text-xl font-bold text-[var(--text-heading)]">{title}</h2>
          <button aria-label="Close dialog" onClick={close} className="shrink-0 cursor-pointer rounded-lg p-1 text-[var(--text-muted)] transition hover:bg-[var(--surface-glass)] hover:text-[var(--text-primary)]">
            <IconClose className="h-4 w-4"/>
          </button>
        </div>
        {children}
      </div>
    </div>
  </ModalPortal>;
}

/* Icons are inline SVG on purpose: nothing to install, version or ship, no runtime cost beyond the
   markup, and each glyph travels inside this page's own chunk. One 24px stroked grid throughout. */
const ico = (className?:string) => ({
  className: className ?? "h-4 w-4", viewBox: "0 0 24 24", fill: "none", stroke: "currentColor",
  strokeWidth: 1.8, strokeLinecap: "round" as const, strokeLinejoin: "round" as const, "aria-hidden": true
});
type IconProps = { className?:string };

function IconArrowLeft({className}:IconProps){return <svg {...ico(className)}><path d="M19 12H5"/><path d="m12 19-7-7 7-7"/></svg>;}
function IconArrowUp({className}:IconProps){return <svg {...ico(className)} strokeWidth={2.2}><path d="M12 19V5"/><path d="m5 12 7-7 7 7"/></svg>;}
function IconArrowDown({className}:IconProps){return <svg {...ico(className)} strokeWidth={2.2}><path d="M12 5v14"/><path d="m19 12-7 7-7-7"/></svg>;}
function IconPlus({className}:IconProps){return <svg {...ico(className)} strokeWidth={2.2}><path d="M12 5v14"/><path d="M5 12h14"/></svg>;}
function IconSearch({className}:IconProps){return <svg {...ico(className)}><circle cx="11" cy="11" r="7"/><path d="m20 20-3.2-3.2"/></svg>;}
function IconReport({className}:IconProps){return <svg {...ico(className)}><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M8 17v-5"/><path d="M12 17V8"/><path d="M16 17v-3"/></svg>;}
function IconCard({className}:IconProps){return <svg {...ico(className)}><rect x="2" y="5" width="20" height="14" rx="2"/><path d="M2 10h20"/><path d="M6 15h4"/></svg>;}
function IconDownload({className}:IconProps){return <svg {...ico(className)}><path d="M12 3v12"/><path d="m7 11 5 5 5-5"/><path d="M4 20h16"/></svg>;}
function IconInfo({className}:IconProps){return <svg {...ico(className)}><circle cx="12" cy="12" r="9"/><path d="M12 11.5V16"/><path d="M12 8h.01"/></svg>;}
function IconPencil({className}:IconProps){return <svg {...ico(className)}><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.12 2.12 0 0 1 3 3L12 15l-4 1 1-4Z"/></svg>;}
function IconPaperclip({className}:IconProps){return <svg {...ico(className)}><path d="M21.4 11.1 12.3 20.2a5 5 0 0 1-7.1-7.1l8.5-8.5a3.5 3.5 0 0 1 5 5l-8.5 8.5a2 2 0 0 1-2.9-2.9l7.8-7.8"/></svg>;}
function IconClose({className}:IconProps){return <svg {...ico(className)} strokeWidth={2.2}><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>;}
function IconChevronDown({className}:IconProps){return <svg {...ico(className)} strokeWidth={2.4}><path d="m6 9 6 6 6-6"/></svg>;}
