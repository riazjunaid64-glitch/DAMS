using System.Data;
using System.Text.RegularExpressions;
using DAMS.Application.Common;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed partial class CommissionRebateService : ICommissionRebateService
    {
        private readonly AppDbContext _context;
        private readonly IFinanceAccountService _financeAccounts;
        private readonly IFinancialEvidenceStorage _storage;

        public CommissionRebateService(AppDbContext context, IFinanceAccountService financeAccounts,
            IFinancialEvidenceStorage storage)
        {
            _context = context;
            _financeAccounts = financeAccounts;
            _storage = storage;
        }

        public async Task<CommissionRebateSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
        {
            var payableStatuses = new[] { BookingCommissionStatus.Payable, BookingCommissionStatus.PartiallyPaid };

            var accruedCommission = await _context.BookingCommissions.AsNoTracking()
                .Where(c => c.Status == BookingCommissionStatus.Approved || c.Status == BookingCommissionStatus.Earned
                    || c.Status == BookingCommissionStatus.Payable || c.Status == BookingCommissionStatus.PartiallyPaid
                    || c.Status == BookingCommissionStatus.Paid)
                .SumAsync(c => (decimal?)(c.ApprovedAmount ?? c.FinalAmount), cancellationToken) ?? 0m;
            var commissionPaid = (await _context.CommissionPayouts.AsNoTracking()
                    .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m)
                - (await _context.CommissionPayoutReversals.AsNoTracking()
                    .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m);
            var payableRows = await _context.BookingCommissions.AsNoTracking()
                .Where(c => payableStatuses.Contains(c.Status))
                .Select(c => new { c.Id, Amount = c.ApprovedAmount ?? c.FinalAmount })
                .ToListAsync(cancellationToken);
            var payablePayouts = await _context.CommissionPayouts.AsNoTracking()
                .Where(p => payableStatuses.Contains(p.Commission.Status))
                .GroupBy(p => p.CommissionId)
                .Select(g => new { CommissionId = g.Key, Amount = g.Sum(p => p.Amount) })
                .ToDictionaryAsync(x => x.CommissionId, x => x.Amount, cancellationToken);
            var payableReversals = await _context.CommissionPayoutReversals.AsNoTracking()
                .Where(r => payableStatuses.Contains(r.Payout.Commission.Status))
                .GroupBy(r => r.Payout.CommissionId)
                .Select(g => new { CommissionId = g.Key, Amount = g.Sum(r => r.Amount) })
                .ToDictionaryAsync(x => x.CommissionId, x => x.Amount, cancellationToken);
            var payableCommission = payableRows
                .Sum(c => Math.Max(0m, c.Amount - payablePayouts.GetValueOrDefault(c.Id)
                    + payableReversals.GetValueOrDefault(c.Id)));
            var approvedRebates = await _context.CustomerRebates.AsNoTracking()
                .Where(r => r.Status == CustomerRebateStatus.Approved || r.Status == CustomerRebateStatus.PartiallyApplied
                    || r.Status == CustomerRebateStatus.Applied || r.Status == CustomerRebateStatus.Paid)
                .SumAsync(r => (decimal?)(r.ApprovedAmount ?? r.FinalAmount), cancellationToken) ?? 0m;
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
                PendingApprovals = await _context.BookingCommissions.CountAsync(c => c.Status == BookingCommissionStatus.PendingApproval, cancellationToken)
                    + await _context.CustomerRebates.CountAsync(r => r.Status == CustomerRebateStatus.PendingApproval, cancellationToken)
            };
        }

        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
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
            string? reason = null, int? commissionRuleId = null)
        {
            var entry = new FinancialWorkflowAuditEntry
            {
                PartnerId = partnerId, CustomerId = customerId, BookingId = bookingId, CommissionId = commissionId,
                PayoutId = payoutId, RebateId = rebateId, RebateDisbursementId = rebateDisbursementId,
                CommissionRuleId = commissionRuleId,
                Action = action, PreviousCommissionStatus = oldCommission, NewCommissionStatus = newCommission,
                PreviousRebateStatus = oldRebate, NewRebateStatus = newRebate,
                PreviousAmount = previousAmount, NewAmount = newAmount, Reason = Limited(reason, "Reason", 2000),
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
