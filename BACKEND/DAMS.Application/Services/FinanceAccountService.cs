using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed class FinanceAccountService : IFinanceAccountService
    {
        private readonly AppDbContext _context;

        public FinanceAccountService(AppDbContext context) => _context = context;

        public async Task<PagedResult<FinanceAccountResponseDto>> GetPageAsync(
            string? search, FinanceAccountType? type, string? holder, bool? isActive,
            int skip, int take, CancellationToken cancellationToken = default)
        {
            var query = _context.FinanceAccounts.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(a => a.Name.Contains(term)
                    || a.AccountHolderName.Contains(term)
                    || (a.BankOrWalletName != null && a.BankOrWalletName.Contains(term))
                    || (a.Description != null && a.Description.Contains(term)));
            }
            if (type.HasValue) query = query.Where(a => a.Type == type.Value);
            if (!string.IsNullOrWhiteSpace(holder)) query = query.Where(a => a.AccountHolderName == holder.Trim());
            if (isActive.HasValue) query = query.Where(a => a.IsActive == isActive.Value);

            var rows = await Project(query.OrderByDescending(a => a.IsActive).ThenBy(a => a.Name))
                .Skip(skip).Take(take + 1).ToListAsync(cancellationToken);
            return new PagedResult<FinanceAccountResponseDto>
            {
                Items = rows.Take(take).ToList(),
                HasMore = rows.Count > take
            };
        }

        public Task<List<FinanceAccountOptionDto>> GetOptionsAsync(bool includeInactive, bool cashLikeOnly = true, FinanceAccountType? type = null, CancellationToken cancellationToken = default) =>
            _context.FinanceAccounts.AsNoTracking()
                .Where(a => includeInactive || a.IsActive)
                // An explicit type wins over the cash-like default, so the asset-purchase form can
                // ask for the fixed-asset accounts without also being handed every liability.
                .Where(a => type.HasValue
                    ? a.Type == type.Value
                    : !cashLikeOnly || a.Type == FinanceAccountType.Cash || a.Type == FinanceAccountType.Bank
                        || a.Type == FinanceAccountType.MobileWallet || a.Type == FinanceAccountType.Other)
                .OrderByDescending(a => a.IsActive).ThenBy(a => a.DisplayOrder).ThenBy(a => a.Name)
                .Select(a => new FinanceAccountOptionDto
                {
                    Id = a.Id, Name = a.Name, Type = a.Type,
                    AccountHolderName = a.AccountHolderName, IsActive = a.IsActive
                }).ToListAsync(cancellationToken);

        public async Task<FinanceAccountResponseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            await Project(_context.FinanceAccounts.AsNoTracking().Where(a => a.Id == id))
                .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Finance account not found.");

        public async Task<PagedResult<FinanceAccountTransactionDto>> GetTransactionsAsync(
            int id, int skip, int take, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.AsNoTracking().Where(a => a.Id == id)
                .Select(a => new
                {
                    a.Type,
                    IsTaxPayable = a.SystemRole == FinanceSystemAccountRole.TaxPayable,
                    IsRefundPayable = a.SystemRole == FinanceSystemAccountRole.CustomerRefundPayable,
                    IsCustomerDeposits = a.SystemRole == FinanceSystemAccountRole.CustomerDeposits,
                    IsCustomerReceivables = a.SystemRole == FinanceSystemAccountRole.CustomerReceivables
                }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Finance account not found.");

            // These projections are unioned below, and EF aligns a union on the FIRST branch's
            // member bindings — a property left unset in this first Select is dropped from every
            // branch and silently returns 0. So every branch binds GrossAmount and WhtAmount, even
            // where they only restate Amount.
            var revenue = _context.ManualRevenues.AsNoTracking().Where(r => r.FinanceAccountId == id)
                .Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Revenue", RecordId = r.Id, Date = r.Date, Label = r.RevenueType,
                    Reference = r.Reference, ProjectName = r.Project != null ? r.Project.ProjectName : "General",
                    Amount = r.Amount, GrossAmount = r.Amount, WhtAmount = 0m
                });
            // Net, so the running balance in the detail view matches what the bank statement shows.
            var expenses = _context.Expenses.AsNoTracking().Where(e => e.FinanceAccountId == id)
                .Select(e => new FinanceAccountTransactionDto
                {
                    Kind = "Expense", RecordId = e.Id, Date = e.Date, Label = e.Category,
                    Reference = e.Vendor, ProjectName = e.Project != null ? e.Project.ProjectName : "General",
                    Amount = -(e.Amount - e.WhtAmount), GrossAmount = e.Amount, WhtAmount = e.WhtAmount
                });
            // A purchase appears twice, once on each account it moved, with opposite signs — the
            // bank sees net cash leaving, the asset ledger sees the full price arriving.
            var assetPurchasesPaid = _context.AssetPurchases.AsNoTracking().Where(p => p.FinanceAccountId == id)
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = "Asset purchase", RecordId = p.Id, Date = p.Date, Label = p.ItemName,
                    Reference = p.Vendor, ProjectName = p.Project != null ? p.Project.ProjectName : "General",
                    Amount = -(p.Amount - p.WhtAmount), GrossAmount = p.Amount, WhtAmount = p.WhtAmount
                });
            var assetPurchasesCapitalised = _context.AssetPurchases.AsNoTracking().Where(p => p.AssetAccountId == id)
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = "Asset acquired", RecordId = p.Id, Date = p.Date, Label = p.ItemName,
                    Reference = p.Vendor, ProjectName = p.Project != null ? p.Project.ProjectName : "General",
                    Amount = p.Amount, GrossAmount = p.Amount, WhtAmount = p.WhtAmount
                });
            var whtDeposits = _context.WhtDeposits.AsNoTracking().Where(d => d.FinanceAccountId == id)
                .Select(d => new FinanceAccountTransactionDto
                {
                    Kind = "WHT deposit", RecordId = d.Id, Date = d.DepositDate, Label = "Tax deposited with FBR",
                    Reference = d.ChallanNumber, ProjectName = "General",
                    Amount = -d.Amount, GrossAmount = d.Amount, WhtAmount = 0m
                });
            var commissionPayouts = _context.CommissionPayouts.AsNoTracking().Where(p => p.FinanceAccountId == id)
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = "Commission payout", RecordId = p.Id, Date = p.PaymentDate,
                    Label = p.Commission.Partner.Name, Reference = p.PaymentReference,
                    ProjectName = p.Commission.Booking.Unit.Project.ProjectName,
                    Amount = -p.Amount, GrossAmount = p.Amount, WhtAmount = 0m
                });
            var commissionReversals = _context.CommissionPayoutReversals.AsNoTracking()
                .Where(r => r.Payout.FinanceAccountId == id).Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Commission reversal", RecordId = r.Id, Date = r.ReversedAt,
                    Label = r.Payout.Commission.Partner.Name, Reference = r.Reason,
                    ProjectName = r.Payout.Commission.Booking.Unit.Project.ProjectName,
                    Amount = r.Amount, GrossAmount = r.Amount, WhtAmount = 0m
                });
            var rebatePayments = _context.RebateDisbursements.AsNoTracking()
                .Where(d => d.FinanceAccountId == id && d.Method == CustomerRebateMethod.CashOrBankPayment)
                .Select(d => new FinanceAccountTransactionDto
                {
                    Kind = "Customer rebate", RecordId = d.Id, Date = d.AppliedAt,
                    Label = d.Rebate.Customer.FullName, Reference = d.Reference,
                    ProjectName = d.Rebate.Booking.Unit.Project.ProjectName,
                    Amount = -d.Amount, GrossAmount = d.Amount, WhtAmount = 0m
                });
            var rebateReversals = _context.RebateDisbursementReversals.AsNoTracking()
                .Where(r => r.Disbursement.FinanceAccountId == id
                    && r.Disbursement.Method == CustomerRebateMethod.CashOrBankPayment)
                .Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Rebate reversal", RecordId = r.Id, Date = r.ReversedAt,
                    Label = r.Disbursement.Rebate.Customer.FullName, Reference = r.Reason,
                    ProjectName = r.Disbursement.Rebate.Booking.Unit.Project.ProjectName,
                    Amount = r.Amount, GrossAmount = r.Amount, WhtAmount = 0m
                });
            // Customer money in. Bookings on a cancelled sale are kept, matching the Finance
            // dashboard: the cash really did arrive, and cancelling must not rewrite the bank.
            var payments = _context.Payments.AsNoTracking().Where(p => p.FinanceAccountId == id)
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = "Customer payment", RecordId = p.Id, Date = p.PaidAt,
                    Label = p.Booking.Customer.FullName, Reference = p.ReceiptNumber,
                    ProjectName = p.Booking.Unit.Project.ProjectName,
                    Amount = p.Amount, GrossAmount = p.Amount, WhtAmount = 0m
                });
            // Tax Payable is the credit side of withholding recorded on supplier expenses. The
            // WhtDeposit.FinanceAccountId is the bank account used, so the payable ledger needs
            // explicit derived rows rather than reusing that cash-account relationship.
            var payableWithheld = _context.Expenses.AsNoTracking().Where(e => account.IsTaxPayable && e.WhtAmount != 0m)
                .Select(e => new FinanceAccountTransactionDto
                {
                    Kind = "WHT withheld", RecordId = e.Id, Date = e.Date, Label = e.Category,
                    Reference = e.Vendor, ProjectName = e.Project != null ? e.Project.ProjectName : "General",
                    Amount = e.WhtAmount, GrossAmount = e.Amount, WhtAmount = e.WhtAmount
                });
            var payableWithheldOnAssets = _context.AssetPurchases.AsNoTracking().Where(p => account.IsTaxPayable && p.WhtAmount != 0m)
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = "WHT withheld", RecordId = p.Id, Date = p.Date, Label = p.ItemName,
                    Reference = p.Vendor, ProjectName = p.Project != null ? p.Project.ProjectName : "General",
                    Amount = p.WhtAmount, GrossAmount = p.Amount, WhtAmount = p.WhtAmount
                });
            var payableDeposited = _context.WhtDeposits.AsNoTracking().Where(d => account.IsTaxPayable)
                .Select(d => new FinanceAccountTransactionDto
                {
                    Kind = "WHT deposited", RecordId = d.Id, Date = d.DepositDate, Label = "Tax deposited with FBR",
                    Reference = d.ChallanNumber, ProjectName = "General",
                    Amount = -d.Amount, GrossAmount = d.Amount, WhtAmount = 0m
                });
            // Actual cash refund paid FROM this account.
            var cancellationRefundsCash = _context.BookingCancellationRefunds.AsNoTracking().Where(r => r.FinanceAccountId == id)
                .Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Customer refund", RecordId = r.Id, Date = r.PaidAt,
                    Label = r.Settlement.Booking.Customer.FullName, Reference = r.PaymentReference,
                    ProjectName = r.Settlement.Booking.Unit.Project.ProjectName,
                    Amount = -r.Amount, GrossAmount = r.Amount, WhtAmount = 0m
                });
            // Customer Refunds Payable liability: created when the settlement is decided,
            // cleared when the cash is actually paid out.
            var refundPayableCreated = _context.BookingCancellationSettlements.AsNoTracking()
                .Where(s => account.IsRefundPayable && s.RefundPayableAccountId == id)
                .Select(s => new FinanceAccountTransactionDto
                {
                    Kind = "Customer refund payable", RecordId = s.Id, Date = s.CancellationDate,
                    Label = s.Booking.Customer.FullName, Reference = s.Booking.BookingReference,
                    ProjectName = s.Booking.Unit.Project.ProjectName,
                    Amount = s.RefundAmount, GrossAmount = s.RefundAmount, WhtAmount = 0m
                });
            var refundPayablePaid = _context.BookingCancellationRefunds.AsNoTracking()
                .Where(r => account.IsRefundPayable && r.Settlement.RefundPayableAccountId == id)
                .Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Customer refund paid", RecordId = r.Id, Date = r.PaidAt,
                    Label = r.Settlement.Booking.Customer.FullName, Reference = r.Settlement.Booking.BookingReference,
                    ProjectName = r.Settlement.Booking.Unit.Project.ProjectName,
                    Amount = -r.Amount, GrossAmount = r.Amount, WhtAmount = 0m
                });
            // ── Customer Deposits ledger ──────────────────────────────────────────────────
            // Derived, never stored: the authoritative record of the cash is the Payment row, and
            // writing a second one just to draw this list would be the same money counted twice.
            // "Before the sale was recognised" is an EARLIER BUSINESS DATE, or the same date entered
            // before possession was recorded. RecognitionDate is a Pakistan business date with no
            // time of day, so comparing against it alone made every receipt on the possession day a
            // pre-possession deposit — including the buyer settling their new receivable at 3 PM
            // after a 10 AM handover. The amounts came out right either way (the invented deposit
            // receipt and its immediate clearing cancel), but the ledger described the cash wrongly.
            // The same-day tiebreak is CreatedAt vs RecognizedAt: both are raw UTC audit instants
            // set by the server, so their ordering is the real one and is safe to compare, which
            // neither PaidAt nor RecognitionDate would be. It is <= rather than <, so the one case
            // the two instants cannot separate — a receipt and a handover inside the same clock tick
            // — resolves the way it always did, as a deposit, rather than flipping on a tie.
            // Every payment taken before the sale was recognised is a deposit received…
            var depositsReceived = _context.Payments.AsNoTracking()
                .Where(p => account.IsCustomerDeposits
                    && (p.Booking.SaleRecognition == null
                        || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1) && p.CreatedAt <= p.Booking.SaleRecognition.RecognizedAt))))
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = "Customer deposit received", RecordId = p.Id, Date = p.PaidAt,
                    Label = p.Booking.Customer.FullName, Reference = p.ReceiptNumber,
                    ProjectName = p.Booking.Unit.Project.ProjectName,
                    Amount = p.Amount, GrossAmount = p.Amount, WhtAmount = 0m
                });
            // …and possession clears exactly those deposits into the recognised sale, dated on the
            // recognition date rather than the day the cash arrived.
            var depositsRecognised = _context.Payments.AsNoTracking()
                .Where(p => account.IsCustomerDeposits && p.Booking.CancellationSettlement == null
                    && p.Booking.SaleRecognition != null
                    && (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1) && p.CreatedAt <= p.Booking.SaleRecognition.RecognizedAt)))
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = "Customer deposit recognised", RecordId = p.Id,
                    Date = p.Booking.SaleRecognition!.RecognitionDate,
                    Label = p.Booking.Customer.FullName, Reference = p.Booking.BookingReference,
                    ProjectName = p.Booking.Unit.Project.ProjectName,
                    Amount = -p.Amount, GrossAmount = p.Amount, WhtAmount = 0m
                });
            // A cancellation clears the deposit too — into the refund payable and retained income,
            // never into revenue.
            var depositsCancelled = _context.Payments.AsNoTracking()
                .Where(p => account.IsCustomerDeposits && p.Booking.CancellationSettlement != null)
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = "Customer deposit released on cancellation", RecordId = p.Id,
                    Date = p.Booking.CancellationSettlement!.CancellationDate,
                    Label = p.Booking.Customer.FullName, Reference = p.Booking.BookingReference,
                    ProjectName = p.Booking.Unit.Project.ProjectName,
                    Amount = -p.Amount, GrossAmount = p.Amount, WhtAmount = 0m
                });

            // ── Customer Receivables ledger ───────────────────────────────────────────────
            var receivablesRecognised = _context.BookingSaleRecognitions.AsNoTracking()
                .Where(_ => account.IsCustomerReceivables)
                .Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Customer receivable recognised", RecordId = r.Id, Date = r.RecognitionDate,
                    Label = r.Booking.Customer.FullName, Reference = r.Booking.BookingReference,
                    ProjectName = r.Booking.Unit.Project.ProjectName,
                    Amount = r.NetSaleValue, GrossAmount = r.NetSaleValue, WhtAmount = 0m
                });
            // Every payment on a recognised booking reduces the receivable. Cash taken BEFORE
            // possession does so on the recognition date (it had already cleared the deposit);
            // cash taken after does so on the day it arrived.
            var receivablesSettled = _context.Payments.AsNoTracking()
                .Where(p => account.IsCustomerReceivables && p.Booking.SaleRecognition != null)
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = (p.PaidAt < p.Booking.SaleRecognition!.RecognitionDate || (p.PaidAt < p.Booking.SaleRecognition!.RecognitionDate.AddDays(1) && p.CreatedAt <= p.Booking.SaleRecognition!.RecognizedAt))
                        ? "Deposit applied to sale" : "Customer receivable collected",
                    RecordId = p.Id,
                    Date = (p.PaidAt < p.Booking.SaleRecognition!.RecognitionDate || (p.PaidAt < p.Booking.SaleRecognition!.RecognitionDate.AddDays(1) && p.CreatedAt <= p.Booking.SaleRecognition!.RecognizedAt))
                        ? p.Booking.SaleRecognition!.RecognitionDate : p.PaidAt,
                    Label = p.Booking.Customer.FullName, Reference = p.ReceiptNumber,
                    ProjectName = p.Booking.Unit.Project.ProjectName,
                    Amount = -p.Amount, GrossAmount = p.Amount, WhtAmount = 0m
                });
            // Non-cash customer credits (balance reduction / credit note / installment adjustment)
            // reduce what is genuinely still owed. They move no cash, so their only balance-sheet
            // effect is here — and they count exactly once.
            var receivablesCredited = _context.RebateDisbursements.AsNoTracking()
                .Where(d => account.IsCustomerReceivables && d.Rebate.Booking.SaleRecognition != null
                    && (d.Method == CustomerRebateMethod.OutstandingBalanceReduction
                        || d.Method == CustomerRebateMethod.InstallmentAdjustment
                        || d.Method == CustomerRebateMethod.CreditNote))
                .Select(d => new FinanceAccountTransactionDto
                {
                    Kind = "Customer credit applied", RecordId = d.Id,
                    Date = d.AppliedAt < d.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                        ? d.Rebate.Booking.SaleRecognition!.RecognitionDate : d.AppliedAt,
                    Label = d.Rebate.Customer.FullName, Reference = d.Reference,
                    ProjectName = d.Rebate.Booking.Unit.Project.ProjectName,
                    Amount = -d.Amount, GrossAmount = d.Amount, WhtAmount = 0m
                });
            var receivablesCreditsReversed = _context.RebateDisbursementReversals.AsNoTracking()
                .Where(r => account.IsCustomerReceivables && r.Disbursement.Rebate.Booking.SaleRecognition != null
                    && (r.Disbursement.Method == CustomerRebateMethod.OutstandingBalanceReduction
                        || r.Disbursement.Method == CustomerRebateMethod.InstallmentAdjustment
                        || r.Disbursement.Method == CustomerRebateMethod.CreditNote))
                .Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Customer credit reversed", RecordId = r.Id,
                    Date = r.ReversedAt < r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                        ? r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate : r.ReversedAt,
                    Label = r.Disbursement.Rebate.Customer.FullName, Reference = r.Reason,
                    ProjectName = r.Disbursement.Rebate.Booking.Unit.Project.ProjectName,
                    Amount = r.Amount, GrossAmount = r.Amount, WhtAmount = 0m
                });

            var capitalCash = _context.CapitalTransactions.AsNoTracking().Where(t => t.FinanceAccountId == id)
                .Select(t => new FinanceAccountTransactionDto
                {
                    Kind = t.Type == CapitalTransactionType.Contribution ? "Capital contribution" : "Capital withdrawal",
                    RecordId = t.Id, Date = t.Date, Label = t.CapitalPartner.Name,
                    Reference = t.Reference, ProjectName = "General",
                    Amount = t.Type == CapitalTransactionType.Contribution ? t.Amount : -t.Amount,
                    GrossAmount = t.Amount, WhtAmount = 0m
                });
            var partnerCapital = _context.CapitalTransactions.AsNoTracking()
                .Where(t => t.CapitalPartner.FinanceAccountId == id)
                .Select(t => new FinanceAccountTransactionDto
                {
                    Kind = t.Type == CapitalTransactionType.OpeningBalance ? "Capital opening balance"
                        : t.Type == CapitalTransactionType.Contribution ? "Capital contribution"
                        : t.Type == CapitalTransactionType.Withdrawal ? "Capital withdrawal"
                        : t.Type == CapitalTransactionType.ProfitShare ? "Capital profit share" : "Capital loss share",
                    RecordId = t.Id, Date = t.Date, Label = t.CapitalPartner.Name,
                    Reference = t.Reference, ProjectName = "General",
                    Amount = t.Type == CapitalTransactionType.Withdrawal || t.Type == CapitalTransactionType.LossShare
                        ? -t.Amount : t.Amount,
                    GrossAmount = t.Amount, WhtAmount = 0m
                });
            var loanCash = _context.LoanTransactions.AsNoTracking().Where(t => t.FinanceAccountId == id)
                .Select(t => new FinanceAccountTransactionDto
                {
                    Kind = t.Type == LoanTransactionType.Drawdown ? "Loan drawdown" : "Loan repayment",
                    RecordId = t.Id, Date = t.Date, Label = t.Loan.Name, Reference = t.Reference,
                    ProjectName = "General",
                    Amount = t.Type == LoanTransactionType.Drawdown
                        ? t.PrincipalAmount : -(t.PrincipalAmount + t.InterestAmount),
                    GrossAmount = t.Type == LoanTransactionType.Drawdown
                        ? t.PrincipalAmount : t.PrincipalAmount + t.InterestAmount,
                    WhtAmount = 0m
                });
            var loanLiability = _context.LoanTransactions.AsNoTracking()
                .Where(t => t.Loan.FinanceAccountId == id && t.PrincipalAmount != 0m)
                .Select(t => new FinanceAccountTransactionDto
                {
                    Kind = t.Type == LoanTransactionType.Drawdown ? "Loan principal drawn" : "Loan principal repaid",
                    RecordId = t.Id, Date = t.Date, Label = t.Loan.Name, Reference = t.Reference,
                    ProjectName = "General",
                    Amount = t.Type == LoanTransactionType.Drawdown ? t.PrincipalAmount : -t.PrincipalAmount,
                    GrossAmount = t.PrincipalAmount, WhtAmount = 0m
                });
            var staffTransfers = _context.StaffCashTransfers.AsNoTracking()
                .Where(t => t.StaffFinanceAccountId == id)
                .Select(t => new FinanceAccountTransactionDto
                {
                    Kind = t.Type == StaffCashMovementType.FundsGiven ? "Staff cash received" : "Staff cash returned",
                    RecordId = t.Id, Date = t.Date,
                    Label = t.Type == StaffCashMovementType.FundsGiven
                        ? "Received from " + t.CounterpartyFinanceAccount.Name
                        : "Returned to " + t.CounterpartyFinanceAccount.Name,
                    Reference = t.Reference, ProjectName = "General",
                    Amount = t.Type == StaffCashMovementType.FundsGiven ? t.Amount : -t.Amount,
                    GrossAmount = t.Amount, WhtAmount = 0m
                });
            var staffCounterpartyTransfers = _context.StaffCashTransfers.AsNoTracking()
                .Where(t => t.CounterpartyFinanceAccountId == id)
                .Select(t => new FinanceAccountTransactionDto
                {
                    Kind = t.Type == StaffCashMovementType.FundsGiven ? "Cash given to staff" : "Cash returned by staff",
                    RecordId = t.Id, Date = t.Date, Label = t.StaffFinanceAccount.AccountHolderName,
                    Reference = t.Reference, ProjectName = "General",
                    Amount = t.Type == StaffCashMovementType.FundsGiven ? -t.Amount : t.Amount,
                    GrossAmount = t.Amount, WhtAmount = 0m
                });
            var rows = await revenue.Concat(payments).Concat(expenses).Concat(commissionPayouts).Concat(commissionReversals)
                .Concat(rebatePayments).Concat(rebateReversals).Concat(whtDeposits)
                .Concat(assetPurchasesPaid).Concat(assetPurchasesCapitalised)
                .Concat(cancellationRefundsCash).Concat(refundPayableCreated).Concat(refundPayablePaid)
                .Concat(capitalCash).Concat(partnerCapital)
                .Concat(loanCash).Concat(loanLiability)
                .Concat(staffTransfers).Concat(staffCounterpartyTransfers)
                .Concat(payableWithheld).Concat(payableWithheldOnAssets).Concat(payableDeposited)
                .Concat(depositsReceived).Concat(depositsRecognised).Concat(depositsCancelled)
                .Concat(receivablesRecognised).Concat(receivablesSettled)
                .Concat(receivablesCredited).Concat(receivablesCreditsReversed)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.RecordId)
                .Skip(skip).Take(take + 1).ToListAsync(cancellationToken);
            return new PagedResult<FinanceAccountTransactionDto>
            {
                Items = rows.Take(take).ToList(), HasMore = rows.Count > take
            };
        }

        public async Task<FinanceAccountsOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            var accounts = await Project(_context.FinanceAccounts.AsNoTracking()).ToListAsync(cancellationToken);
            return new FinanceAccountsOverviewDto
            {
                ActiveAccounts = accounts.Count(a => a.IsActive),
                InactiveAccounts = accounts.Count(a => !a.IsActive),
                TotalBalance = accounts.Where(a => AccountBalanceDirection.IsCashLike(a.Type)).Sum(a => a.CurrentBalance),
                HolderBalances = accounts.Where(a => AccountBalanceDirection.IsCashLike(a.Type))
                    .GroupBy(a => a.AccountHolderName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new FinanceHolderBalanceDto
                    {
                        AccountHolderName = g.First().AccountHolderName,
                        AccountCount = g.Count(),
                        CurrentBalance = g.Sum(a => a.CurrentBalance)
                    }).OrderByDescending(h => h.CurrentBalance).ToList()
            };
        }

        public async Task<FinanceAccountResponseDto> CreateAsync(CreateFinanceAccountDto dto, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            if (dto.OpeningBalance != 0m && await _context.OpeningBalanceSets.AnyAsync(cancellationToken))
                throw new InvalidOperationException("Opening balances are controlled in Finance settings. Reopen the baseline to add this amount.");
            await EnsureUniqueName(dto.Name, null, cancellationToken);
            await EnsureUniqueStaffHolderAsync(dto, null, cancellationToken);
            var account = new FinanceAccount
            {
                Name = dto.Name.Trim(), Type = dto.Type,
                AccountHolderName = dto.AccountHolderName.Trim(), OpeningBalance = dto.OpeningBalance,
                LedgerCode = Clean(dto.LedgerCode), DisplayOrder = dto.DisplayOrder,
                BankOrWalletName = Clean(dto.BankOrWalletName), Description = Clean(dto.Description),
                IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            _context.FinanceAccounts.Add(account);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(account.Id, cancellationToken);
        }

        public async Task<FinanceAccountResponseDto> UpdateAsync(int id, UpdateFinanceAccountDto dto, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            var account = await _context.FinanceAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Finance account not found.");
            ApplyConcurrencyToken(account, dto.ConcurrencyToken);
            if (dto.OpeningBalance != account.OpeningBalance && await _context.OpeningBalanceSets.AnyAsync(cancellationToken))
                throw new InvalidOperationException("Opening balances are controlled in Finance settings. Reopen the baseline to change this amount.");
            EnsureSystemIdentityIsPreserved(account, dto);
            await ValidateLinkedLoanAccountAsync(id, dto.Type, dto.OpeningBalance, cancellationToken);
            if (dto.Type != account.Type && await HasDependenciesAsync(id, cancellationToken))
                throw new InvalidOperationException("An account with financial history cannot change type. Create a correctly typed account and keep this one for reconciliation.");
            await EnsureUniqueName(dto.Name, id, cancellationToken);
            await EnsureUniqueStaffHolderAsync(dto, id, cancellationToken);
            account.Name = dto.Name.Trim();
            account.Type = dto.Type;
            account.AccountHolderName = dto.AccountHolderName.Trim();
            account.OpeningBalance = dto.OpeningBalance;
            account.LedgerCode = Clean(dto.LedgerCode);
            account.DisplayOrder = dto.DisplayOrder;
            account.BankOrWalletName = Clean(dto.BankOrWalletName);
            account.Description = Clean(dto.Description);
            account.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task<FinanceAccountResponseDto> SetActiveAsync(
            int id, bool isActive, string concurrencyToken, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Finance account not found.");
            ApplyConcurrencyToken(account, concurrencyToken);
            if (account.SystemRole != FinanceSystemAccountRole.None && !isActive)
                throw new InvalidOperationException("System finance accounts cannot be deactivated because financial statements depend on them.");
            if (!isActive && await _context.Loans.AnyAsync(l => l.FinanceAccountId == id && l.IsActive, cancellationToken))
                throw new InvalidOperationException("Deactivate the linked loan before deactivating its liability account.");
            if (!isActive && account.Type == FinanceAccountType.StaffFloat)
            {
                var balance = await Project(_context.FinanceAccounts.AsNoTracking().Where(a => a.Id == id))
                    .Select(a => a.CurrentBalance).SingleAsync(cancellationToken);
                if (balance != 0m)
                    throw new InvalidOperationException("A staff float can only be deactivated after its balance reaches zero.");
            }
            account.IsActive = isActive;
            account.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task DeleteUnusedAsync(int id, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Finance account not found.");
            if (account.SystemRole != FinanceSystemAccountRole.None)
                throw new InvalidOperationException("System finance accounts cannot be deleted.");
            var used = await HasDependenciesAsync(id, cancellationToken);
            if (used) throw new InvalidOperationException("This account has transactions and cannot be deleted. Make it inactive instead.");
            _context.FinanceAccounts.Remove(account);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task EnsureSelectableAsync(int accountId, int? currentAccountId = null, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == accountId)
                .Select(a => new { a.IsActive, a.Type })
                .SingleOrDefaultAsync(cancellationToken);
            if (account == null) throw new InvalidOperationException("Selected finance account does not exist.");
            if (!account.IsActive && currentAccountId != accountId)
                throw new InvalidOperationException("Selected finance account is inactive. Choose an active account.");
            if (!AccountBalanceDirection.IsCashLike(account.Type))
                throw new InvalidOperationException("Only a cash, bank, mobile wallet or other cash-like account can be used for payments.");
        }

        public async Task EnsureExpenseSourceAsync(
            int accountId,
            int? currentAccountId = null,
            CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == accountId)
                .Select(a => new { a.IsActive, a.Type })
                .SingleOrDefaultAsync(cancellationToken);
            if (account == null) throw new InvalidOperationException("Selected finance account does not exist.");
            if (!account.IsActive && currentAccountId != accountId)
                throw new InvalidOperationException("Selected finance account is inactive. Choose an active account.");
            if (!AccountBalanceDirection.CanPayExpense(account.Type))
                throw new InvalidOperationException(
                    "Expenses can only be paid from cash, bank, mobile wallet, other cash-like accounts, or a staff float.");
        }

        /// <summary>
        /// Validates the destination of a capitalised purchase. The mirror of
        /// <see cref="EnsureSelectableAsync"/>: that one insists on cash going out, this one insists
        /// the value lands somewhere it is actually held. Booking a purchase into a bank account
        /// would count the money twice, and into a liability would invert its sign.
        /// <para>
        /// A fixed asset is the only valid destination. Work in progress used to qualify on the
        /// theory that construction cost accumulates into the building and is released to cost of
        /// sales later. The client has since confirmed the opposite: construction spending is a
        /// cost on the day it is paid, so it belongs on the expense flow with the construction
        /// heads (cement, steel, labour, contractors) behind it, not here.
        /// </para>
        /// <para>
        /// A purchase ALREADY booked to a work-in-progress account stays editable in place —
        /// correcting its amount or attachment must not be blocked, and forcing it to move would
        /// rewrite history rather than preserve it. Only pointing a purchase AT work in progress is
        /// refused. The work-in-progress accounts themselves stay on the chart because their
        /// inherited ERP opening balances are still real assets.
        /// </para>
        /// </summary>
        public async Task EnsureAssetAccountAsync(int accountId, int? currentAccountId = null, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == accountId)
                .Select(a => new { a.IsActive, a.Type })
                .SingleOrDefaultAsync(cancellationToken);
            if (account == null) throw new InvalidOperationException("Selected asset account does not exist.");
            if (!account.IsActive && currentAccountId != accountId)
                throw new InvalidOperationException("Selected asset account is inactive. Choose an active account.");
            if (account.Type == FinanceAccountType.WorkInProgress)
            {
                if (currentAccountId == accountId) return;
                throw new InvalidOperationException(
                    "Construction and work-in-progress spending is recorded as an expense, not capitalised. "
                    + "Record it under Expenses against its construction head (cement, steel, labour, contractor) "
                    + "so it reduces profit on the day it is paid.");
            }
            if (account.Type != FinanceAccountType.FixedAsset)
                throw new InvalidOperationException(
                    "Purchases can only be capitalised into a fixed-asset account.");
        }

        public async Task<List<FinanceAccountResponseDto>> SetupClientChartAsync(CancellationToken cancellationToken = default)
        {
            var definitions = ClientChart();
            var names = definitions.Select(d => d.Name).ToList();
            var existing = await _context.FinanceAccounts.Where(a => names.Contains(a.Name)).ToListAsync(cancellationToken);
            foreach (var definition in definitions)
            {
                var account = existing.SingleOrDefault(a => string.Equals(a.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
                if (account == null)
                {
                    account = new FinanceAccount
                    {
                        Name = definition.Name, Type = definition.Type, LedgerCode = definition.LedgerCode,
                        DisplayOrder = definition.DisplayOrder, AccountHolderName = definition.Holder,
                        SystemRole = definition.SystemRole,
                        OpeningBalance = 0m, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                    };
                    _context.FinanceAccounts.Add(account);
                    existing.Add(account);
                }
                else if (account.Type != definition.Type)
                {
                    throw new InvalidOperationException($"'{definition.Name}' already exists with account type {account.Type}. Correct it before running setup.");
                }
                else
                {
                    account.LedgerCode ??= definition.LedgerCode;
                    if (definition.SystemRole != FinanceSystemAccountRole.None && account.SystemRole == FinanceSystemAccountRole.None)
                        account.SystemRole = definition.SystemRole;
                    if (definition.SystemRole != FinanceSystemAccountRole.None && account.SystemRole != definition.SystemRole)
                        throw new InvalidOperationException($"'{definition.Name}' already exists with a different system role.");
                    if (account.DisplayOrder == 0) account.DisplayOrder = definition.DisplayOrder;
                }
            }
            await _context.SaveChangesAsync(cancellationToken);

            var partners = await _context.CapitalPartners.Where(p => ClientPartnerNames.Contains(p.Name)).ToListAsync(cancellationToken);
            foreach (var name in ClientPartnerNames)
            {
                var account = existing.Single(a => string.Equals(a.Name, name + " Capital", StringComparison.OrdinalIgnoreCase));
                var partner = partners.SingleOrDefault(p => p.Name == name);
                if (partner == null)
                {
                    _context.CapitalPartners.Add(new CapitalPartner
                    {
                        Name = name, ProfitSharePercent = 11.1111m, FinanceAccountId = account.Id,
                        IsActive = true, CreatedAt = DateTime.UtcNow
                    });
                }
                else if (!partner.FinanceAccountId.HasValue)
                {
                    partner.FinanceAccountId = account.Id;
                }
            }
            await _context.SaveChangesAsync(cancellationToken);
            return await Project(_context.FinanceAccounts.AsNoTracking().Where(a => names.Contains(a.Name)))
                .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name).ToListAsync(cancellationToken);
        }

        private static readonly string[] ClientPartnerNames =
        [
            "M. Shahid Omer", "Yasir Arfat Anjum", "Nadeem Akhtar Satti", "Imtiaz Raheem",
            "Muhammad Tayyab Khan", "Muhammad Afzal", "Syed Iqbal Mian", "Ayub Satti", "Shabbir Hussain"
        ];

        private static List<ChartAccount> ClientChart()
        {
            var rows = new List<ChartAccount>
            {
                new("HBL Bank", FinanceAccountType.Bank, "4", 10),
                new("HBL Bank 79488961-03", FinanceAccountType.Bank, "4", 20),
                new("Bank Alfalah 1010591891", FinanceAccountType.Bank, "5", 30),
                new("Bank Alfalah 91010840084", FinanceAccountType.Bank, "31", 40),
                new("Bank Alfalah 91010841873", FinanceAccountType.Bank, "31", 50),
                new("Alfalah Saving", FinanceAccountType.Bank, null, 60),
                new("Cash Account", FinanceAccountType.Cash, "6", 70),
                new("Cost of Plot", FinanceAccountType.FixedAsset, "3", 200),
                new("Office Equipment", FinanceAccountType.FixedAsset, "7", 210),
                new("Office Furniture & Fixture", FinanceAccountType.FixedAsset, "23", 220),
                new("Mobile Phone & SIM Cards", FinanceAccountType.FixedAsset, "24", 230),
                new("Floria Building — Work in Progress", FinanceAccountType.WorkInProgress, "26", 300),
                new("Work in Progress — Site Office", FinanceAccountType.WorkInProgress, "29", 310),
                new("Securities & Advances", FinanceAccountType.Receivable, "19", 400),
                new("WHT TAX", FinanceAccountType.Receivable, "37", 410),
                new("Customer Receivables", FinanceAccountType.Receivable, null, 420,
                    SystemRole: FinanceSystemAccountRole.CustomerReceivables),
                new("Customer General Account / Customer Deposits", FinanceAccountType.Liability, "1", 500,
                    SystemRole: FinanceSystemAccountRole.CustomerDeposits),
                new("Tax Payable", FinanceAccountType.Liability, "11", 510, SystemRole: FinanceSystemAccountRole.TaxPayable),
                // No ledger code. The numeric codes above are the client's real ERP codes; this
                // account has no counterpart in that chart, and inventing an official-looking one
                // would put a code DAMS made up into the Trial Balance and its exports as though the
                // accountant had issued it. Null until they tell us what it should be.
                new("Customer Refunds Payable", FinanceAccountType.Liability, null, 515, SystemRole: FinanceSystemAccountRole.CustomerRefundPayable),
                new("Loan A/C", FinanceAccountType.Liability, "32", 520)
            };
            rows.AddRange(ClientPartnerNames.Select((name, index) =>
                new ChartAccount(name + " Capital", FinanceAccountType.Capital, null, 600 + index * 10, name)));
            return rows;
        }

        private sealed record ChartAccount(
            string Name,
            FinanceAccountType Type,
            string? LedgerCode,
            int DisplayOrder,
            string Holder = "Seven Ventures",
            FinanceSystemAccountRole SystemRole = FinanceSystemAccountRole.None);

        // Every outflow figure below counts expenses NET of withholding tax. Tax deducted from a
        // supplier never left this account — it is held for FBR, and leaves later as a WhtDeposit,
        // which is why deposits are an outflow here despite not being a business expense.
        //
        // Inflow is customer payments plus manually entered revenue. Payments are the larger of the
        // two by far, so leaving them out does not make the balance approximate — it makes it wrong
        // by the whole of what customers have paid, which is why balances used to read negative.
        private IQueryable<FinanceAccountResponseDto> Project(IQueryable<FinanceAccount> query) =>
            from a in query
            let genericIn = (a.Payments.Sum(p => (decimal?)p.Amount) ?? 0m)
                + (a.ManualRevenues.Sum(r => (decimal?)r.Amount) ?? 0m)
            // Gross, not net. The asset is worth what it cost; the tax withheld from the supplier
            // is a debt to FBR, not a discount on the desk.
            let capitalisedIn = a.AssetPurchasesReceived.Sum(p => (decimal?)p.Amount) ?? 0m
            let genericOut = (a.Expenses.Sum(e => (decimal?)(e.Amount - e.WhtAmount)) ?? 0m)
                // Net, like expenses: only this much actually left the bank.
                + (a.AssetPurchasesPaid.Sum(p => (decimal?)(p.Amount - p.WhtAmount)) ?? 0m)
                + (a.CommissionPayouts.Sum(p => (decimal?)p.Amount) ?? 0m)
                - (a.CommissionPayouts.SelectMany(p => p.Reversals).Sum(r => (decimal?)r.Amount) ?? 0m)
                + (a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                    .Sum(d => (decimal?)d.Amount) ?? 0m)
                - (a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                    .SelectMany(d => d.Reversals).Sum(r => (decimal?)r.Amount) ?? 0m)
                + (a.WhtDeposits.Sum(d => (decimal?)d.Amount) ?? 0m)
                // Actual cancellation-refund cash paid out of this account.
                + (a.CancellationRefundsPaid.Sum(r => (decimal?)r.Amount) ?? 0m)
            let cashCapitalIn = a.CapitalCashTransactions.Where(t => t.Type == CapitalTransactionType.Contribution)
                .Sum(t => (decimal?)t.Amount) ?? 0m
            let cashCapitalOut = a.CapitalCashTransactions.Where(t => t.Type == CapitalTransactionType.Withdrawal)
                .Sum(t => (decimal?)t.Amount) ?? 0m
            let partnerIn = a.CapitalPartners.SelectMany(p => p.Transactions)
                .Where(t => t.Type == CapitalTransactionType.OpeningBalance || t.Type == CapitalTransactionType.Contribution
                    || t.Type == CapitalTransactionType.ProfitShare).Sum(t => (decimal?)t.Amount) ?? 0m
            let partnerOut = a.CapitalPartners.SelectMany(p => p.Transactions)
                .Where(t => t.Type == CapitalTransactionType.Withdrawal || t.Type == CapitalTransactionType.LossShare)
                .Sum(t => (decimal?)t.Amount) ?? 0m
            let loanCashIn = a.LoanCashTransactions.Where(t => t.Type == LoanTransactionType.Drawdown)
                .Sum(t => (decimal?)t.PrincipalAmount) ?? 0m
            let loanCashOut = a.LoanCashTransactions.Where(t => t.Type == LoanTransactionType.Repayment)
                .Sum(t => (decimal?)(t.PrincipalAmount + t.InterestAmount)) ?? 0m
            let loanLiabilityIn = a.Loans.SelectMany(l => l.Transactions)
                .Where(t => t.Type == LoanTransactionType.Drawdown).Sum(t => (decimal?)t.PrincipalAmount) ?? 0m
            let loanLiabilityOut = a.Loans.SelectMany(l => l.Transactions)
                .Where(t => t.Type == LoanTransactionType.Repayment).Sum(t => (decimal?)t.PrincipalAmount) ?? 0m
            let staffFloatIn = a.StaffCashTransfers.Where(t => t.Type == StaffCashMovementType.FundsGiven)
                .Sum(t => (decimal?)t.Amount) ?? 0m
            let staffFloatOut = a.StaffCashTransfers.Where(t => t.Type == StaffCashMovementType.FundsReturned)
                .Sum(t => (decimal?)t.Amount) ?? 0m
            let staffCounterpartyIn = a.StaffCashCounterpartyTransfers
                .Where(t => t.Type == StaffCashMovementType.FundsReturned).Sum(t => (decimal?)t.Amount) ?? 0m
            let staffCounterpartyOut = a.StaffCashCounterpartyTransfers
                .Where(t => t.Type == StaffCashMovementType.FundsGiven).Sum(t => (decimal?)t.Amount) ?? 0m
            let isTaxPayable = a.SystemRole == FinanceSystemAccountRole.TaxPayable
            // Tax withheld from asset suppliers is owed to FBR on identical terms, so the payable
            // counts it too — otherwise the liability is short by whatever capital suppliers lost.
            let taxPayableIn = isTaxPayable
                ? (_context.Expenses.Sum(e => (decimal?)e.WhtAmount) ?? 0m)
                    + (_context.AssetPurchases.Sum(p => (decimal?)p.WhtAmount) ?? 0m)
                : 0m
            let taxPayableOut = isTaxPayable ? (_context.WhtDeposits.Sum(d => (decimal?)d.Amount) ?? 0m) : 0m
            let isRefundPayable = a.SystemRole == FinanceSystemAccountRole.CustomerRefundPayable
            // The liability side of a cancellation settlement: created when the Admin decides to
            // refund, cleared when the cash actually goes out. Queried directly like the tax
            // payable above, not via a.CancellationSettlementsPayable, so the "in" side of a
            // PayNow settlement and its immediate refund are counted from the same source of
            // truth as the PayLater case.
            let refundPayableIn = isRefundPayable
                ? (_context.BookingCancellationSettlements.Where(s => s.RefundPayableAccountId == a.Id)
                    .Sum(s => (decimal?)s.RefundAmount) ?? 0m)
                : 0m
            let refundPayableOut = isRefundPayable
                ? (_context.BookingCancellationRefunds.Where(r => r.Settlement.RefundPayableAccountId == a.Id)
                    .Sum(r => (decimal?)r.Amount) ?? 0m)
                : 0m
            // Customer Deposits: money taken before the sale is recognised. It rises with every
            // payment received while the booking is still unrecognised, and falls when possession
            // turns that cash into revenue, or when a cancellation turns it into a refund
            // obligation plus retained income. Derived from the payments themselves rather than
            // from a stored snapshot, so what went in and what came out can never disagree.
            let isCustomerDeposits = a.SystemRole == FinanceSystemAccountRole.CustomerDeposits
            let depositIn = isCustomerDeposits
                ? (_context.Payments.Where(p => p.Booking.SaleRecognition == null
                        || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1) && p.CreatedAt <= p.Booking.SaleRecognition.RecognizedAt)))
                    .Sum(p => (decimal?)p.Amount) ?? 0m)
                : 0m
            let depositOut = isCustomerDeposits
                ? (_context.Payments.Where(p => p.Booking.CancellationSettlement != null
                        || (p.Booking.SaleRecognition != null
                            && (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1) && p.CreatedAt <= p.Booking.SaleRecognition.RecognizedAt))))
                    .Sum(p => (decimal?)p.Amount) ?? 0m)
                : 0m
            // Customer Receivables: what buyers still owe on sales that HAVE been recognised.
            // Raised in full at recognition, then cleared by every payment on that booking (the
            // pre-possession ones having already cleared the deposit) and by valid non-cash
            // credits. Nothing here exists for an unrecognised booking.
            let isCustomerReceivables = a.SystemRole == FinanceSystemAccountRole.CustomerReceivables
            let receivableIn = isCustomerReceivables
                ? (_context.BookingSaleRecognitions.Sum(r => (decimal?)r.NetSaleValue) ?? 0m)
                    + (_context.RebateDisbursementReversals
                        .Where(r => r.Disbursement.Rebate.Booking.SaleRecognition != null
                            && (r.Disbursement.Method == CustomerRebateMethod.OutstandingBalanceReduction
                                || r.Disbursement.Method == CustomerRebateMethod.InstallmentAdjustment
                                || r.Disbursement.Method == CustomerRebateMethod.CreditNote))
                        .Sum(r => (decimal?)r.Amount) ?? 0m)
                : 0m
            let receivableOut = isCustomerReceivables
                ? (_context.Payments.Where(p => p.Booking.SaleRecognition != null)
                        .Sum(p => (decimal?)p.Amount) ?? 0m)
                    + (_context.RebateDisbursements
                        .Where(d => d.Rebate.Booking.SaleRecognition != null
                            && (d.Method == CustomerRebateMethod.OutstandingBalanceReduction
                                || d.Method == CustomerRebateMethod.InstallmentAdjustment
                                || d.Method == CustomerRebateMethod.CreditNote))
                        .Sum(d => (decimal?)d.Amount) ?? 0m)
                : 0m
            let debitMovement = genericIn + capitalisedIn + cashCapitalIn + loanCashIn
                + staffFloatIn + staffCounterpartyIn
                - genericOut - cashCapitalOut - loanCashOut - staffFloatOut - staffCounterpartyOut
            let movement = a.Type == FinanceAccountType.Capital
                ? partnerIn - partnerOut
                : (isTaxPayable ? taxPayableIn - taxPayableOut
                    : isRefundPayable ? refundPayableIn - refundPayableOut
                    : isCustomerDeposits ? depositIn - depositOut
                    : isCustomerReceivables ? receivableIn - receivableOut
                    : debitMovement + loanLiabilityIn - loanLiabilityOut)
            select new FinanceAccountResponseDto
            {
                Id = a.Id, Name = a.Name, Type = a.Type, AccountHolderName = a.AccountHolderName,
                OpeningBalance = a.OpeningBalance, LedgerCode = a.LedgerCode, DisplayOrder = a.DisplayOrder,
                SystemRole = a.SystemRole,
                BankOrWalletName = a.BankOrWalletName, Description = a.Description, IsActive = a.IsActive,
                RevenueReceived = a.Type == FinanceAccountType.Capital ? partnerIn : (isTaxPayable ? taxPayableIn : isRefundPayable ? refundPayableIn : isCustomerDeposits ? depositIn : isCustomerReceivables ? receivableIn : genericIn + capitalisedIn + cashCapitalIn + loanCashIn + loanLiabilityIn + staffFloatIn + staffCounterpartyIn),
                ExpensesPaid = a.Type == FinanceAccountType.Capital ? partnerOut : (isTaxPayable ? taxPayableOut : isRefundPayable ? refundPayableOut : isCustomerDeposits ? depositOut : isCustomerReceivables ? receivableOut : genericOut + cashCapitalOut + loanCashOut + loanLiabilityOut + staffFloatOut + staffCounterpartyOut),
                WhtWithheld = isTaxPayable
                    ? taxPayableIn
                    : (a.Expenses.Sum(e => (decimal?)e.WhtAmount) ?? 0m)
                        + (a.AssetPurchasesPaid.Sum(p => (decimal?)p.WhtAmount) ?? 0m),
                WhtDeposited = isTaxPayable ? taxPayableOut : a.WhtDeposits.Sum(d => (decimal?)d.Amount) ?? 0m,
                NetMovement = movement,
                CurrentBalance = a.OpeningBalance + movement,
                TransactionCount = a.Payments.Count + a.ManualRevenues.Count + a.Expenses.Count
                    + a.AssetPurchasesPaid.Count + a.AssetPurchasesReceived.Count + a.CommissionPayouts.Count
                    + a.CommissionPayouts.SelectMany(p => p.Reversals).Count()
                    + a.RebateDisbursements.Count(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                    + a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                        .SelectMany(d => d.Reversals).Count()
                    + a.WhtDeposits.Count + a.CapitalCashTransactions.Count
                    + a.CapitalPartners.SelectMany(p => p.Transactions).Count()
                    + a.LoanCashTransactions.Count
                    + a.Loans.SelectMany(l => l.Transactions).Count(t => t.PrincipalAmount != 0m)
                    + a.StaffCashTransfers.Count + a.StaffCashCounterpartyTransfers.Count
                    + a.CancellationRefundsPaid.Count
                    + (isTaxPayable
                        ? _context.Expenses.Count(e => e.WhtAmount != 0m)
                            + _context.AssetPurchases.Count(p => p.WhtAmount != 0m)
                            + _context.WhtDeposits.Count()
                        : 0)
                    + (isRefundPayable
                        ? _context.BookingCancellationSettlements.Count(s => s.RefundPayableAccountId == a.Id)
                            + _context.BookingCancellationRefunds.Count(r => r.Settlement.RefundPayableAccountId == a.Id)
                        : 0)
                    // Deposits: one line per payment in, one per payment cleared out.
                    + (isCustomerDeposits
                        ? _context.Payments.Count(p => p.Booking.SaleRecognition == null
                                || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1) && p.CreatedAt <= p.Booking.SaleRecognition.RecognizedAt)))
                            + _context.Payments.Count(p => p.Booking.CancellationSettlement != null
                                || (p.Booking.SaleRecognition != null
                                    && (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1) && p.CreatedAt <= p.Booking.SaleRecognition.RecognizedAt))))
                        : 0)
                    + (isCustomerReceivables
                        ? _context.BookingSaleRecognitions.Count()
                            + _context.Payments.Count(p => p.Booking.SaleRecognition != null)
                            + _context.RebateDisbursements.Count(d => d.Rebate.Booking.SaleRecognition != null
                                && (d.Method == CustomerRebateMethod.OutstandingBalanceReduction
                                    || d.Method == CustomerRebateMethod.InstallmentAdjustment
                                    || d.Method == CustomerRebateMethod.CreditNote))
                            + _context.RebateDisbursementReversals.Count(r => r.Disbursement.Rebate.Booking.SaleRecognition != null
                                && (r.Disbursement.Method == CustomerRebateMethod.OutstandingBalanceReduction
                                    || r.Disbursement.Method == CustomerRebateMethod.InstallmentAdjustment
                                    || r.Disbursement.Method == CustomerRebateMethod.CreditNote))
                        : 0),
                CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt,
                ConcurrencyToken = Convert.ToBase64String(a.RowVersion)
            };

        private async Task EnsureUniqueName(string name, int? excludingId, CancellationToken cancellationToken)
        {
            var normalized = name.Trim();
            if (await _context.FinanceAccounts.AnyAsync(a => a.Name == normalized && (!excludingId.HasValue || a.Id != excludingId), cancellationToken))
                throw new InvalidOperationException("A finance account with this name already exists.");
        }

        private async Task EnsureUniqueStaffHolderAsync(
            CreateFinanceAccountDto dto,
            int? excludingId,
            CancellationToken cancellationToken)
        {
            if (dto.Type != FinanceAccountType.StaffFloat) return;
            var holder = dto.AccountHolderName.Trim();
            if (await _context.FinanceAccounts.AnyAsync(a =>
                    a.Type == FinanceAccountType.StaffFloat
                    && a.AccountHolderName == holder
                    && (!excludingId.HasValue || a.Id != excludingId.Value), cancellationToken))
                throw new InvalidOperationException("This person already has a staff float.");
        }

        // Canonical definitions for system accounts this service can resolve/create on demand
        // (not just via SetupClientChartAsync). TaxPayable is expected to already exist via the
        // client chart. The customer deposit/receivable pair is normally established by the chart
        // setup or the migration backfill; a definition is kept here so a command workflow can
        // repair a database that predates them without hand-creating an account.
        private static readonly Dictionary<FinanceSystemAccountRole, ChartAccount> SystemAccountDefinitions = new()
        {
            [FinanceSystemAccountRole.CustomerRefundPayable] =
                new ChartAccount("Customer Refunds Payable", FinanceAccountType.Liability, null, 515,
                    SystemRole: FinanceSystemAccountRole.CustomerRefundPayable),
            [FinanceSystemAccountRole.CustomerDeposits] =
                new ChartAccount("Customer General Account / Customer Deposits", FinanceAccountType.Liability, "1", 500,
                    SystemRole: FinanceSystemAccountRole.CustomerDeposits),
            [FinanceSystemAccountRole.CustomerReceivables] =
                new ChartAccount("Customer Receivables", FinanceAccountType.Receivable, null, 420,
                    SystemRole: FinanceSystemAccountRole.CustomerReceivables)
        };

        public async Task<int> EnsureSystemAccountAsync(FinanceSystemAccountRole role, CancellationToken cancellationToken = default)
        {
            if (!SystemAccountDefinitions.TryGetValue(role, out var definition))
                throw new InvalidOperationException($"No system-account definition exists for {role}.");

            var existing = await _context.FinanceAccounts.FirstOrDefaultAsync(a => a.SystemRole == role, cancellationToken);
            if (existing != null)
            {
                if (existing.Type != definition.Type)
                    throw new InvalidOperationException($"The {definition.Name} system account must be a {definition.Type} account.");
                if (!existing.IsActive)
                {
                    existing.IsActive = true;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);
                }
                return existing.Id;
            }

            var byName = await _context.FinanceAccounts
                .FirstOrDefaultAsync(a => a.Name == definition.Name, cancellationToken);
            if (byName != null)
            {
                if (byName.Type != definition.Type)
                    throw new InvalidOperationException(
                        $"An account named '{definition.Name}' already exists but is not a {definition.Type} account. Correct it before continuing.");
                if (byName.SystemRole != FinanceSystemAccountRole.None)
                    throw new InvalidOperationException($"An account named '{definition.Name}' already exists with a different system role.");
                byName.SystemRole = role;
                if (!byName.IsActive) byName.IsActive = true;
                byName.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                return byName.Id;
            }

            // Prefer the same holder as the existing Tax Payable system account, then any
            // existing company account, then the client chart's own default.
            var holder = await _context.FinanceAccounts.AsNoTracking()
                    .Where(a => a.SystemRole == FinanceSystemAccountRole.TaxPayable)
                    .Select(a => a.AccountHolderName).FirstOrDefaultAsync(cancellationToken)
                ?? await _context.FinanceAccounts.AsNoTracking()
                    .OrderBy(a => a.DisplayOrder).Select(a => a.AccountHolderName).FirstOrDefaultAsync(cancellationToken)
                ?? definition.Holder;

            var created = new FinanceAccount
            {
                Name = definition.Name, Type = definition.Type, AccountHolderName = holder,
                LedgerCode = definition.LedgerCode, DisplayOrder = definition.DisplayOrder, SystemRole = role,
                OpeningBalance = 0m, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            _context.FinanceAccounts.Add(created);
            await _context.SaveChangesAsync(cancellationToken);
            return created.Id;
        }

        private static void EnsureSystemIdentityIsPreserved(FinanceAccount account, UpdateFinanceAccountDto dto)
        {
            if (account.SystemRole == FinanceSystemAccountRole.None) return;
            if (!string.Equals(account.Name, dto.Name.Trim(), StringComparison.Ordinal)
                || account.Type != dto.Type
                || !string.Equals(account.LedgerCode, Clean(dto.LedgerCode), StringComparison.Ordinal))
                throw new InvalidOperationException("System finance accounts cannot change name, type or ledger code.");
        }

        private async Task<bool> HasDependenciesAsync(int id, CancellationToken cancellationToken) =>
            await _context.Payments.AnyAsync(p => p.FinanceAccountId == id, cancellationToken)
            || await _context.ManualRevenues.AnyAsync(r => r.FinanceAccountId == id, cancellationToken)
            || await _context.Expenses.AnyAsync(e => e.FinanceAccountId == id, cancellationToken)
            || await _context.AssetPurchases.AnyAsync(p => p.FinanceAccountId == id || p.AssetAccountId == id, cancellationToken)
            || await _context.CommissionPayouts.AnyAsync(p => p.FinanceAccountId == id, cancellationToken)
            || await _context.RebateDisbursements.AnyAsync(d => d.FinanceAccountId == id, cancellationToken)
            || await _context.BookingCancellationRefunds.AnyAsync(r => r.FinanceAccountId == id, cancellationToken)
            || await _context.BookingCancellationSettlements.AnyAsync(s => s.RefundPayableAccountId == id, cancellationToken)
            || await _context.WhtDeposits.AnyAsync(d => d.FinanceAccountId == id, cancellationToken)
            || await _context.OpeningBalanceEntries.AnyAsync(e => e.FinanceAccountId == id, cancellationToken)
            || await _context.CapitalPartners.AnyAsync(p => p.FinanceAccountId == id, cancellationToken)
            || await _context.CapitalTransactions.AnyAsync(t => t.FinanceAccountId == id, cancellationToken)
            || await _context.Loans.AnyAsync(l => l.FinanceAccountId == id, cancellationToken)
            || await _context.LoanTransactions.AnyAsync(t => t.FinanceAccountId == id, cancellationToken)
            || await _context.StaffCashTransfers.AnyAsync(
                t => t.StaffFinanceAccountId == id || t.CounterpartyFinanceAccountId == id, cancellationToken);

        private async Task ValidateLinkedLoanAccountAsync(int accountId, FinanceAccountType type, decimal openingBalance,
            CancellationToken cancellationToken)
        {
            var loanId = await _context.Loans.AsNoTracking().Where(l => l.FinanceAccountId == accountId)
                .Select(l => (int?)l.Id).SingleOrDefaultAsync(cancellationToken);
            if (!loanId.HasValue) return;
            if (type != FinanceAccountType.Liability)
                throw new InvalidOperationException("An account linked to a loan must remain a Liability account.");
            if (openingBalance < 0m)
                throw new InvalidOperationException("A loan liability cannot have a negative opening balance.");
            var movements = await _context.LoanTransactions.AsNoTracking().Where(t => t.LoanId == loanId.Value)
                .GroupBy(t => t.Date).Select(g => new
                {
                    Date = g.Key,
                    Amount = g.Sum(t => t.Type == LoanTransactionType.Drawdown ? t.PrincipalAmount : -t.PrincipalAmount)
                }).OrderBy(x => x.Date).ToListAsync(cancellationToken);
            var running = openingBalance;
            foreach (var movement in movements)
            {
                running += movement.Amount;
                if (running < 0m)
                    throw new InvalidOperationException($"This opening balance would make the loan negative on {movement.Date:dd MMM yyyy}.");
            }
        }

        private static void Validate(CreateFinanceAccountDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("Account name is required.");
            if (dto.Name.Trim().Length > 120) throw new InvalidOperationException("Account name cannot exceed 120 characters.");
            if (!Enum.IsDefined(dto.Type)) throw new InvalidOperationException("Select a valid account type.");
            if (dto.LedgerCode?.Trim().Length > 30) throw new InvalidOperationException("Ledger code cannot exceed 30 characters.");
            if (string.IsNullOrWhiteSpace(dto.AccountHolderName)) throw new InvalidOperationException("Account holder name is required.");
            if (dto.AccountHolderName.Trim().Length > 150) throw new InvalidOperationException("Account holder name cannot exceed 150 characters.");
            if (Math.Abs(dto.OpeningBalance) > 999_999_999_999_999.99m) throw new InvalidOperationException("Opening balance is outside the supported range.");
            if (dto.BankOrWalletName?.Trim().Length > 150) throw new InvalidOperationException("Bank or wallet name cannot exceed 150 characters.");
            if (dto.Description?.Trim().Length > 1000) throw new InvalidOperationException("Description cannot exceed 1000 characters.");
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private void ApplyConcurrencyToken(FinanceAccount account, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (account.RowVersion.Length == 0) return;
                throw new InvalidOperationException("The account version is missing. Refresh and try again.");
            }
            try { _context.Entry(account).Property(a => a.RowVersion).OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The account version is invalid. Refresh and try again."); }
        }
    }
}
