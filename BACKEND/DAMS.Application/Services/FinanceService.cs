using System.Data;
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
    public partial class FinanceService : IFinanceService
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

        public async Task<FinancialSummaryDto> GetSummaryAsync(int? projectId, DateTime? from, DateTime? to,
            int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default)
        {
            var fromValue = from?.Date;
            var toValue = to?.Date;
            // Bounds only — NOT the both-or-neither rule, which belongs at the API boundary: an
            // open-ended call like GetSummaryAsync(null, null, someDate) is a legitimate
            // balance-as-at-a-date question. ExclusiveEnd covers the upper bound; this covers the
            // lower one, so a date SQL Server cannot store is refused with a message rather than
            // reaching the database and coming back as a conversion fault.
            EnsureFilterBound(fromValue, "From date");
            var toExclusive = ExclusiveEnd(toValue);

            // The cards come out of the SAME grouped read set the dashboard's chart and pie are
            // folded from — see FinanceService.Dashboard.cs. Not merely an optimisation: while the
            // cards summed each source and the chart grouped the same sources again, the two could
            // drift on any filter one of them got subtly wrong, and nothing on the screen would say
            // so. There is now one reading of each source and three views of it.
            var aggregates = await LoadPeriodAggregatesAsync(
                projectId, fromValue, toExclusive, accountId, unassigned, cancellationToken);
            return await BuildSummaryAsync(
                aggregates, fromValue, toValue, toExclusive, projectId, accountId, unassigned, cancellationToken);
        }

        /// <summary>
        /// The cards, from an already-loaded read set plus the few figures that are not period totals
        /// of a financial source: the deposit balance, the outstanding/overdue snapshots and the
        /// selected account's position.
        /// </summary>
        private async Task<FinancialSummaryDto> BuildSummaryAsync(
            PeriodAggregates aggregates, DateTime? fromValue, DateTime? toValue, DateTime? toExclusive,
            int? projectId, int? accountId, bool unassigned, CancellationToken cancellationToken)
        {
            var accountFilterApplied = accountId.HasValue || unassigned;

            // Outstanding and overdue are balance snapshots as they stand TODAY, deliberately not
            // date-filtered. The summary and the paged tables share these SQL projections so their
            // totals always reconcile.
            var outstandingTotal = 0m;
            var overdueTotal = 0m;
            // A BALANCE, not a period total: what customers have paid in that the company still
            // owes, as at the END of the selected range. Suppressed under an account filter for
            // the same reason Outstanding is — a deposit belongs to a booking, not to a bank.
            var customerDeposits = 0m;
            if (!accountFilterApplied)
            {
                outstandingTotal = await OutstandingBalanceQuery(projectId)
                    .SumAsync(x => (decimal?)x.OutstandingAmount, cancellationToken) ?? 0m;
                overdueTotal = await OverdueBalanceQuery(projectId)
                    .SumAsync(x => (decimal?)x.OverdueAmount, cancellationToken) ?? 0m;
                customerDeposits = await CustomerDepositBalanceAsync(projectId, toExclusive, cancellationToken);
            }

            // When a single account is selected, surface its running balance up to the end of
            // the selected period. This is account-wide (project filter is ignored) because the
            // balance is a property of the account, matching the Accounts detail view.
            //
            // The balance comes from AccountSnapshotsAsync — the SAME computation the Balance Sheet
            // and Trial Balance are built from — rather than from a second implementation here. The
            // second implementation is what went wrong: it knew about payments, expenses, assets,
            // loans in cash and staff floats, and knew nothing about partner capital, so a bank that
            // held nothing but a Rs 5,000,000 partner contribution reported a Current Balance of
            // zero while the account ledger and the Balance Sheet both reported five million. The
            // same gap applied to every account whose balance is not plain cash movement — a loan
            // liability, a capital account, Tax Payable, Customer Deposits. One computation cannot
            // disagree with itself.
            decimal? accountOpeningBalance = null;
            decimal? accountCurrentBalance = null;
            decimal? accountNetMovement = null;
            if (accountId.HasValue)
            {
                var account = await _context.FinanceAccounts.AsNoTracking()
                    .Where(a => a.Id == accountId.Value)
                    .Select(a => new { a.OpeningBalance })
                    .SingleOrDefaultAsync(cancellationToken);
                if (account is not null)
                {
                    var reportEnd = toValue ?? PakistanTime.Today;
                    var openingDate = await OpeningDateAsync(cancellationToken);
                    var snapshots = await AccountSnapshotsAsync(null, reportEnd, openingDate, cancellationToken, accountId.Value);
                    accountCurrentBalance = snapshots.SingleOrDefault(s => s.Id == accountId.Value)?.Balance ?? 0m;
                    // Gated on the go-live date for the same reason the balance beside it is: an
                    // opening balance dated 1 August is not part of what the account held on 31 July,
                    // and showing it there while the balance correctly excludes it made the two
                    // figures on the same card contradict each other.
                    accountOpeningBalance = !openingDate.HasValue || openingDate.Value <= reportEnd
                        ? account.OpeningBalance
                        : 0m;

                    // Cash movement over the selected period — not the same thing as the balance
                    // change above, and not the same thing as profit. Expenses count at what
                    // actually left the account and FBR deposits count too, even though neither
                    // matches a P&L figure. This is what the screen shows in place of Net Profit
                    // while an account is selected.
                    accountNetMovement = await AccountPeriodCashMovementAsync(
                        fromValue, toExclusive, accountId.Value, cancellationToken);
                }
            }

            return new FinancialSummaryDto
            {
                CustomerDepositsBalance = Money(customerDeposits),
                AutomaticRevenue = Money(aggregates.AutomaticRevenue),
                ManualRevenue = Money(aggregates.ManualRevenueTotal),
                TotalRevenue = Money(aggregates.TotalRevenue),
                TotalExpenses = Money(aggregates.TotalExpenses),
                // NOT REPORTED under an account filter, and null rather than a number for a reason.
                // A recognised sale moves no cash, so it belongs to no bank: selecting a bank drops
                // every possession-recognised sale out of the revenue side while every expense paid
                // from that bank stays. The subtraction still produced a figure, and the screen
                // still called it Net Profit — a loss shown for a bank that had funded a profitable
                // month. Profitability is a property of the business over a period, not of an
                // account, so when an account is chosen there is no Net Profit to state; the screen
                // shows that account's cash movement instead.
                NetProfit = accountFilterApplied
                    ? null
                    : Money(aggregates.TotalRevenue - aggregates.TotalExpenses),
                AccountFilterApplied = accountFilterApplied,
                WhtWithheld = Money(aggregates.WhtWithheld),
                TotalAssetPurchases = Money(aggregates.FixedAssetCharge),
                OutstandingAmount = Money(outstandingTotal),
                OverdueAmount = Money(overdueTotal),
                AccountOpeningBalance = accountOpeningBalance,
                AccountCurrentBalance = accountCurrentBalance,
                AccountNetMovement = accountNetMovement
            };
        }

        // ── Paged table rows ────────────────────────────────────────────────────────
        // Each method fetches `take + 1` rows in SQL (OFFSET/FETCH) so HasMore is known
        // without a separate COUNT query.

        public async Task<PagedResult<RevenueLineDto>> GetRevenuePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default)
        {
            var fromValue = from?.Date;
            var toExclusive = ExclusiveEnd(to);

            // Recognised sales and manual revenue are unioned (UNION ALL) into one shape, ordered
            // and paged in SQL. A stable secondary key (Source + entity Id) keeps paging
            // deterministic across chunks.
            //
            // Customer payments are NOT here. They are cash receipts against a deposit or a
            // receivable, never revenue in their own right — they live in the Customer Deposits
            // view and on the account ledgers instead.
            var recognisedSales = SaleRecognitionQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(r => new RevenueRow
                {
                    SortId = r.Id,
                    Date = r.RecognitionDate,
                    ProjectId = r.Booking.Unit.ProjectId,
                    ProjectName = r.Booking.Unit.Project.ProjectName,
                    Amount = r.NetSaleValue,
                    Source = "Unit Sale",
                    ManualRevenueId = null,
                    PaymentType = null,
                    InstallmentType = null,
                    ReceiptNumber = null,
                    BookingReference = r.Booking.BookingReference,
                    CustomerName = r.Booking.Customer.FullName,
                    RevenueType = "Unit Sale (possession)",
                    RevenueCategoryId = null,
                    Reference = r.Booking.BookingReference,
                    Description = r.Booking.Customer.FullName + " · Unit " + r.Booking.Unit.UnitNumber,
                    AttachmentFileName = null,
                    AttachmentContentType = null,
                    AttachmentFileSize = null,
                    AttachmentUploadedAt = null,
                    FinanceAccountId = null,
                    FinanceAccountName = null,
                    AccountHolderName = null,
                    RowVersion = null
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
                    RevenueCategoryId = r.RevenueCategoryId,
                    Reference = r.Reference,
                    Description = r.Description,
                    AttachmentFileName = r.Attachment != null ? r.Attachment.OriginalFileName : null,
                    AttachmentContentType = r.Attachment != null ? r.Attachment.ContentType : null,
                    AttachmentFileSize = r.Attachment != null ? r.Attachment.FileSize : null,
                    AttachmentUploadedAt = r.Attachment != null ? r.Attachment.UploadedAt : null,
                    FinanceAccountId = r.FinanceAccountId,
                    FinanceAccountName = r.FinanceAccount != null ? r.FinanceAccount.Name : null,
                    AccountHolderName = r.FinanceAccount != null ? r.FinanceAccount.AccountHolderName : null,
                    RowVersion = r.RowVersion
                });

            // What the company keeps on a cancellation — real income, recognised once on the
            // cancellation date. The refund itself is not shown here: it never was revenue, so
            // paying it back is not negative revenue.
            var retained = RetainedCancellationQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(s => new RevenueRow
                {
                    SortId = s.Id,
                    Date = s.CancellationDate,
                    ProjectId = s.Booking.Unit.ProjectId,
                    ProjectName = s.Booking.Unit.Project.ProjectName,
                    Amount = s.RetainedAmount,
                    Source = "Cancellation Retained",
                    ManualRevenueId = null,
                    PaymentType = null,
                    InstallmentType = null,
                    ReceiptNumber = null,
                    BookingReference = s.Booking.BookingReference,
                    CustomerName = s.Booking.Customer.FullName,
                    RevenueType = "Cancellation Income (Retained)",
                    RevenueCategoryId = null,
                    Reference = s.Booking.BookingReference,
                    Description = s.Reason,
                    AttachmentFileName = null,
                    AttachmentContentType = null,
                    AttachmentFileSize = null,
                    AttachmentUploadedAt = null,
                    FinanceAccountId = null,
                    FinanceAccountName = null,
                    AccountHolderName = null,
                    RowVersion = null
                });

            var raw = await recognisedSales.Concat(manual).Concat(retained)
                .OrderByDescending(x => x.Date)
                .ThenBy(x => x.Source)
                .ThenByDescending(x => x.SortId)
                .Skip(skip).Take(take + 1)
                .ToListAsync(cancellationToken);

            var items = raw.Take(take).Select(r => new RevenueLineDto
            {
                Date = r.Date,
                ProjectId = r.ProjectId,
                ProjectName = r.ProjectName ?? "General",
                Amount = r.Amount,
                Source = r.Source,
                ManualRevenueId = r.ManualRevenueId,
                RevenueType = r.RevenueType ?? string.Empty,
                RevenueCategoryId = r.RevenueCategoryId,
                Reference = r.Reference,
                Description = r.Description,
                FinanceAccountId = r.FinanceAccountId,
                FinanceAccountName = r.FinanceAccountName,
                AccountHolderName = r.AccountHolderName,
                ConcurrencyToken = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion),
                Attachment = MapAttachment(r.AttachmentFileName, r.AttachmentContentType, r.AttachmentFileSize, r.AttachmentUploadedAt)
            }).ToList();

            return new PagedResult<RevenueLineDto> { Items = items, HasMore = raw.Count > take };
        }

        /// <summary>
        /// One expense, in the same shape the drill-down list uses — including the concurrency
        /// token, which is the whole point: an editor opened from the Total Expenses breakdown has
        /// to be able to save, and a save without the record's own RowVersion is a lost update.
        /// Null when the id is not an expense (or was deleted since the list was drawn).
        /// </summary>
        public async Task<ExpenseLineDto?> GetExpenseAsync(int id, CancellationToken cancellationToken = default) =>
            await _context.Expenses.AsNoTracking()
                .Where(e => e.Id == id)
                .Select(ExpenseLineProjection)
                .SingleOrDefaultAsync(cancellationToken);

        public async Task<PagedResult<ExpenseLineDto>> GetExpensePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default)
        {
            var fromValue = from?.Date;
            var toExclusive = ExclusiveEnd(to);

            var rows = await ExpenseQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => e.Id)
                .Skip(skip).Take(take + 1)
                .Select(ExpenseLineProjection)
                .ToListAsync(cancellationToken);

            return Page(rows, take);
        }

        /// <summary>
        /// The single definition of an expense row. Shared by the list and the by-id lookup so the
        /// editor cannot be handed a differently-shaped record than the row it was opened from.
        /// </summary>
        private static readonly System.Linq.Expressions.Expression<Func<Expense, ExpenseLineDto>> ExpenseLineProjection =
            e => new ExpenseLineDto
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
                    ConcurrencyToken = Convert.ToBase64String(e.RowVersion),
                    Attachment = e.Attachment == null ? null : new FinanceAttachmentDto
                    {
                        FileName = e.Attachment.OriginalFileName,
                        ContentType = e.Attachment.ContentType,
                        FileSize = e.Attachment.FileSize,
                        UploadedAt = e.Attachment.UploadedAt
                    }
                };

        /// <summary>
        /// Customer money held but not yet earned, one row per booking, as at <paramref name="to"/>
        /// (all of time when null). A BALANCE view, not a period view: <c>from</c> is deliberately
        /// ignored, because summing deposits over a range would be summing a liability's closing
        /// position with its own movements.
        /// <para>
        /// A booking drops off this list the moment possession recognises its sale or a
        /// cancellation clears it — on the date that happened, not on the date the page is opened.
        /// </para>
        /// </summary>
        public async Task<PagedResult<CustomerDepositLineDto>> GetCustomerDepositPageAsync(
            int? projectId, DateTime? to, int skip, int take, CancellationToken cancellationToken = default)
        {
            var end = ExclusiveEnd(to);

            var payments = _context.Payments.AsNoTracking()
                .Where(p => !end.HasValue || p.PaidAt < end.Value)
                .GroupBy(p => p.BookingId)
                .Select(g => new { BookingId = g.Key, Amount = g.Sum(p => (decimal?)p.Amount) });

            var bookings = _context.Bookings.AsNoTracking().AsQueryable();
            if (projectId.HasValue) bookings = bookings.Where(b => b.Unit.ProjectId == projectId.Value);

            var rows =
                from booking in bookings
                join payment in payments on booking.Id equals payment.BookingId
                let recognitionDate = booking.SaleRecognition != null
                    ? (DateTime?)booking.SaleRecognition.RecognitionDate : null
                let cancellationDate = booking.CancellationSettlement != null
                    ? (DateTime?)booking.CancellationSettlement.CancellationDate : null
                let cash = payment.Amount ?? 0m
                // Cleared BY the as-at date, not "cleared at some point": a report for last month
                // must still show the deposit that only came off the books this month.
                let cleared = (recognitionDate.HasValue && (!end.HasValue || recognitionDate.Value < end.Value))
                    || (cancellationDate.HasValue && (!end.HasValue || cancellationDate.Value < end.Value))
                let balance = cleared ? 0m : cash
                where balance != 0m
                select new CustomerDepositLineDto
                {
                    BookingId = booking.Id,
                    BookingReference = booking.BookingReference,
                    CustomerName = booking.Customer.FullName,
                    ProjectId = booking.Unit.ProjectId,
                    ProjectName = booking.Unit.Project != null ? booking.Unit.Project.ProjectName : "General",
                    UnitNumber = booking.Unit.UnitNumber,
                    NetSaleValue = booking.AgreedSalePrice - booking.DiscountAmount,
                    CustomerCashReceived = cash,
                    DepositBalance = balance,
                    RecognitionDate = recognitionDate,
                    CancellationDate = cancellationDate
                };

            var page = await rows.OrderByDescending(r => r.DepositBalance).ThenBy(r => r.BookingId)
                .Skip(skip).Take(take + 1).ToListAsync(cancellationToken);
            // Status is an enum: formatted in memory because enum.ToString does not translate.
            var statuses = await _context.Bookings.AsNoTracking()
                .Where(b => page.Select(r => r.BookingId).Contains(b.Id))
                .Select(b => new { b.Id, b.Status }).ToListAsync(cancellationToken);
            foreach (var row in page)
                row.BookingStatus = statuses.FirstOrDefault(s => s.Id == row.BookingId)?.Status.ToString() ?? string.Empty;

            return Page(page, take);
        }

        public async Task<PagedResult<OutstandingLineDto>> GetOutstandingPageAsync(int? projectId, int skip, int take, CancellationToken cancellationToken = default)
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

        public async Task<PagedResult<OverdueLineDto>> GetOverduePageAsync(int? projectId, int skip, int take, CancellationToken cancellationToken = default)
        {
            // Fetch the page with Type as an enum, then format it in memory (enum.ToString
            // is not reliably translatable to SQL).
            var balances = OverdueBalanceQuery(projectId)
                .OrderBy(x => x.DueDate).ThenBy(x => x.SortId);

            var raw = await balances.Skip(skip).Take(take + 1).ToListAsync(cancellationToken);

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

        /// <summary>
        /// Every line behind Net Profit: revenue (+), costs (−), and the fixed assets bought in the
        /// period (−, as ordinary costs). The signed amounts add up to the Net Profit card for the
        /// same filters, so an operator can total the rows in front of them and arrive at the figure
        /// they clicked.
        /// </summary>
        public async Task<PagedResult<NetProfitLineDto>> GetNetProfitPageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default)
        {
            var fromValue = from?.Date;
            var toExclusive = ExclusiveEnd(to);

            // Every revenue line (+) and cost line (−), unioned, ordered and paged in SQL. Customer
            // payments are absent by design: they move cash between a bank and a deposit or
            // receivable, and never touch profit.
            var recognisedSales = SaleRecognitionQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(r => new NetProfitRow
                {
                    SortId = r.Id,
                    Date = r.RecognitionDate,
                    ProjectName = r.Booking.Unit.Project.ProjectName,
                    Kind = "revenue",
                    Amount = r.NetSaleValue,
                    IsPayment = false,
                    PaymentType = null,
                    InstallmentType = null,
                    Label = "Unit sale — " + r.Booking.BookingReference
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

            // The obligation, split into the two directions the list understands: taking a
            // commission on is a cost, releasing or reducing one gives that cost back. The payout
            // itself is absent — it settles the payable and never touches profit.
            var commissionAccrued = CommissionAccrualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Where(a => a.Amount > 0m)
                .Select(a => new NetProfitRow
                {
                    SortId = a.Id, Date = a.AccruedOn, ProjectName = a.Commission.Booking.Unit.Project.ProjectName,
                    Kind = "expense", Amount = a.Amount, IsPayment = false, PaymentType = null,
                    InstallmentType = null, Label = "Partner commission"
                });
            var commissionReleased = CommissionAccrualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Where(a => a.Amount < 0m)
                .Select(a => new NetProfitRow
                {
                    SortId = a.Id, Date = a.AccruedOn, ProjectName = a.Commission.Booking.Unit.Project.ProjectName,
                    Kind = "revenue", Amount = -a.Amount, IsPayment = false, PaymentType = null,
                    InstallmentType = null, Label = "Partner commission released"
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

            var loanInterest = LoanInterestQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(t => new NetProfitRow
                {
                    SortId = t.Id, Date = t.Date, ProjectName = "General", Kind = "expense",
                    Amount = t.InterestAmount, IsPayment = false, PaymentType = null,
                    InstallmentType = null, Label = "Loan Interest"
                });

            // The retained slice of a cancelled booking's deposit — the only part of a cancellation
            // that is income. The refund is a liability movement and does not belong on this list.
            var retained = RetainedCancellationQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(s => new NetProfitRow
                {
                    SortId = s.Id, Date = s.CancellationDate, ProjectName = s.Booking.Unit.Project.ProjectName,
                    Kind = "revenue", Amount = s.RetainedAmount, IsPayment = false, PaymentType = null,
                    InstallmentType = null, Label = "Cancellation income (retained)"
                });
            // Credits granted against a recognised sale, dated at the later of the credit and the
            // recognition — the cost of revenue that will never be collected.
            var nonCashCredits = NonCashCreditQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(d => new NetProfitRow
                {
                    SortId = d.Id,
                    Date = d.AppliedAt < d.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                        ? d.Rebate.Booking.SaleRecognition!.RecognitionDate : d.AppliedAt,
                    ProjectName = d.Rebate.Booking.Unit.Project.ProjectName,
                    Kind = "expense", Amount = d.Amount, IsPayment = false, PaymentType = null,
                    InstallmentType = null, Label = "Customer credit (non-cash)"
                });
            var nonCashCreditReversals = NonCashCreditReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(r => new NetProfitRow
                {
                    SortId = r.Id,
                    Date = r.ReversedAt < r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                        ? r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate : r.ReversedAt,
                    ProjectName = r.Disbursement.Rebate.Booking.Unit.Project.ProjectName,
                    Kind = "revenue", Amount = r.Amount, IsPayment = false, PaymentType = null,
                    InstallmentType = null, Label = "Customer credit reversal"
                });

            // Fixed-asset purchases, as ordinary cost rows: the client's rule is that buying an asset
            // is spending, so it belongs in the same list as every other cost with nothing for the
            // reader to add back. The item name is kept in the label so the row is still recognisable
            // as a purchase rather than an invoice.
            var assetPurchases = FixedAssetChargeQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(p => new NetProfitRow
                {
                    SortId = p.Id, Date = p.Date,
                    ProjectName = p.Project != null ? p.Project.ProjectName : "General",
                    Kind = "expense", Amount = p.Amount, IsPayment = false, PaymentType = null,
                    InstallmentType = null, Label = "Fixed asset purchase — " + p.ItemName
                });

            var raw = await recognisedSales.Concat(manual).Concat(expenses).Concat(commissionAccrued)
                .Concat(commissionReleased).Concat(rebatePayments).Concat(rebateReversals)
                .Concat(loanInterest).Concat(retained)
                .Concat(nonCashCredits).Concat(nonCashCreditReversals).Concat(assetPurchases)
                .OrderByDescending(x => x.Date)
                .ThenBy(x => x.Kind)
                .ThenByDescending(x => x.SortId)
                .Skip(skip).Take(take + 1)
                .ToListAsync(cancellationToken);

            var items = raw.Take(take).Select(r => new NetProfitLineDto
            {
                Date = r.Date,
                ProjectName = r.ProjectName ?? "General",
                Kind = r.Kind,
                Label = r.Label ?? string.Empty,
                // Anything that is not revenue reduces profit, so it is negative. Naming "revenue"
                // as the positive case rather than "expense" as the negative one means a future kind
                // cannot default itself into income.
                Amount = r.Kind == "revenue" ? r.Amount : -r.Amount
            }).ToList();

            return new PagedResult<NetProfitLineDto> { Items = items, HasMore = raw.Count > take };
        }

        /// <summary>
        /// Every line behind the Total Expenses card: costs (+) and the things that give a cost back
        /// (−). Built from exactly the components <see cref="GetSummaryAsync"/> adds into
        /// <see cref="FinancialSummaryDto.TotalExpenses"/>, in the same order, so the signed amounts
        /// over every page total that card to the paisa.
        /// <para>
        /// This exists because the card is NOT the expense table. Total Expenses is ordinary expenses
        /// plus commissions, rebates, non-cash customer credits, loan interest and fixed-asset
        /// purchases, each net of its reversals — so an operator who clicked the card and was shown
        /// only <see cref="GetExpensePageAsync"/> saw a table that could not account for its own
        /// heading, with the difference silently absent rather than reported.
        /// </para>
        /// </summary>
        public async Task<PagedResult<CostLineDto>> GetCostBreakdownPageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default)
        {
            var fromValue = from?.Date;
            var toExclusive = ExclusiveEnd(to);

            var expenses = ExpenseQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(e => new CostRow
                {
                    SortId = e.Id, Date = e.Date, ProjectName = e.Project!.ProjectName,
                    Source = "expense", Kind = "cost", Amount = e.Amount, ExpenseId = e.Id, Label = e.Category,
                    AttachmentFileName = e.Attachment != null ? e.Attachment.OriginalFileName : null,
                    AttachmentContentType = e.Attachment != null ? e.Attachment.ContentType : null,
                    AttachmentFileSize = e.Attachment != null ? e.Attachment.FileSize : 0L,
                    AttachmentUploadedAt = e.Attachment != null ? e.Attachment.UploadedAt : (DateTime?)null
                });
            // Built from the obligation ledger, exactly as the Total Expenses card is. A payout is a
            // payable settlement and is not a cost, so it is not listed under a cost heading.
            var commissionAccrued = CommissionAccrualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Where(a => a.Amount > 0m)
                .Select(a => new CostRow
                {
                    SortId = a.Id, Date = a.AccruedOn, ProjectName = a.Commission.Booking.Unit.Project.ProjectName,
                    Source = "commission", Kind = "cost", Amount = a.Amount, ExpenseId = null, Label = "Partner commission",
                    AttachmentFileName = null, AttachmentContentType = null,
                    AttachmentFileSize = 0L, AttachmentUploadedAt = (DateTime?)null
                });
            var commissionReleased = CommissionAccrualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Where(a => a.Amount < 0m)
                .Select(a => new CostRow
                {
                    SortId = a.Id, Date = a.AccruedOn, ProjectName = a.Commission.Booking.Unit.Project.ProjectName,
                    Source = "commission", Kind = "reduction", Amount = -a.Amount, ExpenseId = null, Label = "Partner commission released",
                    AttachmentFileName = null, AttachmentContentType = null,
                    AttachmentFileSize = 0L, AttachmentUploadedAt = (DateTime?)null
                });
            var rebatePayments = CashRebateQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(d => new CostRow
                {
                    SortId = d.Id, Date = d.AppliedAt, ProjectName = d.Rebate.Booking.Unit.Project.ProjectName,
                    Source = "rebate", Kind = "cost", Amount = d.Amount, ExpenseId = null, Label = "Customer rebate",
                    AttachmentFileName = null, AttachmentContentType = null,
                    AttachmentFileSize = 0L, AttachmentUploadedAt = (DateTime?)null
                });
            var rebateReversals = CashRebateReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(r => new CostRow
                {
                    SortId = r.Id, Date = r.ReversedAt, ProjectName = r.Disbursement.Rebate.Booking.Unit.Project.ProjectName,
                    Source = "rebate", Kind = "reduction", Amount = r.Amount, ExpenseId = null, Label = "Customer rebate reversal",
                    AttachmentFileName = null, AttachmentContentType = null,
                    AttachmentFileSize = 0L, AttachmentUploadedAt = (DateTime?)null
                });
            // Dated at the later of the credit and the recognition, exactly as the summary and the
            // Net Profit list date them — a credit cannot be a cost before the sale it reduces is
            // income, so a period that predates possession must not pick it up.
            var nonCashCredits = NonCashCreditQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(d => new CostRow
                {
                    SortId = d.Id,
                    Date = d.AppliedAt < d.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                        ? d.Rebate.Booking.SaleRecognition!.RecognitionDate : d.AppliedAt,
                    ProjectName = d.Rebate.Booking.Unit.Project.ProjectName,
                    Source = "customerCredit", Kind = "cost", Amount = d.Amount, ExpenseId = null, Label = "Customer credit (non-cash)",
                    AttachmentFileName = null, AttachmentContentType = null,
                    AttachmentFileSize = 0L, AttachmentUploadedAt = (DateTime?)null
                });
            var nonCashCreditReversals = NonCashCreditReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(r => new CostRow
                {
                    SortId = r.Id,
                    Date = r.ReversedAt < r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                        ? r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate : r.ReversedAt,
                    ProjectName = r.Disbursement.Rebate.Booking.Unit.Project.ProjectName,
                    Source = "customerCredit", Kind = "reduction", Amount = r.Amount, ExpenseId = null, Label = "Customer credit reversal",
                    AttachmentFileName = null, AttachmentContentType = null,
                    AttachmentFileSize = 0L, AttachmentUploadedAt = (DateTime?)null
                });
            var loanInterest = LoanInterestQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(t => new CostRow
                {
                    SortId = t.Id, Date = t.Date, ProjectName = "General",
                    Source = "loanInterest", Kind = "cost", Amount = t.InterestAmount, ExpenseId = null, Label = "Loan Interest",
                    AttachmentFileName = null, AttachmentContentType = null,
                    AttachmentFileSize = 0L, AttachmentUploadedAt = (DateTime?)null
                });
            var assetPurchases = FixedAssetChargeQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(p => new CostRow
                {
                    SortId = p.Id, Date = p.Date,
                    ProjectName = p.Project != null ? p.Project.ProjectName : "General",
                    Source = "assetPurchase", Kind = "cost", Amount = p.Amount, ExpenseId = null,
                    Label = "Fixed asset purchase — " + p.ItemName,
                    AttachmentFileName = p.Attachment != null ? p.Attachment.OriginalFileName : null,
                    AttachmentContentType = p.Attachment != null ? p.Attachment.ContentType : null,
                    AttachmentFileSize = p.Attachment != null ? p.Attachment.FileSize : 0L,
                    AttachmentUploadedAt = p.Attachment != null ? p.Attachment.UploadedAt : (DateTime?)null
                });

            var raw = await expenses.Concat(commissionAccrued).Concat(commissionReleased)
                .Concat(rebatePayments).Concat(rebateReversals)
                .Concat(nonCashCredits).Concat(nonCashCreditReversals)
                .Concat(loanInterest).Concat(assetPurchases)
                .OrderByDescending(x => x.Date)
                .ThenBy(x => x.Kind)
                .ThenByDescending(x => x.SortId)
                .Skip(skip).Take(take + 1)
                .ToListAsync(cancellationToken);

            var items = raw.Take(take).Select(r => new CostLineDto
            {
                Date = r.Date,
                ProjectName = r.ProjectName ?? "General",
                Kind = r.Kind,
                Label = r.Label ?? string.Empty,
                ExpenseId = r.ExpenseId,
                Source = r.Source,
                // SortId is the row's own primary key in every branch above — the same value the
                // ordering already leans on — so the drill-down can address the record it drew
                // without a second column carrying the same number.
                SourceId = r.SortId,
                // "cost" is named as the positive case rather than "reduction" as the negative one,
                // so a component added here later cannot default itself into giving money back.
                Amount = r.Kind == "cost" ? r.Amount : -r.Amount,
                // Built after materialising, not inside the union: every branch of a Concat has to
                // project the same flat shape, and only two of the nine have a file at all.
                Attachment = r.AttachmentFileName == null ? null : new FinanceAttachmentDto
                {
                    FileName = r.AttachmentFileName,
                    ContentType = r.AttachmentContentType ?? "application/octet-stream",
                    FileSize = r.AttachmentFileSize,
                    UploadedAt = r.AttachmentUploadedAt ?? default
                }
            }).ToList();

            return new PagedResult<CostLineDto> { Items = items, HasMore = raw.Count > take };
        }

        private sealed class CostRow
        {
            public int SortId { get; set; }
            public DateTime Date { get; set; }
            public string? ProjectName { get; set; }
            public string Kind { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public int? ExpenseId { get; set; }
            public string Source { get; set; } = string.Empty;
            public string? Label { get; set; }
            public string? AttachmentFileName { get; set; }
            public string? AttachmentContentType { get; set; }
            public long AttachmentFileSize { get; set; }
            public DateTime? AttachmentUploadedAt { get; set; }
        }

        /// <summary>
        /// The selected account's period movement, without rereading its entire history at both
        /// endpoints. Each source contributes its existing signed amount to one SQL sum. Sources
        /// with two account sides (assets, staff cash, loans and capital) are read once each.
        /// This remains account-wide and independent of the opening-balance cutover.
        /// </summary>
        private async Task<decimal> AccountPeriodCashMovementAsync(
            DateTime? fromValue, DateTime? toExclusive, int accountId, CancellationToken cancellationToken)
        {
            // GetSummaryAsync also supports open-ended and reversed bounds. For a reversed window,
            // the previous difference of cumulative sums was the negative of the intervening period.
            var direction = 1m;
            if (fromValue.HasValue && toExclusive.HasValue && fromValue.Value > toExclusive.Value)
            {
                (fromValue, toExclusive) = (toExclusive, fromValue);
                direction = -1m;
            }

            var amounts = PaymentsQuery(null, fromValue, toExclusive, accountId)
                .Select(p => p.Amount)
                .Concat(ManualQuery(null, fromValue, toExclusive, accountId).Select(r => r.Amount))
                // Supplier withholding stays in the cash account until the FBR deposit is paid.
                .Concat(ExpenseQuery(null, fromValue, toExclusive, accountId)
                    .Select(e => -(e.Amount - e.WhtAmount)))
                .Concat(CommissionPayoutQuery(null, fromValue, toExclusive, accountId, false)
                    .Select(p => -p.Amount))
                .Concat(CommissionReversalQuery(null, fromValue, toExclusive, accountId, false)
                    .Select(r => r.Amount))
                .Concat(CashRebateQuery(null, fromValue, toExclusive, accountId, false)
                    .Select(d => -d.Amount))
                .Concat(CashRebateReversalQuery(null, fromValue, toExclusive, accountId, false)
                    .Select(r => r.Amount))
                // The asset rises by gross cost while cash falls by the net amount paid.
                .Concat(AssetPurchaseQuery(null, fromValue, toExclusive, null, null, false)
                    .Where(p => p.AssetAccountId == accountId || p.FinanceAccountId == accountId)
                    .Select(p => (p.AssetAccountId == accountId ? p.Amount : 0m)
                        - (p.FinanceAccountId == accountId ? p.Amount - p.WhtAmount : 0m)))
                .Concat(_context.WhtDeposits.AsNoTracking()
                    .Where(d => d.FinanceAccountId == accountId
                        && (!fromValue.HasValue || d.DepositDate >= fromValue.Value)
                        && (!toExclusive.HasValue || d.DepositDate < toExclusive.Value))
                    .Select(d => -d.Amount))
                .Concat(_context.LoanTransactions.AsNoTracking()
                    .Where(t => t.FinanceAccountId == accountId
                        && (t.Type == LoanTransactionType.Drawdown || t.Type == LoanTransactionType.Repayment)
                        && (!fromValue.HasValue || t.Date >= fromValue.Value)
                        && (!toExclusive.HasValue || t.Date < toExclusive.Value))
                    .Select(t => t.Type == LoanTransactionType.Drawdown
                        ? t.PrincipalAmount : -(t.PrincipalAmount + t.InterestAmount)))
                .Concat(_context.CapitalTransactions.AsNoTracking()
                    .Where(t => t.FinanceAccountId == accountId
                        && (t.Type == CapitalTransactionType.Contribution || t.Type == CapitalTransactionType.Withdrawal)
                        && (!fromValue.HasValue || t.Date >= fromValue.Value)
                        && (!toExclusive.HasValue || t.Date < toExclusive.Value))
                    .Select(t => t.Type == CapitalTransactionType.Contribution ? t.Amount : -t.Amount))
                .Concat(_context.StaffCashTransfers.AsNoTracking()
                    .Where(t => (t.StaffFinanceAccountId == accountId || t.CounterpartyFinanceAccountId == accountId)
                        && (!fromValue.HasValue || t.Date >= fromValue.Value)
                        && (!toExclusive.HasValue || t.Date < toExclusive.Value))
                    .Select(t =>
                        ((t.StaffFinanceAccountId == accountId && t.Type == StaffCashMovementType.FundsGiven)
                            || (t.CounterpartyFinanceAccountId == accountId && t.Type == StaffCashMovementType.FundsReturned)
                                ? t.Amount : 0m)
                        - ((t.StaffFinanceAccountId == accountId && t.Type == StaffCashMovementType.FundsReturned)
                            || (t.CounterpartyFinanceAccountId == accountId && t.Type == StaffCashMovementType.FundsGiven)
                                ? t.Amount : 0m)))
                // A promised cancellation refund does not move cash until its actual payout.
                .Concat(CancellationRefundCashQuery(fromValue, toExclusive, accountId)
                    .Select(r => -r.Amount));

            return direction * (await amounts.SumAsync(amount => (decimal?)amount, cancellationToken) ?? 0m);
        }

        // Filtered base queries shared by summary totals and paged rows.
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

        // ── Sale recognition: the one event that turns a booking into revenue ───────────────
        // Dated at RecognitionDate (the Pakistan business date possession was given), never at the
        // booking's CURRENT status: a booking that reaches possession today must not appear as
        // revenue in a report for a period that closed months ago.
        //
        // Account filter: a recognised sale moves no cash, so it belongs to no bank account. Its
        // accounting counterpart is the Customer Receivables system account, and that is the only
        // account selection it answers to — the same rule the refund payable already follows.
        private IQueryable<BookingSaleRecognition> SaleRecognitionQuery(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId = null, bool unassigned = false)
        {
            var q = _context.BookingSaleRecognitions.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(r => r.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(r => r.RecognitionDate >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(r => r.RecognitionDate < toExclusive.Value);
            if (accountId.HasValue)
                q = q.Where(_ => _context.FinanceAccounts.Any(a => a.Id == accountId.Value
                    && a.SystemRole == FinanceSystemAccountRole.CustomerReceivables));
            else if (unassigned) q = q.Where(_ => false);
            return q;
        }

        // The part of a cancelled booking's deposit the company keeps. THIS is the cancellation's
        // income — not the customer's original payments, which were never revenue, and not the
        // refund, which is a liability rather than a contra-revenue. Recognised on the cancellation
        // date; the later payout moves cash only.
        private IQueryable<BookingCancellationSettlement> RetainedCancellationQuery(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId = null, bool unassigned = false)
        {
            var q = _context.BookingCancellationSettlements.AsNoTracking().Where(s => s.RetainedAmount > 0m);
            if (projectId.HasValue) q = q.Where(s => s.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(s => s.CancellationDate >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(s => s.CancellationDate < toExclusive.Value);
            // The retained amount is released out of the Customer Deposits liability, so that is
            // the account it answers to.
            if (accountId.HasValue)
                q = q.Where(_ => _context.FinanceAccounts.Any(a => a.Id == accountId.Value
                    && a.SystemRole == FinanceSystemAccountRole.CustomerDeposits));
            else if (unassigned) q = q.Where(_ => false);
            return q;
        }

        /// <summary>
        /// Non-cash customer credits (balance reduction / credit note / installment adjustment) on
        /// bookings whose sale HAS been recognised.
        /// <para>
        /// Before recognition these credits are invisible to finance, exactly as they always were:
        /// nothing has been booked for the customer to owe. Once the full net sale value is
        /// recognised as revenue, however, the part of it the customer will never pay has to land
        /// somewhere — otherwise the receivable would claim money that was given away. That is what
        /// this is: the cost of the credit, recognised at possession (or on its own day if granted
        /// later), against a receivable reduced by exactly the same amount, exactly once.
        /// </para>
        /// </summary>
        private IQueryable<RebateDisbursement> NonCashCreditQuery(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId = null, bool unassigned = false)
        {
            var q = _context.RebateDisbursements.AsNoTracking()
                .Where(d => d.Rebate.Booking.SaleRecognition != null
                    && (d.Method == CustomerRebateMethod.OutstandingBalanceReduction
                        || d.Method == CustomerRebateMethod.InstallmentAdjustment
                        || d.Method == CustomerRebateMethod.CreditNote));
            if (projectId.HasValue) q = q.Where(d => d.Rebate.Booking.Unit.ProjectId == projectId.Value);
            // A credit granted before possession is only recognised AT possession, so its effective
            // date is the later of the two. Compared inline rather than through a stored column so
            // a back-dated credit can never disagree with the recognition it belongs to.
            if (fromValue.HasValue)
                q = q.Where(d => (d.AppliedAt < d.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                    ? d.Rebate.Booking.SaleRecognition!.RecognitionDate : d.AppliedAt) >= fromValue.Value);
            if (toExclusive.HasValue)
                q = q.Where(d => (d.AppliedAt < d.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                    ? d.Rebate.Booking.SaleRecognition!.RecognitionDate : d.AppliedAt) < toExclusive.Value);
            if (accountId.HasValue)
                q = q.Where(_ => _context.FinanceAccounts.Any(a => a.Id == accountId.Value
                    && a.SystemRole == FinanceSystemAccountRole.CustomerReceivables));
            else if (unassigned) q = q.Where(_ => false);
            return q;
        }

        private IQueryable<RebateDisbursementReversal> NonCashCreditReversalQuery(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId = null, bool unassigned = false)
        {
            var q = _context.RebateDisbursementReversals.AsNoTracking()
                .Where(r => r.Disbursement.Rebate.Booking.SaleRecognition != null
                    && (r.Disbursement.Method == CustomerRebateMethod.OutstandingBalanceReduction
                        || r.Disbursement.Method == CustomerRebateMethod.InstallmentAdjustment
                        || r.Disbursement.Method == CustomerRebateMethod.CreditNote));
            if (projectId.HasValue) q = q.Where(r => r.Disbursement.Rebate.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue)
                q = q.Where(r => (r.ReversedAt < r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                    ? r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate : r.ReversedAt) >= fromValue.Value);
            if (toExclusive.HasValue)
                q = q.Where(r => (r.ReversedAt < r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                    ? r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate : r.ReversedAt) < toExclusive.Value);
            if (accountId.HasValue)
                q = q.Where(_ => _context.FinanceAccounts.Any(a => a.Id == accountId.Value
                    && a.SystemRole == FinanceSystemAccountRole.CustomerReceivables));
            else if (unassigned) q = q.Where(_ => false);
            return q;
        }

        // Cancellation settlements with a positive refund — the refund-payable side of a
        // cancellation. Dated at CancellationDate, the Pakistan business date the obligation was
        // recognised on — never CancelledAt (a raw UTC instant that can land on the wrong calendar
        // day around Pakistan midnight) and never the later cash payout date. Filtered by
        // RefundPayableAccountId, matching every other account-scoped query here: the accounting
        // counterpart of a refund is the liability account, not the bank the cash eventually leaves
        // from.
        private IQueryable<BookingCancellationSettlement> CancellationSettlementQuery(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId = null, bool unassigned = false)
        {
            var q = _context.BookingCancellationSettlements.AsNoTracking().Where(s => s.RefundAmount > 0m);
            if (projectId.HasValue) q = q.Where(s => s.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(s => s.CancellationDate >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(s => s.CancellationDate < toExclusive.Value);
            if (accountId.HasValue) q = q.Where(s => s.RefundPayableAccountId == accountId.Value);
            else if (unassigned) q = q.Where(_ => false);
            return q;
        }

        // Actual cancellation-refund cash payouts, dated at PaidAt — used only for cash-account
        // outflow, never for P&L (the P&L effect happened at cancellation, not at payout).
        private IQueryable<BookingCancellationRefund> CancellationRefundCashQuery(DateTime? fromValue, DateTime? toExclusive, int accountId)
        {
            var q = _context.BookingCancellationRefunds.AsNoTracking().Where(r => r.FinanceAccountId == accountId);
            if (fromValue.HasValue) q = q.Where(r => r.PaidAt >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(r => r.PaidAt < toExclusive.Value);
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

        private IQueryable<LoanTransaction> LoanInterestQuery(int? projectId, DateTime? fromValue, DateTime? toExclusive,
            int? accountId = null, bool unassigned = false)
        {
            var q = _context.LoanTransactions.AsNoTracking()
                .Where(t => t.Type == LoanTransactionType.Repayment && t.InterestAmount != 0m);
            // Loans are company-wide until project attribution is explicitly designed. They must
            // not leak into a selected project's profitability.
            if (projectId.HasValue || unassigned) q = q.Where(_ => false);
            if (fromValue.HasValue) q = q.Where(t => t.Date >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(t => t.Date < toExclusive.Value);
            if (accountId.HasValue) q = q.Where(t => t.FinanceAccountId == accountId.Value);
            return q;
        }

        /// <summary>
        /// The dated commission obligation: what the company took onto its books as owed to partners,
        /// and what it later released. This — not the payout — is the commission expense.
        /// <para>
        /// A payout is a settlement of the payable, so it has no P&amp;L effect at all; the accrual
        /// carries both the charge to profit and the credit to Commission Payable, which is why an
        /// account filter here resolves to that liability account rather than to any bank.
        /// </para>
        /// </summary>
        private IQueryable<CommissionAccrual> CommissionAccrualQuery(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId, bool unassigned)
        {
            var q = _context.CommissionAccruals.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(a => a.Commission.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(a => a.AccruedOn >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(a => a.AccruedOn < toExclusive.Value);
            if (accountId.HasValue)
                q = q.Where(_ => _context.FinanceAccounts.Any(a => a.Id == accountId.Value
                    && a.SystemRole == FinanceSystemAccountRole.CommissionPayable));
            else if (unassigned) q = q.Where(_ => false);
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
            // A booking-level credit settles installments too — BookingCreditPolicy has already
            // decided which ones. Reading only the credits that name an installment is what used to
            // report a customer overdue for money a rebate had already taken off their balance.
            var allocations = _context.RebateCreditAllocations.AsNoTracking()
                .GroupBy(a => a.InstallmentId)
                .Select(g => new { InstallmentId = g.Key, Amount = g.Sum(a => (decimal?)a.Amount) });

            return
                from installment in OverdueInstallments(projectId)
                join payment in payments on installment.Id equals payment.InstallmentId into paymentGroup
                from payment in paymentGroup.DefaultIfEmpty()
                join credit in credits on installment.Id equals credit.InstallmentId into creditGroup
                from credit in creditGroup.DefaultIfEmpty()
                join reversal in reversals on installment.Id equals reversal.InstallmentId into reversalGroup
                from reversal in reversalGroup.DefaultIfEmpty()
                join allocation in allocations on installment.Id equals allocation.InstallmentId into allocationGroup
                from allocation in allocationGroup.DefaultIfEmpty()
                let settled = (payment.Amount ?? 0m) + (credit.Amount ?? 0m) - (reversal.Amount ?? 0m)
                    + (allocation.Amount ?? 0m)
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
            public int? RevenueCategoryId { get; set; }
            public string? Reference { get; set; }
            public string? Description { get; set; }
            public string? AttachmentFileName { get; set; }
            public string? AttachmentContentType { get; set; }
            public long? AttachmentFileSize { get; set; }
            public DateTime? AttachmentUploadedAt { get; set; }
            public int? FinanceAccountId { get; set; }
            public string? FinanceAccountName { get; set; }
            public string? AccountHolderName { get; set; }

            /// <summary>
            /// Null on the recognised-sale and retained-cancellation branches: those are events, not
            /// editable records, so they have no version to guard. Carried as raw bytes rather than a
            /// base64 string because the encoding does not translate to SQL — and bound in EVERY
            /// branch because EF aligns a union on the first branch's members and drops any that one
            /// leaves unset.
            /// </summary>
            public byte[]? RowVersion { get; set; }
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
            ValidateRevenue(dto.Amount, dto.RevenueType, dto.RevenueCategoryId);
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Received In Account is required.");
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId.Value, null, cancellationToken);
            var category = await ResolveRevenueCategoryAsync(dto.RevenueCategoryId, dto.RevenueType, null, cancellationToken);

            var revenue = new ManualRevenue
            {
                ProjectId = dto.ProjectId,
                FinanceAccountId = dto.FinanceAccountId,
                Amount = dto.Amount,
                RevenueCategoryId = category.Id,
                RevenueTypeName = category.Name,
                RevenueType = category.Name,
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                Reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference.Trim(),
                Date = await FinanceDateRules.ResolveAsync(_context, dto.Date, "Revenue date", cancellationToken),
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
            ApplyRowVersion(revenue, dto.ConcurrencyToken, "revenue entry");
            ValidateRevenue(dto.Amount, dto.RevenueType, dto.RevenueCategoryId);
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Received In Account is required.");
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId.Value, revenue.FinanceAccountId, cancellationToken);
            var category = await ResolveRevenueCategoryAsync(dto.RevenueCategoryId, dto.RevenueType, revenue.RevenueCategoryId, cancellationToken);
            // Resolved before the attachment is written to disk: a date the rules reject must not
            // leave an orphaned upload behind. An omitted date keeps whatever the row already has,
            // so a legacy row can still be corrected without being forced onto a new date.
            var revenueDate = dto.Date.HasValue
                ? await FinanceDateRules.ResolveAsync(_context, dto.Date, "Revenue date", cancellationToken)
                : revenue.Date;

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
            revenue.RevenueCategoryId = category.Id;
            revenue.RevenueTypeName = category.Name;
            revenue.RevenueType = category.Name;
            revenue.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            revenue.Reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference.Trim();
            revenue.Date = revenueDate;

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

        public async Task DeleteManualRevenueAsync(int id, string? concurrencyToken = null, CancellationToken cancellationToken = default)
        {
            var revenue = await _context.ManualRevenues
                .Include(r => r.Attachment)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (revenue == null)
                throw new InvalidOperationException("Manual revenue entry not found.");
            // Deleting is as destructive as editing and races the same way: one admin correcting the
            // amount while another removes the row must not both appear to succeed.
            ApplyRowVersion(revenue, concurrencyToken, "revenue entry");

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
            await _accountService.EnsureExpenseSourceAsync(dto.FinanceAccountId.Value, null, cancellationToken);

            var expense = new Expense
            {
                ProjectId = dto.ProjectId,
                FinanceAccountId = dto.FinanceAccountId,
                Amount = dto.Amount,
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                Date = await FinanceDateRules.ResolveAsync(_context, dto.Date, "Expense date", cancellationToken),
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

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

            try
            {
                await ExecuteResilientlyAsync(async () =>
                {
                    // Held until after SaveChanges so the year-to-date read and the insert that
                    // depends on it cannot be interleaved with another save for the same vendor.
                    await using var thresholdGuard = await BeginThresholdGuardAsync(
                        new[] { dto.VendorId }, cancellationToken);
                    // Inside the guard because this is where the threshold is read and the tax
                    // decided. Re-runnable: it recomputes onto the same entity.
                    await ApplyExpenseDetailsAsync(expense, dto, cancellationToken);
                    _context.Expenses.Add(expense);
                    await _context.SaveChangesAsync(cancellationToken);
                    if (thresholdGuard != null)
                        await thresholdGuard.CommitAsync(cancellationToken);
                });
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
            ApplyRowVersion(expense, dto.ConcurrencyToken, "expense");
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Paid From Account is required.");
            await _accountService.EnsureExpenseSourceAsync(dto.FinanceAccountId.Value, expense.FinanceAccountId, cancellationToken);
            // Resolved before the threshold transaction opens and before the attachment is written
            // to disk, so a date the rules reject costs neither a lock nor an orphaned upload. An
            // omitted date keeps whatever the row already has.
            var expenseDate = dto.Date.HasValue
                ? await FinanceDateRules.ResolveAsync(_context, dto.Date, "Expense date", cancellationToken)
                : expense.Date;

            var oldStoredFileName = expense.Attachment?.StoredFileName;
            string? newStoredFileName = null;
            (string StoredFileName, ValidatedFinanceAttachment Metadata)? savedAttachment = null;
            if (attachment != null)
            {
                savedAttachment = await SaveAttachmentAsync(attachment, cancellationToken);
                newStoredFileName = savedAttachment.Value.StoredFileName;
            }

            // Tax already withheld on this row is the amount FBR may have been paid, so a lower
            // figure has to be checked against the deposits — which needs the serialisable window.
            var withheldBefore = expense.WhtAmount;

            try
            {
                await ExecuteResilientlyAsync(async () =>
                {
                    // Editing re-decides the threshold too, so it needs the same protection as
                    // creating — for the vendor being left as well as the one being joined.
                    await using var thresholdGuard = await BeginThresholdGuardAsync(
                        new[] { expense.VendorId, dto.VendorId }, cancellationToken,
                        serialisable: withheldBefore > 0m);

                    if (savedAttachment.HasValue)
                    {
                        expense.Attachment ??= new FinanceAttachment { ExpenseId = expense.Id };
                        ApplyAttachment(expense.Attachment, savedAttachment.Value);
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
                    expense.Date = expenseDate;
                    // Re-resolved after the date and amount move, because both feed the threshold check
                    // and therefore the tax.
                    await ApplyExpenseDetailsAsync(expense, dto, cancellationToken);
                    await _whtService.EnsureDepositsStayCoveredAsync(
                        expense.WhtAmount - withheldBefore, cancellationToken);

                    await _context.SaveChangesAsync(cancellationToken);
                    if (thresholdGuard != null)
                        await thresholdGuard.CommitAsync(cancellationToken);
                });
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

        public async Task DeleteExpenseAsync(int id, string? concurrencyToken = null, CancellationToken cancellationToken = default)
        {
            var expense = await _context.Expenses
                .Include(e => e.Attachment)
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (expense == null)
                throw new InvalidOperationException("Expense not found.");
            ApplyRowVersion(expense, concurrencyToken, "expense");

            var storedFileName = expense.Attachment?.StoredFileName;
            await ExecuteResilientlyAsync(async () =>
            {
                // Removing an expense lowers the vendor's year-to-date total, so it moves the same
                // aggregate a save reads. Without the lock a concurrent entry can decide the threshold
                // against a row that is about to disappear.
                await using var thresholdGuard = await BeginThresholdGuardAsync(
                    new[] { expense.VendorId }, cancellationToken, serialisable: expense.WhtAmount > 0m);
                // Deleting takes the whole withheld amount off the payable. If FBR has already been
                // paid it, there is nothing left for the deposit to have come from.
                await _whtService.EnsureDepositsStayCoveredAsync(-expense.WhtAmount, cancellationToken);
                _context.Expenses.Remove(expense);
                await _context.SaveChangesAsync(cancellationToken);
                if (thresholdGuard != null)
                    await thresholdGuard.CommitAsync(cancellationToken);
            });
            await DeleteObsoleteFileAsync(storedFileName);
        }

        public async Task<FinanceAttachmentDownload> GetAttachmentAsync(
            FinanceRecordKind kind,
            int recordId,
            CancellationToken cancellationToken = default)
        {
            // Switched, not a two-way ternary: with a third record kind an "else" would quietly
            // look the id up in the wrong table and either 404 or serve someone else's file.
            var attachment = kind switch
            {
                FinanceRecordKind.Revenue => await _context.ManualRevenues.AsNoTracking()
                    .Where(r => r.Id == recordId && r.Attachment != null)
                    .Select(r => r.Attachment!)
                    .FirstOrDefaultAsync(cancellationToken),
                FinanceRecordKind.Expense => await _context.Expenses.AsNoTracking()
                    .Where(e => e.Id == recordId && e.Attachment != null)
                    .Select(e => e.Attachment!)
                    .FirstOrDefaultAsync(cancellationToken),
                FinanceRecordKind.AssetPurchase => await _context.AssetPurchases.AsNoTracking()
                    .Where(p => p.Id == recordId && p.Attachment != null)
                    .Select(p => p.Attachment!)
                    .FirstOrDefaultAsync(cancellationToken),
                _ => throw new InvalidOperationException("Unknown finance record kind.")
            };

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
            else if (kind == FinanceRecordKind.AssetPurchase)
            {
                var purchase = await _context.AssetPurchases
                    .Include(p => p.Attachment)
                    .FirstOrDefaultAsync(p => p.Id == recordId, cancellationToken)
                    ?? throw new InvalidOperationException("Asset purchase not found.");
                attachment = purchase.Attachment;
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
        /// <para>
        /// <paramref name="serialisable"/> escalates the window for the one case a row lock cannot
        /// cover: a record that already carries withholding tax, where the check is "is this still
        /// at least what has been deposited with FBR". That reads the deposit history, which no
        /// vendor lock protects, so under READ COMMITTED a deposit could commit between the read and
        /// the delete and leave Tax Payable negative. Serialisable holds the range it read until
        /// commit, which is the same protection the deposit path takes from its side — and it is
        /// asked for only when the stored row has tax on it, so ordinary expense entry keeps the
        /// cheaper row lock.
        /// </para>
        /// </summary>
        private async Task<IDbContextTransaction?> BeginThresholdGuardAsync(
            IEnumerable<int?> vendorIds, CancellationToken cancellationToken, bool serialisable = false)
        {
            var ids = vendorIds.Where(id => id.HasValue).Select(id => id!.Value)
                .Distinct().OrderBy(id => id).ToList();
            if (ids.Count == 0 && !serialisable) return null;
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null) return null;

            var transaction = serialisable
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : await _context.Database.BeginTransactionAsync(cancellationToken);
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
        /// Runs one transactional unit through the retrying execution strategy.
        /// <para>
        /// Not optional. The API registers SQL Server with <c>EnableRetryOnFailure</c>, and EF
        /// refuses to execute ANY operation inside a transaction the caller opened itself while a
        /// retrying strategy is configured — <c>OnFirstExecution</c> throws "the configured execution
        /// strategy 'SqlServerRetryingExecutionStrategy' does not support user-initiated
        /// transactions". So every path that takes the threshold guard has to be a retriable unit or
        /// it cannot run at all on the real database: saving an expense against a vendor threw before
        /// this wrapper existed, which is every expense that carries withholding tax.
        /// </para>
        /// <para>
        /// The delegate can be re-executed, so it must hold only work that is safe to repeat:
        /// database mutations on entities the change tracker already knows about. Entity
        /// construction, file writes and file deletions stay outside it — a retry that built a
        /// second entity would insert both, and one that re-saved an upload would leave the first
        /// copy orphaned on disk.
        /// </para>
        /// </summary>
        private Task ExecuteResilientlyAsync(Func<Task> operation) =>
            _context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

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
                RevenueCategoryId = r.RevenueCategoryId,
                Description = r.Description,
                Reference = r.Reference,
                Date = r.Date,
                CreatedAt = r.CreatedAt,
                ConcurrencyToken = Token(r.RowVersion),
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
                ConcurrencyToken = Token(e.RowVersion),
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

        /// <summary>
        /// Pins the row version the caller loaded onto the tracked entity, so the UPDATE/DELETE
        /// carries it in its WHERE clause and EF raises a concurrency exception rather than letting a
        /// stale copy win.
        /// <para>
        /// A missing token is tolerated only when the row genuinely has none — an in-memory test
        /// store, or a row read before the version column existed. Once a real version is present,
        /// omitting it is an error rather than a licence to overwrite: silently accepting it would
        /// make the protection opt-out by simply not sending a field.
        /// </para>
        /// </summary>
        private void ApplyRowVersion<TEntity>(TEntity entity, string? token, string label)
            where TEntity : class
        {
            var property = _context.Entry(entity).Property<byte[]>(nameof(Expense.RowVersion));
            if (string.IsNullOrWhiteSpace(token))
            {
                if (property.CurrentValue is { Length: > 0 })
                    throw new DbUpdateConcurrencyException($"The {label} version is missing. Refresh and try again.");
                return;
            }
            try { property.OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException)
            {
                throw new DbUpdateConcurrencyException($"The {label} version is invalid. Refresh and try again.");
            }
        }

        private static string Token(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

        private static void ValidateRevenue(decimal amount, string revenueType, int? categoryId)
        {
            if (amount <= 0)
                throw new InvalidOperationException("Amount must be greater than zero.");
            if (!categoryId.HasValue && string.IsNullOrWhiteSpace(revenueType))
                throw new InvalidOperationException("Revenue type is required.");
        }

        private async Task<(int? Id, string Name)> ResolveRevenueCategoryAsync(
            int? categoryId, string revenueType, int? currentCategoryId, CancellationToken cancellationToken)
        {
            if (categoryId.HasValue)
            {
                var row = await _context.RevenueCategories.AsNoTracking().Where(c => c.Id == categoryId)
                    .Select(c => new { c.Id, c.Name, c.IsActive }).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Selected revenue category does not exist.");
                if (!row.IsActive && currentCategoryId != row.Id)
                    throw new InvalidOperationException("Selected revenue category is inactive. Choose an active category.");
                return (row.Id, row.Name);
            }

            var name = revenueType.Trim();
            var matched = await _context.RevenueCategories.AsNoTracking().Where(c => c.Name == name)
                .Select(c => new { c.Id, c.Name, c.IsActive }).SingleOrDefaultAsync(cancellationToken);
            if (matched != null)
            {
                if (!matched.IsActive && currentCategoryId != matched.Id)
                    throw new InvalidOperationException("Selected revenue category is inactive. Choose an active category.");
                return (matched.Id, matched.Name);
            }
            // Test and legacy stores created before the managed list can still preserve their text.
            // A migrated production store always has seeded categories, so new unclassified values
            // cannot bypass the controlled list.
            if (await _context.RevenueCategories.AnyAsync(cancellationToken))
                throw new InvalidOperationException("Choose a revenue category from the managed list.");
            return (null, name);
        }

        private static string DescribeCustomerCash(PaymentType type, InstallmentType? installmentType)
        {
            if (type == PaymentType.BookingAmount)
                return "Booking Amount";
            if (installmentType == InstallmentType.Possession)
                return "Possession Payment";
            return "Installment Payment";
        }
    }
}
