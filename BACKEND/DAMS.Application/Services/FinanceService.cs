using DAMS.Application.Common;
using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services
{
    public class FinanceService : IFinanceService
    {
        private readonly AppDbContext _context;
        private readonly IFinanceAttachmentStorage _attachmentStorage;
        private readonly IFinanceAccountService _accountService;
        private readonly IWhtService _whtService;
        private readonly ILogger<FinanceService> _logger;

        public FinanceService(AppDbContext context, IFinanceAttachmentStorage attachmentStorage, IFinanceAccountService accountService, IWhtService whtService, ILogger<FinanceService> logger)
        {
            _context = context;
            _attachmentStorage = attachmentStorage;
            _accountService = accountService;
            _whtService = whtService;
            _logger = logger;
        }

        public async Task<FinancialSummaryDto> GetSummaryAsync(int? projectId, DateTime? from, DateTime? to, int? accountId = null, bool unassigned = false)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            // All totals are computed in SQL — no rows are materialised for the cards.
            var automaticRevenue = await PaymentsQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var manualRevenue = await ManualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .SumAsync(r => (decimal?)r.Amount) ?? 0m;
            // GROSS. The full invoice is the business cost, whatever was withheld from the payment,
            // so the expense and profit figures are unaffected by withholding.
            var ordinaryExpenses = await ExpenseQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;
            var whtWithheld = await ExpenseQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .SumAsync(e => (decimal?)e.WhtAmount) ?? 0m;
            var commissionPayouts = await CommissionPayoutQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var commissionReversals = await CommissionReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .SumAsync(r => (decimal?)r.Amount) ?? 0m;
            var rebatePayments = await CashRebateQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .SumAsync(d => (decimal?)d.Amount) ?? 0m;
            var rebateReversals = await CashRebateReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .SumAsync(r => (decimal?)r.Amount) ?? 0m;
            var totalExpenses = ordinaryExpenses + commissionPayouts - commissionReversals + rebatePayments - rebateReversals;

            // Outstanding/overdue are balance snapshots (not date-filtered). The summary and
            // paged tables share these SQL projections so their totals always reconcile.
            var accountFilterApplied = accountId.HasValue || unassigned;
            var outstandingTotal = 0m;
            var overdueTotal = 0m;
            if (!accountFilterApplied)
            {
                outstandingTotal = await OutstandingBalanceQuery(projectId)
                    .SumAsync(x => (decimal?)x.OutstandingAmount) ?? 0m;
                overdueTotal = await OverdueBalanceQuery(projectId)
                    .SumAsync(x => (decimal?)x.OverdueAmount) ?? 0m;
            }

            // When a single account is selected, surface its running balance up to the end of
            // the selected period. This is account-wide (project filter is ignored) because the
            // balance is a property of the account, matching the Accounts detail view.
            decimal? accountOpeningBalance = null;
            decimal? accountCurrentBalance = null;
            decimal? accountNetMovement = null;
            if (accountId.HasValue)
            {
                var opening = await _context.FinanceAccounts.AsNoTracking()
                    .Where(a => a.Id == accountId.Value)
                    .Select(a => (decimal?)a.OpeningBalance)
                    .SingleOrDefaultAsync();
                if (opening.HasValue)
                {
                    var cumulativeRevenue = await CashInflowBeforeAsync(toExclusive, accountId.Value);
                    var cumulativeExpenses = await CashOutflowBeforeAsync(toExclusive, accountId.Value);
                    accountOpeningBalance = opening.Value;
                    accountCurrentBalance = opening.Value + cumulativeRevenue - cumulativeExpenses;

                    // Cash movement over the selected period — not the same thing as profit.
                    // Expenses count at what actually left the account and FBR deposits count too,
                    // even though neither matches the P&L figure above.
                    var inflowBeforePeriod = fromValue.HasValue
                        ? await CashInflowBeforeAsync(fromValue, accountId.Value)
                        : 0m;
                    var outflowBeforePeriod = fromValue.HasValue
                        ? await CashOutflowBeforeAsync(fromValue, accountId.Value)
                        : 0m;
                    accountNetMovement =
                        (cumulativeRevenue - inflowBeforePeriod)
                        - (cumulativeExpenses - outflowBeforePeriod);
                }
            }

            var totalRevenue = automaticRevenue + manualRevenue;
            return new FinancialSummaryDto
            {
                AutomaticRevenue = automaticRevenue,
                ManualRevenue = manualRevenue,
                TotalRevenue = totalRevenue,
                TotalExpenses = totalExpenses,
                NetProfit = totalRevenue - totalExpenses,
                WhtWithheld = whtWithheld,
                OutstandingAmount = outstandingTotal,
                OverdueAmount = overdueTotal,
                AccountOpeningBalance = accountOpeningBalance,
                AccountCurrentBalance = accountCurrentBalance,
                AccountNetMovement = accountNetMovement
            };
        }

        // ── Paged table rows ────────────────────────────────────────────────────────
        // Each method fetches `take + 1` rows in SQL (OFFSET/FETCH) so HasMore is known
        // without a separate COUNT query.

        public async Task<PagedResult<RevenueLineDto>> GetRevenuePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            // Payments and manual revenue are unioned (UNION ALL) into one shape, ordered
            // and paged in SQL. A stable secondary key (Source + entity Id) keeps paging
            // deterministic across chunks.
            var payments = PaymentsQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(p => new RevenueRow
                {
                    SortId = p.Id,
                    Date = p.PaidAt,
                    ProjectId = p.Booking.Unit.ProjectId,
                    ProjectName = p.Booking.Unit.Project.ProjectName,
                    Amount = p.Amount,
                    Source = "Payment",
                    ManualRevenueId = null,
                    PaymentType = p.Type,
                    InstallmentType = p.Installment != null ? (InstallmentType?)p.Installment.Type : null,
                    ReceiptNumber = p.ReceiptNumber,
                    BookingReference = p.Booking.BookingReference,
                    CustomerName = p.Booking.Customer.FullName,
                    RevenueType = null,
                    Reference = null,
                    Description = null,
                    AttachmentFileName = null,
                    AttachmentContentType = null,
                    AttachmentFileSize = null,
                    AttachmentUploadedAt = null,
                    FinanceAccountId = p.FinanceAccountId,
                    FinanceAccountName = p.FinanceAccount != null ? p.FinanceAccount.Name : null,
                    AccountHolderName = p.FinanceAccount != null ? p.FinanceAccount.AccountHolderName : null
                });

            var manual = ManualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(r => new RevenueRow
                {
                    SortId = r.Id,
                    Date = r.Date,
                    ProjectId = r.ProjectId,
                    ProjectName = r.Project!.ProjectName,
                    Amount = r.Amount,
                    Source = "Manual Revenue",
                    ManualRevenueId = r.Id,
                    PaymentType = null,
                    InstallmentType = null,
                    ReceiptNumber = null,
                    BookingReference = null,
                    CustomerName = null,
                    RevenueType = r.RevenueType,
                    Reference = r.Reference,
                    Description = r.Description,
                    AttachmentFileName = r.Attachment != null ? r.Attachment.OriginalFileName : null,
                    AttachmentContentType = r.Attachment != null ? r.Attachment.ContentType : null,
                    AttachmentFileSize = r.Attachment != null ? r.Attachment.FileSize : null,
                    AttachmentUploadedAt = r.Attachment != null ? r.Attachment.UploadedAt : null,
                    FinanceAccountId = r.FinanceAccountId,
                    FinanceAccountName = r.FinanceAccount != null ? r.FinanceAccount.Name : null,
                    AccountHolderName = r.FinanceAccount != null ? r.FinanceAccount.AccountHolderName : null
                });

            var raw = await payments.Concat(manual)
                .OrderByDescending(x => x.Date)
                .ThenBy(x => x.Source)
                .ThenByDescending(x => x.SortId)
                .Skip(skip).Take(take + 1)
                .ToListAsync();

            var items = raw.Take(take).Select(r => new RevenueLineDto
            {
                Date = r.Date,
                ProjectId = r.ProjectId,
                ProjectName = r.ProjectName ?? "General",
                Amount = r.Amount,
                Source = r.Source,
                ManualRevenueId = r.ManualRevenueId,
                RevenueType = r.Source == "Payment"
                    ? ResolveAutomaticRevenueType(r.PaymentType!.Value, r.InstallmentType)
                    : (r.RevenueType ?? string.Empty),
                Reference = r.Source == "Payment"
                    ? BuildPaymentReference(r.ReceiptNumber, r.BookingReference ?? string.Empty, r.CustomerName ?? string.Empty)
                    : r.Reference,
                Description = r.Description,
                FinanceAccountId = r.FinanceAccountId,
                FinanceAccountName = r.FinanceAccountName,
                AccountHolderName = r.AccountHolderName,
                Attachment = MapAttachment(r.AttachmentFileName, r.AttachmentContentType, r.AttachmentFileSize, r.AttachmentUploadedAt)
            }).ToList();

            return new PagedResult<RevenueLineDto> { Items = items, HasMore = raw.Count > take };
        }

        public async Task<PagedResult<ExpenseLineDto>> GetExpensePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            var rows = await ExpenseQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => e.Id)
                .Skip(skip).Take(take + 1)
                .Select(e => new ExpenseLineDto
                {
                    Id = e.Id,
                    Date = e.Date,
                    ProjectId = e.ProjectId,
                    ProjectName = e.Project != null ? e.Project.ProjectName : "General",
                    Category = e.Category,
                    CategoryId = e.CategoryId,
                    Amount = e.Amount,
                    WhtApplied = e.WhtApplied,
                    WhtRate = e.WhtRate,
                    WhtAmount = e.WhtAmount,
                    NetPaid = e.Amount - e.WhtAmount,
                    WhtRateOverridden = e.WhtRateOverridden,
                    WhtOverrideReason = e.WhtOverrideReason,
                    WhtTaxSection = e.WhtTaxSection,
                    Description = e.Description,
                    Reference = e.Vendor,
                    VendorId = e.VendorId,
                    FinanceAccountId = e.FinanceAccountId,
                    FinanceAccountName = e.FinanceAccount != null ? e.FinanceAccount.Name : null,
                    AccountHolderName = e.FinanceAccount != null ? e.FinanceAccount.AccountHolderName : null,
                    Attachment = e.Attachment == null ? null : new FinanceAttachmentDto
                    {
                        FileName = e.Attachment.OriginalFileName,
                        ContentType = e.Attachment.ContentType,
                        FileSize = e.Attachment.FileSize,
                        UploadedAt = e.Attachment.UploadedAt
                    }
                })
                .ToListAsync();

            return Page(rows, take);
        }

        public async Task<PagedResult<OutstandingLineDto>> GetOutstandingPageAsync(int? projectId, int skip, int take)
        {
            var balances = OutstandingBalanceQuery(projectId)
                .OrderByDescending(x => x.OutstandingAmount).ThenBy(x => x.SortId)
                .Select(x => new OutstandingLineDto
                {
                    BookingReference = x.BookingReference,
                    CustomerName = x.CustomerName,
                    ProjectId = x.ProjectId,
                    ProjectName = x.ProjectName,
                    UnitNumber = x.UnitNumber,
                    AgreedSalePrice = x.NetSalePrice,
                    ReceivedAmount = x.SettledAmount,
                    OutstandingAmount = x.OutstandingAmount
                });

            return Page(await balances.Skip(skip).Take(take + 1).ToListAsync(), take);
        }

        public async Task<PagedResult<OverdueLineDto>> GetOverduePageAsync(int? projectId, int skip, int take)
        {
            // Fetch the page with Type as an enum, then format it in memory (enum.ToString
            // is not reliably translatable to SQL).
            var balances = OverdueBalanceQuery(projectId)
                .OrderBy(x => x.DueDate).ThenBy(x => x.SortId);

            var raw = await balances.Skip(skip).Take(take + 1).ToListAsync();

            var items = raw.Take(take).Select(r => new OverdueLineDto
            {
                BookingReference = r.BookingReference,
                CustomerName = r.CustomerName,
                ProjectId = r.ProjectId,
                ProjectName = r.ProjectName,
                UnitNumber = r.UnitNumber,
                SequenceNumber = r.SequenceNumber,
                InstallmentType = r.Type.ToString(),
                DueDate = r.DueDate,
                Amount = r.Amount,
                PaidAmount = r.PaidAmount,
                OverdueAmount = r.OverdueAmount
            }).ToList();

            return new PagedResult<OverdueLineDto> { Items = items, HasMore = raw.Count > take };
        }

        public async Task<PagedResult<NetProfitLineDto>> GetNetProfitPageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            // Net Profit = every revenue line (+) and expense line (−). Three sources are
            // unioned, ordered and paged in SQL.
            var payments = PaymentsQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(p => new NetProfitRow
                {
                    SortId = p.Id,
                    Date = p.PaidAt,
                    ProjectName = p.Booking.Unit.Project.ProjectName,
                    Kind = "revenue",
                    Amount = p.Amount,
                    IsPayment = true,
                    PaymentType = p.Type,
                    InstallmentType = p.Installment != null ? (InstallmentType?)p.Installment.Type : null,
                    Label = null
                });

            var manual = ManualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(r => new NetProfitRow
                {
                    SortId = r.Id,
                    Date = r.Date,
                    ProjectName = r.Project!.ProjectName,
                    Kind = "revenue",
                    Amount = r.Amount,
                    IsPayment = false,
                    PaymentType = null,
                    InstallmentType = null,
                    Label = r.RevenueType
                });

            var expenses = ExpenseQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(e => new NetProfitRow
                {
                    SortId = e.Id,
                    Date = e.Date,
                    ProjectName = e.Project!.ProjectName,
                    Kind = "expense",
                    Amount = e.Amount,
                    IsPayment = false,
                    PaymentType = null,
                    InstallmentType = null,
                    Label = e.Category
                });

            var commissionPayouts = CommissionPayoutQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(p => new NetProfitRow
                {
                    SortId = p.Id, Date = p.PaymentDate, ProjectName = p.Commission.Booking.Unit.Project.ProjectName,
                    Kind = "expense", Amount = p.Amount, IsPayment = false, PaymentType = null,
                    InstallmentType = null, Label = "Partner commission"
                });
            var commissionReversals = CommissionReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(r => new NetProfitRow
                {
                    SortId = r.Id, Date = r.ReversedAt, ProjectName = r.Payout.Commission.Booking.Unit.Project.ProjectName,
                    Kind = "revenue", Amount = r.Amount, IsPayment = false, PaymentType = null,
                    InstallmentType = null, Label = "Commission payout reversal"
                });
            var rebatePayments = CashRebateQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(d => new NetProfitRow
                {
                    SortId = d.Id, Date = d.AppliedAt, ProjectName = d.Rebate.Booking.Unit.Project.ProjectName,
                    Kind = "expense", Amount = d.Amount, IsPayment = false, PaymentType = null,
                    InstallmentType = null, Label = "Customer rebate"
                });
            var rebateReversals = CashRebateReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(r => new NetProfitRow
                {
                    SortId = r.Id, Date = r.ReversedAt, ProjectName = r.Disbursement.Rebate.Booking.Unit.Project.ProjectName,
                    Kind = "revenue", Amount = r.Amount, IsPayment = false, PaymentType = null,
                    InstallmentType = null, Label = "Customer rebate reversal"
                });

            var raw = await payments.Concat(manual).Concat(expenses).Concat(commissionPayouts)
                .Concat(commissionReversals).Concat(rebatePayments).Concat(rebateReversals)
                .OrderByDescending(x => x.Date)
                .ThenBy(x => x.Kind)
                .ThenByDescending(x => x.SortId)
                .Skip(skip).Take(take + 1)
                .ToListAsync();

            var items = raw.Take(take).Select(r => new NetProfitLineDto
            {
                Date = r.Date,
                ProjectName = r.ProjectName ?? "General",
                Kind = r.Kind,
                Label = r.IsPayment
                    ? ResolveAutomaticRevenueType(r.PaymentType!.Value, r.InstallmentType)
                    : (r.Label ?? string.Empty),
                Amount = r.Kind == "expense" ? -r.Amount : r.Amount
            }).ToList();

            return new PagedResult<NetProfitLineDto> { Items = items, HasMore = raw.Count > take };
        }

        /// <summary>
        /// Everything that has arrived in one account before <paramref name="toExclusive"/> (all of
        /// time when null): customer payments and manually entered revenue alike. Customer payments
        /// are the largest inflow in the business, so a balance that omits them is not approximate
        /// — it is arbitrarily far out, and usually negative.
        /// </summary>
        private async Task<decimal> CashInflowBeforeAsync(DateTime? toExclusive, int accountId)
        {
            var payments = await PaymentsQuery(null, null, toExclusive, accountId, false)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var manual = await ManualQuery(null, null, toExclusive, accountId, false)
                .SumAsync(r => (decimal?)r.Amount) ?? 0m;
            return payments + manual;
        }

        /// <summary>
        /// Everything that has left one account before <paramref name="toExclusive"/> (all of time
        /// when null).
        /// <para>
        /// Expenses count at their NET amount, because tax withheld from a supplier never left the
        /// bank — it is held for FBR. It leaves later, as a <c>WhtDeposit</c>, which is why
        /// deposits are an outflow here even though they are not a business expense and never
        /// appear in the P&amp;L.
        /// </para>
        /// </summary>
        private async Task<decimal> CashOutflowBeforeAsync(DateTime? toExclusive, int accountId)
        {
            var expenses = await ExpenseQuery(null, null, toExclusive, accountId, false)
                .SumAsync(e => (decimal?)(e.Amount - e.WhtAmount)) ?? 0m;
            var commissions = (await CommissionPayoutQuery(null, null, toExclusive, accountId, false)
                    .SumAsync(p => (decimal?)p.Amount) ?? 0m)
                - (await CommissionReversalQuery(null, null, toExclusive, accountId, false)
                    .SumAsync(r => (decimal?)r.Amount) ?? 0m);
            var rebates = (await CashRebateQuery(null, null, toExclusive, accountId, false)
                    .SumAsync(d => (decimal?)d.Amount) ?? 0m)
                - (await CashRebateReversalQuery(null, null, toExclusive, accountId, false)
                    .SumAsync(r => (decimal?)r.Amount) ?? 0m);
            var whtDeposits = await WhtDepositQuery(toExclusive, accountId)
                .SumAsync(d => (decimal?)d.Amount) ?? 0m;
            return expenses + commissions + rebates + whtDeposits;
        }

        private IQueryable<WhtDeposit> WhtDepositQuery(DateTime? toExclusive, int accountId)
        {
            var q = _context.WhtDeposits.AsNoTracking().Where(d => d.FinanceAccountId == accountId);
            if (toExclusive.HasValue) q = q.Where(d => d.DepositDate < toExclusive.Value);
            return q;
        }

        // ── Filtered base queries (shared by summary totals and paged rows) ──
        private IQueryable<Payment> PaymentsQuery(int? projectId, DateTime? fromValue, DateTime? toExclusive,
            int? accountId = null, bool unassigned = false)
        {
            // Payments on cancelled bookings stay in revenue: the money was really
            // received, and cancelling a booking must not rewrite finance history.
            var q = _context.Payments.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(p => p.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(p => p.PaidAt >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(p => p.PaidAt < toExclusive.Value);
            if (accountId.HasValue) q = q.Where(p => p.FinanceAccountId == accountId.Value);
            else if (unassigned) q = q.Where(p => p.FinanceAccountId == null);
            return q;
        }

        private IQueryable<ManualRevenue> ManualQuery(int? projectId, DateTime? fromValue, DateTime? toExclusive, int? accountId = null, bool unassigned = false)
        {
            var q = _context.ManualRevenues.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(r => r.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(r => r.Date >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(r => r.Date < toExclusive.Value);
            if (accountId.HasValue) q = q.Where(r => r.FinanceAccountId == accountId.Value);
            else if (unassigned) q = q.Where(r => r.FinanceAccountId == null);
            return q;
        }

        private IQueryable<Expense> ExpenseQuery(int? projectId, DateTime? fromValue, DateTime? toExclusive, int? accountId = null, bool unassigned = false)
        {
            var q = _context.Expenses.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(e => e.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(e => e.Date >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(e => e.Date < toExclusive.Value);
            if (accountId.HasValue) q = q.Where(e => e.FinanceAccountId == accountId.Value);
            else if (unassigned) q = q.Where(e => e.FinanceAccountId == null);
            return q;
        }

        private IQueryable<CommissionPayout> CommissionPayoutQuery(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId, bool unassigned)
        {
            var q = _context.CommissionPayouts.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(p => p.Commission.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(p => p.PaymentDate >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(p => p.PaymentDate < toExclusive.Value);
            if (accountId.HasValue) q = q.Where(p => p.FinanceAccountId == accountId.Value);
            else if (unassigned) q = q.Where(_ => false);
            return q;
        }

        private IQueryable<CommissionPayoutReversal> CommissionReversalQuery(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId, bool unassigned)
        {
            var q = _context.CommissionPayoutReversals.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(r => r.Payout.Commission.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(r => r.ReversedAt >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(r => r.ReversedAt < toExclusive.Value);
            if (accountId.HasValue) q = q.Where(r => r.Payout.FinanceAccountId == accountId.Value);
            else if (unassigned) q = q.Where(_ => false);
            return q;
        }

        private IQueryable<RebateDisbursement> CashRebateQuery(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId, bool unassigned)
        {
            var q = _context.RebateDisbursements.AsNoTracking()
                .Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment);
            if (projectId.HasValue) q = q.Where(d => d.Rebate.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(d => d.AppliedAt >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(d => d.AppliedAt < toExclusive.Value);
            if (accountId.HasValue) q = q.Where(d => d.FinanceAccountId == accountId.Value);
            else if (unassigned) q = q.Where(_ => false);
            return q;
        }

        private IQueryable<RebateDisbursementReversal> CashRebateReversalQuery(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId, bool unassigned)
        {
            var q = _context.RebateDisbursementReversals.AsNoTracking()
                .Where(r => r.Disbursement.Method == CustomerRebateMethod.CashOrBankPayment);
            if (projectId.HasValue) q = q.Where(r => r.Disbursement.Rebate.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(r => r.ReversedAt >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(r => r.ReversedAt < toExclusive.Value);
            if (accountId.HasValue) q = q.Where(r => r.Disbursement.FinanceAccountId == accountId.Value);
            else if (unassigned) q = q.Where(_ => false);
            return q;
        }

        private IQueryable<Booking> OutstandingBookings(int? projectId)
        {
            var q = _context.Bookings.AsNoTracking().Where(b => b.Status != BookingStatus.Cancelled);
            if (projectId.HasValue) q = q.Where(b => b.Unit.ProjectId == projectId.Value);
            return q;
        }

        private IQueryable<Installment> OverdueInstallments(int? projectId)
        {
            var today = PakistanTime.Today;
            var q = _context.Installments.AsNoTracking()
                .Where(i => i.DueDate < today
                    && i.Status != InstallmentStatus.Paid
                    && i.Booking.Status != BookingStatus.Cancelled);
            if (projectId.HasValue) q = q.Where(i => i.Booking.Unit.ProjectId == projectId.Value);
            return q;
        }

        private IQueryable<OutstandingBalanceRow> OutstandingBalanceQuery(int? projectId)
        {
            var payments = _context.Payments.AsNoTracking()
                .GroupBy(p => p.BookingId)
                .Select(g => new { BookingId = g.Key, Amount = g.Sum(p => (decimal?)p.Amount) });
            var credits = _context.RebateDisbursements.AsNoTracking()
                .Where(d => d.Method == CustomerRebateMethod.OutstandingBalanceReduction
                    || d.Method == CustomerRebateMethod.InstallmentAdjustment || d.Method == CustomerRebateMethod.CreditNote)
                .GroupBy(d => d.Rebate.BookingId)
                .Select(g => new { BookingId = g.Key, Amount = g.Sum(d => (decimal?)d.Amount) });
            var reversals = _context.RebateDisbursementReversals.AsNoTracking()
                .Where(r => r.Disbursement.Method == CustomerRebateMethod.OutstandingBalanceReduction
                    || r.Disbursement.Method == CustomerRebateMethod.InstallmentAdjustment
                    || r.Disbursement.Method == CustomerRebateMethod.CreditNote)
                .GroupBy(r => r.Disbursement.Rebate.BookingId)
                .Select(g => new { BookingId = g.Key, Amount = g.Sum(r => (decimal?)r.Amount) });

            return
                from booking in OutstandingBookings(projectId)
                join payment in payments on booking.Id equals payment.BookingId into paymentGroup
                from payment in paymentGroup.DefaultIfEmpty()
                join credit in credits on booking.Id equals credit.BookingId into creditGroup
                from credit in creditGroup.DefaultIfEmpty()
                join reversal in reversals on booking.Id equals reversal.BookingId into reversalGroup
                from reversal in reversalGroup.DefaultIfEmpty()
                let netSalePrice = booking.AgreedSalePrice - booking.DiscountAmount
                let settled = (payment.Amount ?? 0m) + (credit.Amount ?? 0m) - (reversal.Amount ?? 0m)
                let outstanding = netSalePrice - settled
                where outstanding > 0m
                select new OutstandingBalanceRow
                {
                    SortId = booking.Id,
                    BookingReference = booking.BookingReference,
                    CustomerName = booking.Customer.FullName,
                    ProjectId = booking.Unit.ProjectId,
                    ProjectName = booking.Unit.Project != null ? booking.Unit.Project.ProjectName : "General",
                    UnitNumber = booking.Unit.UnitNumber,
                    NetSalePrice = netSalePrice,
                    SettledAmount = settled,
                    OutstandingAmount = outstanding
                };
        }

        private IQueryable<OverdueBalanceRow> OverdueBalanceQuery(int? projectId)
        {
            var payments = _context.Payments.AsNoTracking()
                .Where(p => p.InstallmentId != null)
                .GroupBy(p => p.InstallmentId!.Value)
                .Select(g => new { InstallmentId = g.Key, Amount = g.Sum(p => (decimal?)p.Amount) });
            var credits = _context.RebateDisbursements.AsNoTracking()
                .Where(d => d.InstallmentId != null)
                .GroupBy(d => d.InstallmentId!.Value)
                .Select(g => new { InstallmentId = g.Key, Amount = g.Sum(d => (decimal?)d.Amount) });
            var reversals = _context.RebateDisbursementReversals.AsNoTracking()
                .Where(r => r.Disbursement.InstallmentId != null)
                .GroupBy(r => r.Disbursement.InstallmentId!.Value)
                .Select(g => new { InstallmentId = g.Key, Amount = g.Sum(r => (decimal?)r.Amount) });

            return
                from installment in OverdueInstallments(projectId)
                join payment in payments on installment.Id equals payment.InstallmentId into paymentGroup
                from payment in paymentGroup.DefaultIfEmpty()
                join credit in credits on installment.Id equals credit.InstallmentId into creditGroup
                from credit in creditGroup.DefaultIfEmpty()
                join reversal in reversals on installment.Id equals reversal.InstallmentId into reversalGroup
                from reversal in reversalGroup.DefaultIfEmpty()
                let settled = (payment.Amount ?? 0m) + (credit.Amount ?? 0m) - (reversal.Amount ?? 0m)
                let overdue = installment.Amount - settled
                where overdue > 0m
                select new OverdueBalanceRow
                {
                    SortId = installment.Id,
                    BookingReference = installment.Booking.BookingReference,
                    CustomerName = installment.Booking.Customer.FullName,
                    ProjectId = installment.Booking.Unit.ProjectId,
                    ProjectName = installment.Booking.Unit.Project != null ? installment.Booking.Unit.Project.ProjectName : "General",
                    UnitNumber = installment.Booking.Unit.UnitNumber,
                    SequenceNumber = installment.SequenceNumber,
                    Type = installment.Type,
                    DueDate = installment.DueDate,
                    Amount = installment.Amount,
                    PaidAmount = settled,
                    OverdueAmount = overdue
                };
        }

        private static PagedResult<T> Page<T>(List<T> rows, int take) =>
            new() { Items = rows.Take(take).ToList(), HasMore = rows.Count > take };

        private sealed class OutstandingBalanceRow
        {
            public int SortId { get; set; }
            public string BookingReference { get; set; } = string.Empty;
            public string CustomerName { get; set; } = string.Empty;
            public int? ProjectId { get; set; }
            public string ProjectName { get; set; } = string.Empty;
            public string UnitNumber { get; set; } = string.Empty;
            public decimal NetSalePrice { get; set; }
            public decimal SettledAmount { get; set; }
            public decimal OutstandingAmount { get; set; }
        }

        private sealed class OverdueBalanceRow
        {
            public int SortId { get; set; }
            public string BookingReference { get; set; } = string.Empty;
            public string CustomerName { get; set; } = string.Empty;
            public int? ProjectId { get; set; }
            public string ProjectName { get; set; } = string.Empty;
            public string UnitNumber { get; set; } = string.Empty;
            public int SequenceNumber { get; set; }
            public InstallmentType Type { get; set; }
            public DateTime DueDate { get; set; }
            public decimal Amount { get; set; }
            public decimal PaidAmount { get; set; }
            public decimal OverdueAmount { get; set; }
        }

        // Shapes used only inside SQL UNION ALL projections.
        private sealed class RevenueRow
        {
            public int SortId { get; set; }
            public DateTime Date { get; set; }
            public int? ProjectId { get; set; }
            public string? ProjectName { get; set; }
            public decimal Amount { get; set; }
            public string Source { get; set; } = string.Empty;
            public int? ManualRevenueId { get; set; }
            public PaymentType? PaymentType { get; set; }
            public InstallmentType? InstallmentType { get; set; }
            public string? ReceiptNumber { get; set; }
            public string? BookingReference { get; set; }
            public string? CustomerName { get; set; }
            public string? RevenueType { get; set; }
            public string? Reference { get; set; }
            public string? Description { get; set; }
            public string? AttachmentFileName { get; set; }
            public string? AttachmentContentType { get; set; }
            public long? AttachmentFileSize { get; set; }
            public DateTime? AttachmentUploadedAt { get; set; }
            public int? FinanceAccountId { get; set; }
            public string? FinanceAccountName { get; set; }
            public string? AccountHolderName { get; set; }
        }

        private sealed class NetProfitRow
        {
            public int SortId { get; set; }
            public DateTime Date { get; set; }
            public string? ProjectName { get; set; }
            public string Kind { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public bool IsPayment { get; set; }
            public PaymentType? PaymentType { get; set; }
            public InstallmentType? InstallmentType { get; set; }
            public string? Label { get; set; }
        }

        public async Task<ManualRevenueResponseDto> CreateManualRevenueAsync(
            CreateManualRevenueDto dto,
            int? adminUserId,
            FinanceAttachmentUpload? attachment = null,
            CancellationToken cancellationToken = default)
        {
            ValidateRevenue(dto.Amount, dto.RevenueType);
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Received In Account is required.");
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId.Value, null, cancellationToken);

            var revenue = new ManualRevenue
            {
                ProjectId = dto.ProjectId,
                FinanceAccountId = dto.FinanceAccountId,
                Amount = dto.Amount,
                RevenueType = dto.RevenueType.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                Reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference.Trim(),
                Date = dto.Date?.Date ?? DateTime.UtcNow,
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

            string? newStoredFileName = null;
            if (attachment != null)
            {
                var saved = await SaveAttachmentAsync(attachment, cancellationToken);
                newStoredFileName = saved.StoredFileName;
                revenue.Attachment = new FinanceAttachment
                {
                    StoredFileName = saved.StoredFileName,
                    OriginalFileName = saved.Metadata.OriginalFileName,
                    ContentType = saved.Metadata.ContentType,
                    FileSize = saved.Metadata.FileSize,
                    UploadedAt = DateTime.UtcNow
                };
            }

            _context.ManualRevenues.Add(revenue);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await DeleteNewFileAfterFailureAsync(newStoredFileName);
                throw;
            }

            return await MapManualRevenueAsync(revenue);
        }

        public async Task<ManualRevenueResponseDto> UpdateManualRevenueAsync(
            int id,
            UpdateManualRevenueDto dto,
            FinanceAttachmentUpload? attachment = null,
            bool removeAttachment = false,
            CancellationToken cancellationToken = default)
        {
            ValidateAttachmentChange(attachment, removeAttachment);
            var revenue = await _context.ManualRevenues
                .Include(r => r.Attachment)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (revenue == null)
                throw new InvalidOperationException("Manual revenue entry not found.");
            ValidateRevenue(dto.Amount, dto.RevenueType);
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Received In Account is required.");
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId.Value, revenue.FinanceAccountId, cancellationToken);

            var oldStoredFileName = revenue.Attachment?.StoredFileName;
            string? newStoredFileName = null;

            if (attachment != null)
            {
                var saved = await SaveAttachmentAsync(attachment, cancellationToken);
                newStoredFileName = saved.StoredFileName;
                if (revenue.Attachment == null)
                {
                    revenue.Attachment = new FinanceAttachment { ManualRevenueId = revenue.Id };
                }
                ApplyAttachment(revenue.Attachment, saved);
            }
            else if (removeAttachment && revenue.Attachment != null)
            {
                _context.FinanceAttachments.Remove(revenue.Attachment);
                revenue.Attachment = null;
            }

            revenue.ProjectId = dto.ProjectId;
            revenue.FinanceAccountId = dto.FinanceAccountId;
            revenue.Amount = dto.Amount;
            revenue.RevenueType = dto.RevenueType.Trim();
            revenue.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            revenue.Reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference.Trim();
            if (dto.Date.HasValue)
                revenue.Date = dto.Date.Value.Date;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await DeleteNewFileAfterFailureAsync(newStoredFileName);
                throw;
            }

            if ((attachment != null || removeAttachment) && oldStoredFileName != null)
                await DeleteObsoleteFileAsync(oldStoredFileName);

            return await MapManualRevenueAsync(revenue);
        }

        public async Task DeleteManualRevenueAsync(int id, CancellationToken cancellationToken = default)
        {
            var revenue = await _context.ManualRevenues
                .Include(r => r.Attachment)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (revenue == null)
                throw new InvalidOperationException("Manual revenue entry not found.");

            var storedFileName = revenue.Attachment?.StoredFileName;
            _context.ManualRevenues.Remove(revenue);
            await _context.SaveChangesAsync(cancellationToken);
            await DeleteObsoleteFileAsync(storedFileName);
        }

        public async Task<ExpenseResponseDto> CreateExpenseAsync(
            CreateExpenseDto dto,
            int? adminUserId,
            FinanceAttachmentUpload? attachment = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Paid From Account is required.");
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId.Value, null, cancellationToken);

            // Held until after SaveChanges so the year-to-date read and the insert that depends on
            // it cannot be interleaved with another save for the same vendor.
            await using var thresholdGuard = await BeginThresholdGuardAsync(
                new[] { dto.VendorId }, cancellationToken);

            var expense = new Expense
            {
                ProjectId = dto.ProjectId,
                FinanceAccountId = dto.FinanceAccountId,
                Amount = dto.Amount,
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                Date = dto.Date?.Date ?? DateTime.UtcNow,
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };
            await ApplyExpenseDetailsAsync(expense, dto, cancellationToken);

            string? newStoredFileName = null;
            if (attachment != null)
            {
                var saved = await SaveAttachmentAsync(attachment, cancellationToken);
                newStoredFileName = saved.StoredFileName;
                expense.Attachment = new FinanceAttachment
                {
                    StoredFileName = saved.StoredFileName,
                    OriginalFileName = saved.Metadata.OriginalFileName,
                    ContentType = saved.Metadata.ContentType,
                    FileSize = saved.Metadata.FileSize,
                    UploadedAt = DateTime.UtcNow
                };
            }

            _context.Expenses.Add(expense);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                if (thresholdGuard != null)
                    await thresholdGuard.CommitAsync(cancellationToken);
            }
            catch
            {
                await DeleteNewFileAfterFailureAsync(newStoredFileName);
                throw;
            }

            return await MapExpenseAsync(expense);
        }

        public async Task<ExpenseResponseDto> UpdateExpenseAsync(
            int id,
            UpdateExpenseDto dto,
            FinanceAttachmentUpload? attachment = null,
            bool removeAttachment = false,
            CancellationToken cancellationToken = default)
        {
            ValidateAttachmentChange(attachment, removeAttachment);
            var expense = await _context.Expenses
                .Include(e => e.Attachment)
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (expense == null)
                throw new InvalidOperationException("Expense not found.");
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Paid From Account is required.");
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId.Value, expense.FinanceAccountId, cancellationToken);

            // Editing re-decides the threshold too, so it needs the same protection as creating —
            // for the vendor being left as well as the one being joined.
            await using var thresholdGuard = await BeginThresholdGuardAsync(
                new[] { expense.VendorId, dto.VendorId }, cancellationToken);

            var oldStoredFileName = expense.Attachment?.StoredFileName;
            string? newStoredFileName = null;

            if (attachment != null)
            {
                var saved = await SaveAttachmentAsync(attachment, cancellationToken);
                newStoredFileName = saved.StoredFileName;
                if (expense.Attachment == null)
                {
                    expense.Attachment = new FinanceAttachment { ExpenseId = expense.Id };
                }
                ApplyAttachment(expense.Attachment, saved);
            }
            else if (removeAttachment && expense.Attachment != null)
            {
                _context.FinanceAttachments.Remove(expense.Attachment);
                expense.Attachment = null;
            }

            expense.ProjectId = dto.ProjectId;
            expense.FinanceAccountId = dto.FinanceAccountId;
            expense.Amount = dto.Amount;
            expense.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            if (dto.Date.HasValue)
                expense.Date = dto.Date.Value.Date;
            // Re-resolved after the date and amount move, because both feed the threshold check
            // and therefore the tax.
            await ApplyExpenseDetailsAsync(expense, dto, cancellationToken);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                if (thresholdGuard != null)
                    await thresholdGuard.CommitAsync(cancellationToken);
            }
            catch
            {
                await DeleteNewFileAfterFailureAsync(newStoredFileName);
                throw;
            }

            if ((attachment != null || removeAttachment) && oldStoredFileName != null)
                await DeleteObsoleteFileAsync(oldStoredFileName);

            return await MapExpenseAsync(expense);
        }

        public async Task DeleteExpenseAsync(int id, CancellationToken cancellationToken = default)
        {
            var expense = await _context.Expenses
                .Include(e => e.Attachment)
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (expense == null)
                throw new InvalidOperationException("Expense not found.");

            // Removing an expense lowers the vendor's year-to-date total, so it moves the same
            // aggregate a save reads. Without the lock a concurrent entry can decide the threshold
            // against a row that is about to disappear.
            await using var thresholdGuard = await BeginThresholdGuardAsync(
                new[] { expense.VendorId }, cancellationToken);

            var storedFileName = expense.Attachment?.StoredFileName;
            _context.Expenses.Remove(expense);
            await _context.SaveChangesAsync(cancellationToken);
            if (thresholdGuard != null)
                await thresholdGuard.CommitAsync(cancellationToken);
            await DeleteObsoleteFileAsync(storedFileName);
        }

        public async Task<FinanceAttachmentDownload> GetAttachmentAsync(
            FinanceRecordKind kind,
            int recordId,
            CancellationToken cancellationToken = default)
        {
            var attachment = kind == FinanceRecordKind.Revenue
                ? await _context.ManualRevenues.AsNoTracking()
                    .Where(r => r.Id == recordId && r.Attachment != null)
                    .Select(r => r.Attachment!)
                    .FirstOrDefaultAsync(cancellationToken)
                : await _context.Expenses.AsNoTracking()
                    .Where(e => e.Id == recordId && e.Attachment != null)
                    .Select(e => e.Attachment!)
                    .FirstOrDefaultAsync(cancellationToken);

            if (attachment == null)
                throw new FileNotFoundException("This finance record does not have an attachment.");

            var content = await _attachmentStorage.OpenReadAsync(attachment.StoredFileName, cancellationToken);
            if (content == null)
                throw new FileNotFoundException("The attachment file is missing from storage. Please replace it from the edit form.");

            return new FinanceAttachmentDownload
            {
                Content = content,
                FileName = attachment.OriginalFileName,
                ContentType = attachment.ContentType
            };
        }

        public async Task RemoveAttachmentAsync(
            FinanceRecordKind kind,
            int recordId,
            CancellationToken cancellationToken = default)
        {
            FinanceAttachment? attachment;
            if (kind == FinanceRecordKind.Revenue)
            {
                var revenue = await _context.ManualRevenues
                    .Include(r => r.Attachment)
                    .FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken)
                    ?? throw new InvalidOperationException("Manual revenue entry not found.");
                attachment = revenue.Attachment;
            }
            else
            {
                var expense = await _context.Expenses
                    .Include(e => e.Attachment)
                    .FirstOrDefaultAsync(e => e.Id == recordId, cancellationToken)
                    ?? throw new InvalidOperationException("Expense not found.");
                attachment = expense.Attachment;
            }

            if (attachment == null)
                return;

            var storedFileName = attachment.StoredFileName;
            _context.FinanceAttachments.Remove(attachment);
            await _context.SaveChangesAsync(cancellationToken);
            await DeleteObsoleteFileAsync(storedFileName);
        }

        /// <summary>
        /// Serialises concurrent expense saves for one vendor while a threshold decision is made.
        /// <para>
        /// Working out withholding is a read-check-write: sum the vendor's year to date, decide
        /// whether the annual allowance is exhausted, then insert. Two admins saving at the same
        /// moment can both read the same below-allowance total, both conclude nothing is due, and
        /// both save — leaving the vendor over the threshold with no tax deducted at all.
        /// </para>
        /// <para>
        /// Locking the vendor row is enough, because every threshold aggregate is keyed by vendor,
        /// and it avoids the range-lock deadlocks that a serialisable isolation level would invite
        /// on a shared table. Non-relational providers (the in-memory store used by tests) have no
        /// locking to take, and expenses with no vendor cannot hit a threshold.
        /// </para>
        /// <para>
        /// Every write that moves a vendor's year-to-date total takes the same lock — including
        /// deleting an expense, and both sides of a move from one vendor to another. Rows are
        /// locked in ascending id order so two moves in opposite directions queue instead of
        /// deadlocking.
        /// </para>
        /// </summary>
        private async Task<IDbContextTransaction?> BeginThresholdGuardAsync(
            IEnumerable<int?> vendorIds, CancellationToken cancellationToken)
        {
            var ids = vendorIds.Where(id => id.HasValue).Select(id => id!.Value)
                .Distinct().OrderBy(id => id).ToList();
            if (ids.Count == 0) return null;
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null) return null;

            var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var id in ids)
                    await _context.Database.ExecuteSqlRawAsync(
                        "SELECT TOP 1 1 FROM [Vendors] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}",
                        new object[] { id }, cancellationToken);
                return transaction;
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
        }

        /// <summary>
        /// Resolves the category and vendor an expense was entered against, snapshots their names
        /// onto the row, then hands off to the withholding calculation.
        /// <para>
        /// The names are copied rather than joined so that renaming a category, retiring it, or
        /// correcting a vendor never rewrites what a past expense says it was for. That also keeps
        /// every pre-existing free-text row valid: no category link, just the text that was typed.
        /// </para>
        /// </summary>
        private async Task ApplyExpenseDetailsAsync(Expense expense, CreateExpenseDto dto, CancellationToken cancellationToken)
        {
            if (dto.Amount <= 0)
                throw new InvalidOperationException("Amount must be greater than zero.");

            if (dto.CategoryId.HasValue)
            {
                var category = await _context.ExpenseCategories.AsNoTracking()
                    .Where(c => c.Id == dto.CategoryId.Value)
                    .Select(c => new { c.Id, c.Name, c.IsActive })
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Selected expense category does not exist.");
                // A retired category stays valid on the expense that already used it, so editing an
                // old row does not force the operator to re-classify it.
                if (!category.IsActive && expense.CategoryId != category.Id)
                    throw new InvalidOperationException("Selected expense category is inactive. Choose an active category.");

                expense.CategoryId = category.Id;
                expense.Category = category.Name;
            }
            else
            {
                // Rows that predate the managed list keep the free text they were entered with, so
                // their history stays readable and editing one does not force a re-classification.
                // A new expense has to be classified: with no managed head there is no rate table
                // behind it, which would make "type your own category" a one-click way to pay a
                // taxable supplier with nothing withheld.
                if (expense.Id == 0 || expense.CategoryId != null)
                    throw new InvalidOperationException(
                        "Choose an expense category from the list. If the head you need is missing, add it under Finance ▸ Settings ▸ Expense heads & rates.");
                if (string.IsNullOrWhiteSpace(dto.Category))
                    throw new InvalidOperationException("Category is required.");
                expense.Category = dto.Category.Trim();
            }

            if (dto.VendorId.HasValue)
            {
                var vendor = await _context.Vendors.AsNoTracking()
                    .Where(v => v.Id == dto.VendorId.Value)
                    .Select(v => new { v.Id, v.Name, v.IsActive })
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Selected vendor does not exist.");
                if (!vendor.IsActive && expense.VendorId != vendor.Id)
                    throw new InvalidOperationException("Selected vendor is inactive. Choose an active vendor.");

                expense.VendorId = vendor.Id;
                expense.Vendor = vendor.Name;
            }
            else
            {
                expense.VendorId = null;
                expense.Vendor = string.IsNullOrWhiteSpace(dto.Vendor) ? null : dto.Vendor.Trim();
            }

            await _whtService.ApplyToExpenseAsync(
                expense, dto.WhtRate, dto.WhtAmount, dto.WhtOverrideReason, cancellationToken);
        }

        private async Task EnsureProjectExistsAsync(int? projectId, CancellationToken cancellationToken = default)
        {
            if (!projectId.HasValue)
                return;
            var exists = await _context.Projects.AnyAsync(p => p.Id == projectId.Value, cancellationToken);
            if (!exists)
                throw new InvalidOperationException("Selected project does not exist.");
        }

        private async Task<string?> GetProjectNameAsync(int? projectId)
        {
            if (!projectId.HasValue)
                return null;
            return await _context.Projects
                .Where(p => p.Id == projectId.Value)
                .Select(p => p.ProjectName)
                .FirstOrDefaultAsync();
        }

        private async Task<ManualRevenueResponseDto> MapManualRevenueAsync(ManualRevenue r)
        {
            var account = await GetAccountIdentityAsync(r.FinanceAccountId);
            return new ManualRevenueResponseDto
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                ProjectName = await GetProjectNameAsync(r.ProjectId),
                FinanceAccountId = r.FinanceAccountId,
                FinanceAccountName = account?.Name,
                AccountHolderName = account?.Holder,
                Amount = r.Amount,
                RevenueType = r.RevenueType,
                Description = r.Description,
                Reference = r.Reference,
                Date = r.Date,
                CreatedAt = r.CreatedAt,
                Attachment = MapAttachment(r.Attachment)
            };
        }

        private async Task<ExpenseResponseDto> MapExpenseAsync(Expense e)
        {
            var account = await GetAccountIdentityAsync(e.FinanceAccountId);
            return new ExpenseResponseDto
            {
                Id = e.Id,
                ProjectId = e.ProjectId,
                ProjectName = await GetProjectNameAsync(e.ProjectId),
                FinanceAccountId = e.FinanceAccountId,
                FinanceAccountName = account?.Name,
                AccountHolderName = account?.Holder,
                Amount = e.Amount,
                Category = e.Category,
                CategoryId = e.CategoryId,
                Description = e.Description,
                Vendor = e.Vendor,
                VendorId = e.VendorId,
                Date = e.Date,
                WhtApplied = e.WhtApplied,
                WhtRate = e.WhtRate,
                WhtAmount = e.WhtAmount,
                NetPaid = e.NetPaid,
                WhtRateOverridden = e.WhtRateOverridden,
                WhtOverrideReason = e.WhtOverrideReason,
                WhtTaxSection = e.WhtTaxSection,
                VendorFilerStatusAtEntry = e.VendorFilerStatusAtEntry,
                CreatedAt = e.CreatedAt,
                Attachment = MapAttachment(e.Attachment)
            };
        }

        private async Task<(string Name, string Holder)?> GetAccountIdentityAsync(int? accountId, CancellationToken cancellationToken = default)
        {
            if (!accountId.HasValue)
                return null;

            var account = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == accountId.Value)
                .Select(a => new { a.Name, a.AccountHolderName })
                .SingleOrDefaultAsync(cancellationToken);
            return account == null ? null : (account.Name, account.AccountHolderName);
        }

        private async Task<(string StoredFileName, ValidatedFinanceAttachment Metadata)> SaveAttachmentAsync(
            FinanceAttachmentUpload upload,
            CancellationToken cancellationToken)
        {
            var metadata = FinanceAttachmentFileValidator.Validate(upload);
            var storedFileName = await _attachmentStorage.SaveAsync(upload.Content, metadata.Extension, cancellationToken);
            return (storedFileName, metadata);
        }

        private static void ApplyAttachment(
            FinanceAttachment attachment,
            (string StoredFileName, ValidatedFinanceAttachment Metadata) saved)
        {
            attachment.StoredFileName = saved.StoredFileName;
            attachment.OriginalFileName = saved.Metadata.OriginalFileName;
            attachment.ContentType = saved.Metadata.ContentType;
            attachment.FileSize = saved.Metadata.FileSize;
            attachment.UploadedAt = DateTime.UtcNow;
        }

        private async Task DeleteNewFileAfterFailureAsync(string? storedFileName)
        {
            if (storedFileName == null)
                return;
            try
            {
                await _attachmentStorage.DeleteAsync(storedFileName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not clean up failed finance attachment {StoredFileName}", storedFileName);
            }
        }

        private async Task DeleteObsoleteFileAsync(string? storedFileName)
        {
            if (storedFileName == null)
                return;
            try
            {
                await _attachmentStorage.DeleteAsync(storedFileName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Metadata is already removed/changed, so the orphan is inaccessible. Keep the
                // successful finance operation successful and surface the cleanup issue in logs.
                _logger.LogWarning(ex, "Could not delete obsolete finance attachment {StoredFileName}", storedFileName);
            }
        }

        private static FinanceAttachmentDto? MapAttachment(FinanceAttachment? attachment) =>
            attachment == null
                ? null
                : new FinanceAttachmentDto
                {
                    FileName = attachment.OriginalFileName,
                    ContentType = attachment.ContentType,
                    FileSize = attachment.FileSize,
                    UploadedAt = attachment.UploadedAt
                };

        private static FinanceAttachmentDto? MapAttachment(
            string? fileName,
            string? contentType,
            long? fileSize,
            DateTime? uploadedAt) =>
            fileName == null || contentType == null || fileSize == null || uploadedAt == null
                ? null
                : new FinanceAttachmentDto
                {
                    FileName = fileName,
                    ContentType = contentType,
                    FileSize = fileSize.Value,
                    UploadedAt = uploadedAt.Value
                };

        private static void ValidateAttachmentChange(FinanceAttachmentUpload? attachment, bool removeAttachment)
        {
            if (attachment != null && removeAttachment)
                throw new InvalidOperationException("Choose either a replacement attachment or removal, not both.");
        }

        private static void ValidateRevenue(decimal amount, string revenueType)
        {
            if (amount <= 0)
                throw new InvalidOperationException("Amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(revenueType))
                throw new InvalidOperationException("Revenue type is required.");
        }

        private static string ResolveAutomaticRevenueType(PaymentType type, InstallmentType? installmentType)
        {
            if (type == PaymentType.BookingAmount)
                return "Booking Amount";
            if (installmentType == InstallmentType.Possession)
                return "Possession Payment";
            return "Installment Payment";
        }

        private static string BuildPaymentReference(string? receiptNumber, string bookingReference, string customerName)
        {
            var primary = string.IsNullOrWhiteSpace(receiptNumber) ? bookingReference : receiptNumber;
            return $"{primary} • {customerName}";
        }
    }
}
