using System.Data;
using System.Text.RegularExpressions;
using DAMS.Application.Common;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAMS.Application.Services
{
    public sealed partial class CommissionRebateService : ICommissionRebateService
    {
        private readonly AppDbContext _context;
        private readonly IFinanceAccountService _financeAccounts;
        private readonly IFinancialEvidenceStorage _storage;
        private readonly ILogger<CommissionRebateService> _logger;

        public CommissionRebateService(AppDbContext context, IFinanceAccountService financeAccounts,
            IFinancialEvidenceStorage storage, ILogger<CommissionRebateService>? logger = null)
        {
            _context = context;
            _financeAccounts = financeAccounts;
            _storage = storage;
            _logger = logger ?? NullLogger<CommissionRebateService>.Instance;
        }

        public async Task<CommissionRebateSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
        {
            // Everything a partner is owed, whether or not it has been paid yet. Cancelled and
            // reversed rows are excluded: the company no longer owes those.
            var commissions = await _context.BookingCommissions.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Accrued = group.Sum(c =>
                        c.Status == BookingCommissionStatus.Pending || c.Status == BookingCommissionStatus.Paid
                            ? c.FinalAmount
                            : 0m),
                    PayableBase = group.Sum(c => c.Status == BookingCommissionStatus.Pending
                        ? c.FinalAmount
                        : 0m),
                    PendingCount = group.Count(c => c.Status == BookingCommissionStatus.Pending)
                })
                .SingleOrDefaultAsync(cancellationToken);

            var payouts = await _context.CommissionPayouts.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Total = group.Sum(p => p.Amount),
                    Pending = group.Sum(p => p.Commission.Status == BookingCommissionStatus.Pending
                        ? p.Amount
                        : 0m),
                    ReversalRequired = group.Sum(p => p.Commission.Status == BookingCommissionStatus.ReversalRequired
                        ? p.Amount
                        : 0m)
                })
                .SingleOrDefaultAsync(cancellationToken);

            var payoutReversals = await _context.CommissionPayoutReversals.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Total = group.Sum(r => r.Amount),
                    Pending = group.Sum(r => r.Payout.Commission.Status == BookingCommissionStatus.Pending
                        ? r.Amount
                        : 0m),
                    ReversalRequired = group.Sum(r =>
                        r.Payout.Commission.Status == BookingCommissionStatus.ReversalRequired
                            ? r.Amount
                            : 0m)
                })
                .SingleOrDefaultAsync(cancellationToken);
            // Still owed to partners: the pending commissions, less whatever has already gone out
            // against them and plus anything since reversed.
            //
            // This is the MANAGEMENT view and it is deliberately not the same number as the Balance
            // Sheet's Commission Payable. Both are built from the same facts and agree on every
            // status but one: a ReversalRequired commission reads zero here and NEGATIVE on the
            // sheet, because the money already paid on a void sale is recoverable from the partner.
            // That amount is reported here on its own line instead, so the two reconcile exactly —
            // sheet payable = PayableCommission − CommissionReversalRequired — rather than
            // disagreeing. Everything else (Pending, Paid, Cancelled, Reversed) is identical.
            var payableCommission = Math.Max(0m,
                (commissions?.PayableBase ?? 0m) - (payouts?.Pending ?? 0m) + (payoutReversals?.Pending ?? 0m));
            // Everything promised to customers that is still standing — cancelled and reversed
            // rebates are the only ones left out. A rebate is granted the moment it is entered;
            // there is no approval between the two.
            var rebates = await _context.CustomerRebates.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Granted = group.Sum(r =>
                        r.Status == CustomerRebateStatus.Pending || r.Status == CustomerRebateStatus.Applied
                            || r.Status == CustomerRebateStatus.Paid
                            ? r.FinalAmount
                            : 0m),
                    PendingCount = group.Count(r => r.Status == CustomerRebateStatus.Pending)
                })
                .SingleOrDefaultAsync(cancellationToken);

            var disbursements = await _context.RebateDisbursements.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Total = group.Sum(d => d.Amount),
                    ReversalRequired = group.Sum(d => d.Rebate.Status == CustomerRebateStatus.ReversalRequired
                        ? d.Amount
                        : 0m)
                })
                .SingleOrDefaultAsync(cancellationToken);

            var disbursementReversals = await _context.RebateDisbursementReversals.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Total = group.Sum(r => r.Amount),
                    ReversalRequired = group.Sum(r =>
                        r.Disbursement.Rebate.Status == CustomerRebateStatus.ReversalRequired
                            ? r.Amount
                            : 0m)
                })
                .SingleOrDefaultAsync(cancellationToken);
            return new CommissionRebateSummaryDto
            {
                AccruedCommission = commissions?.Accrued ?? 0m,
                PayableCommission = payableCommission,
                CommissionPaid = (payouts?.Total ?? 0m) - (payoutReversals?.Total ?? 0m),
                CommissionReversalRequired = (payouts?.ReversalRequired ?? 0m)
                    - (payoutReversals?.ReversalRequired ?? 0m),
                RebatesGranted = rebates?.Granted ?? 0m,
                RebatesAppliedOrPaid = (disbursements?.Total ?? 0m) - (disbursementReversals?.Total ?? 0m),
                RebateReversalRequired = (disbursements?.ReversalRequired ?? 0m)
                    - (disbursementReversals?.ReversalRequired ?? 0m),
                ActivePartners = await _context.ThirdPartyPartners.CountAsync(p => p.IsActive, cancellationToken),
                // How many commissions and rebates are still owed.
                PendingRecords = (commissions?.PendingCount ?? 0) + (rebates?.PendingCount ?? 0)
            };
        }

        // A commission or rebate entered straight on the booking screen carries no rule name and needs
        // no typed justification, but the audit log still has to say what was agreed. These render the
        // entry itself ("2% of net sale price") so an untitled record is never anonymous in the log.
        private static string BasisLabel(FinancialCalculationBasis basis) => basis switch
        {
            FinancialCalculationBasis.AgreedSalePrice => "sale price",
            FinancialCalculationBasis.NetSalePriceAfterDiscount => "net sale price",
            FinancialCalculationBasis.BookingAmountReceived => "booking amount received",
            FinancialCalculationBasis.AmountActuallyCollected => "amount collected",
            _ => "manually approved amount"
        };

        private static string DescribeCalculation(FinancialCalculationType type, decimal? rate,
            decimal? fixedAmount, FinancialCalculationBasis basis) =>
            type == FinancialCalculationType.Percentage
                ? $"{rate ?? 0m:0.######}% of {BasisLabel(basis)}"
                : $"Fixed amount of {fixedAmount ?? 0m:0.00}";

        private static string MethodLabel(CustomerRebateMethod method) => method switch
        {
            CustomerRebateMethod.OutstandingBalanceReduction => "a reduction of the outstanding balance",
            CustomerRebateMethod.InstallmentAdjustment => "an installment adjustment",
            CustomerRebateMethod.CashOrBankPayment => "a cash or bank payment",
            CustomerRebateMethod.CreditNote => "a credit note",
            _ => "another method"
        };

        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
        private static decimal Rate(decimal value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);
        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static string Required(string? value, string label, int max)
        {
            var result = Clean(value) ?? throw new InvalidOperationException($"{label} is required.");
            if (result.Length > max) throw new InvalidOperationException($"{label} cannot exceed {max} characters.");
            return result;
        }

        private static string? Limited(string? value, string label, int max)
        {
            var result = Clean(value);
            if (result?.Length > max) throw new InvalidOperationException($"{label} cannot exceed {max} characters.");
            return result;
        }

        private static string? Normalize(string? value)
        {
            var clean = Clean(value);
            return clean == null ? null : Regex.Replace(clean, "[^A-Za-z0-9@.+]", string.Empty).ToUpperInvariant();
        }

        private static string? NormalizePhone(string? value)
        {
            var clean = Clean(value);
            return clean == null ? null : Regex.Replace(clean, "[^0-9]", string.Empty);
        }

        private static string? NormalizeEmail(string? value) => Clean(value)?.ToUpperInvariant();

        private void ApplyToken<TEntity>(TEntity entity, string? token, string label) where TEntity : class
        {
            var property = _context.Entry(entity).Property<byte[]>("RowVersion");
            if (string.IsNullOrWhiteSpace(token))
            {
                if (property.CurrentValue is { Length: > 0 })
                    throw new InvalidOperationException($"The {label} version is missing. Refresh and try again.");
                return;
            }
            try { property.OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException($"The {label} version is invalid. Refresh and try again."); }
        }

        private static string Token(byte[]? value) => value is { Length: > 0 } ? Convert.ToBase64String(value) : string.Empty;

        private FinancialWorkflowAuditEntry Audit(FinancialWorkflowAction action, FinancialWorkflowActor actor, int? partnerId = null,
            int? customerId = null, int? bookingId = null, int? commissionId = null, int? payoutId = null,
            int? rebateId = null, int? rebateDisbursementId = null, BookingCommissionStatus? oldCommission = null,
            BookingCommissionStatus? newCommission = null, CustomerRebateStatus? oldRebate = null,
            CustomerRebateStatus? newRebate = null, decimal? previousAmount = null, decimal? newAmount = null,
            string? reason = null, int? commissionRuleId = null, int? commissionRuleRevisionId = null)
        {
            var entry = new FinancialWorkflowAuditEntry
            {
                PartnerId = partnerId, CustomerId = customerId, BookingId = bookingId, CommissionId = commissionId,
                PayoutId = payoutId, RebateId = rebateId, RebateDisbursementId = rebateDisbursementId,
                CommissionRuleId = commissionRuleId, CommissionRuleRevisionId = commissionRuleRevisionId,
                Action = action, PreviousCommissionStatus = oldCommission, NewCommissionStatus = newCommission,
                PreviousRebateStatus = oldRebate, NewRebateStatus = newRebate,
                // Not length-capped: the reason may be a machine-generated rule change summary that
                // records exact before/after values for every field and can exceed a fixed cap.
                // User-entered reasons are already validated at their call sites. The column is
                // unbounded, so a long summary is preserved in full instead of failing the save.
                PreviousAmount = previousAmount, NewAmount = newAmount, Reason = Clean(reason),
                PerformedByUserId = actor.UserId, PerformedByName = Limited(actor.DisplayName, "Actor name", 200),
                OccurredAt = DateTime.UtcNow
            };
            _context.FinancialWorkflowAuditEntries.Add(entry);
            return entry;
        }

        private async Task<T> SerializableAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
        {
            if (!_context.Database.IsRelational()) return await operation();
            var strategy = _context.Database.CreateExecutionStrategy();
            var replaying = false;
            return await strategy.ExecuteAsync(async () =>
            {
                // A transient fault rolls the attempt back in the DATABASE and nowhere else: the
                // change tracker still holds everything it wrote, and EF fixup has already stitched
                // those entities into the collections the guards read. So the retry measured itself
                // against its own undone work — reversing a disbursement a second time reported
                // "Reversal exceeds the disbursement balance of 0.00", because the reversal it was
                // retrying was still counted against the balance it was checking.
                if (replaying) _context.ChangeTracker.Clear();
                replaying = true;
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var result = await operation();
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
    }
}
