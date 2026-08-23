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
            var accruedCommission = await _context.BookingCommissions.AsNoTracking()
                .Where(c => c.Status == BookingCommissionStatus.Pending || c.Status == BookingCommissionStatus.Paid)
                .SumAsync(c => (decimal?)c.FinalAmount, cancellationToken) ?? 0m;
            var commissionPaid = (await _context.CommissionPayouts.AsNoTracking()
                    .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m)
                - (await _context.CommissionPayoutReversals.AsNoTracking()
                    .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m);
            // Still owed to partners: the pending commissions, less whatever has already gone out
            // against them and plus anything since reversed.
            var payableBase = await _context.BookingCommissions.AsNoTracking()
                .Where(c => c.Status == BookingCommissionStatus.Pending)
                .SumAsync(c => (decimal?)c.FinalAmount, cancellationToken) ?? 0m;
            var payablePayouts = await _context.CommissionPayouts.AsNoTracking()
                .Where(p => p.Commission.Status == BookingCommissionStatus.Pending)
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
            var payableReversals = await _context.CommissionPayoutReversals.AsNoTracking()
                .Where(r => r.Payout.Commission.Status == BookingCommissionStatus.Pending)
                .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;
            var payableCommission = Math.Max(0m, payableBase - payablePayouts + payableReversals);
            var approvedRebates = await _context.CustomerRebates.AsNoTracking()
                .Where(r => r.Status == CustomerRebateStatus.Pending || r.Status == CustomerRebateStatus.Applied
                    || r.Status == CustomerRebateStatus.Paid)
                .SumAsync(r => (decimal?)r.FinalAmount, cancellationToken) ?? 0m;
            var rebatePaid = (await _context.RebateDisbursements.AsNoTracking()
                    .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m)
                - (await _context.RebateDisbursementReversals.AsNoTracking()
                    .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m);
            var commissionRecovery = (await _context.CommissionPayouts.AsNoTracking()
                    .Where(p => p.Commission.Status == BookingCommissionStatus.ReversalRequired)
                    .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m)
                - (await _context.CommissionPayoutReversals.AsNoTracking()
                    .Where(r => r.Payout.Commission.Status == BookingCommissionStatus.ReversalRequired)
                    .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m);
            var rebateRecovery = (await _context.RebateDisbursements.AsNoTracking()
                    .Where(d => d.Rebate.Status == CustomerRebateStatus.ReversalRequired)
                    .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m)
                - (await _context.RebateDisbursementReversals.AsNoTracking()
                    .Where(r => r.Disbursement.Rebate.Status == CustomerRebateStatus.ReversalRequired)
                    .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m);
            return new CommissionRebateSummaryDto
            {
                AccruedCommission = accruedCommission,
                PayableCommission = payableCommission,
                CommissionPaid = commissionPaid,
                CommissionReversalRequired = commissionRecovery,
                ApprovedRebates = approvedRebates,
                RebatesAppliedOrPaid = rebatePaid,
                RebateReversalRequired = rebateRecovery,
                ActivePartners = await _context.ThirdPartyPartners.CountAsync(p => p.IsActive, cancellationToken),
                // How many commissions and rebates are still owed.
                PendingRecords = await _context.BookingCommissions.CountAsync(c => c.Status == BookingCommissionStatus.Pending, cancellationToken)
                    + await _context.CustomerRebates.CountAsync(r => r.Status == CustomerRebateStatus.Pending, cancellationToken)
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
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var result = await operation();
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
    }
}
