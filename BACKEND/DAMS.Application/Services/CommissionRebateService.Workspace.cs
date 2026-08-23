using DAMS.Application.Common;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace DAMS.Application.Services
{
    public sealed partial class CommissionRebateService
    {
        public async Task<BookingCommissionRebateWorkspaceDto> GetBookingWorkspaceAsync(int bookingId,
            CancellationToken cancellationToken = default)
        {
            var booking = await _context.Bookings.AsNoTracking().Include(b => b.Customer)
                .Include(b => b.Unit).ThenInclude(u => u.Project).Include(b => b.Payments)
                .SingleOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
                ?? throw new KeyNotFoundException("Booking not found.");
            var attributions = await _context.ThirdPartyAttributions.AsNoTracking().Include(a => a.Partner)
                .Where(a => a.BookingId == bookingId).OrderByDescending(a => a.IsPrimary).ThenBy(a => a.Id)
                .ToListAsync(cancellationToken);
            var commissions = await _context.BookingCommissions.AsNoTracking().Include(c => c.Partner)
                .Include(c => c.RuleRevision)
                .Include(c => c.Payouts).ThenInclude(p => p.FinanceAccount)
                .Include(c => c.Payouts).ThenInclude(p => p.Reversals)
                .Include(c => c.Payouts).ThenInclude(p => p.Evidence).Include(c => c.Evidence)
                .Where(c => c.BookingId == bookingId).OrderBy(c => c.CreatedAt).AsSplitQuery().ToListAsync(cancellationToken);
            var rebates = await _context.CustomerRebates.AsNoTracking().Include(r => r.Customer)
                .Include(r => r.Disbursements).ThenInclude(d => d.FinanceAccount)
                .Include(r => r.Disbursements).ThenInclude(d => d.Reversals)
                .Include(r => r.Disbursements).ThenInclude(d => d.Evidence).Include(r => r.Evidence)
                .Where(r => r.BookingId == bookingId).OrderBy(r => r.CreatedAt).AsSplitQuery().ToListAsync(cancellationToken);
            // Only the newest audit entries are inlined; the full log is served by the paged audit
            // endpoint so a heavily-worked booking cannot force an unbounded read into the workspace.
            // Ordered by Id (a monotonic surrogate that matches insertion/time order) so "load more"
            // can continue from the last previewed Id with a stable keyset cursor.
            var audit = await _context.FinancialWorkflowAuditEntries.AsNoTracking().Where(a => a.BookingId == bookingId)
                .OrderByDescending(a => a.Id)
                .Select(AuditProjection).Take(AuditPreviewSize + 1).ToListAsync(cancellationToken);
            var hasMoreAudit = audit.Count > AuditPreviewSize;
            if (hasMoreAudit)
                audit.RemoveAt(audit.Count - 1);
            var rebateCredits = rebates.SelectMany(r => r.Disbursements)
                .Where(d => d.Method is CustomerRebateMethod.OutstandingBalanceReduction
                    or CustomerRebateMethod.InstallmentAdjustment or CustomerRebateMethod.CreditNote)
                .Sum(d => d.Amount - d.Reversals.Sum(x => x.Amount));
            return new BookingCommissionRebateWorkspaceDto
            {
                BookingId = booking.Id, BookingReference = booking.BookingReference,
                CustomerName = booking.Customer.FullName, ProjectName = booking.Unit.Project.ProjectName,
                UnitNumber = booking.Unit.UnitNumber, BookingStatus = booking.Status,
                AgreedSalePrice = booking.AgreedSalePrice, NetSalePrice = Money(booking.AgreedSalePrice - booking.DiscountAmount),
                AmountCollected = Money(booking.Payments.Sum(p => p.Amount)), RebateCredits = Money(rebateCredits),
                Attributions = attributions.Select(MapAttribution).ToList(),
                Commissions = commissions.Select(c => MapCommission(c, booking.BookingReference)).ToList(),
                Rebates = rebates.Select(r => MapRebate(r, booking.BookingReference)).ToList(),
                Audit = audit, HasMoreAudit = hasMoreAudit
            };
        }
        private const int AuditPreviewSize = 100;
        private static readonly System.Linq.Expressions.Expression<Func<FinancialWorkflowAuditEntry, FinancialAuditDto>> AuditProjection =
            a => new FinancialAuditDto
            {
                Id = a.Id, Action = a.Action, PreviousCommissionStatus = a.PreviousCommissionStatus,
                NewCommissionStatus = a.NewCommissionStatus, PreviousRebateStatus = a.PreviousRebateStatus,
                NewRebateStatus = a.NewRebateStatus, PreviousAmount = a.PreviousAmount, NewAmount = a.NewAmount,
                Reason = a.Reason, PerformedByName = a.PerformedByName, OccurredAt = a.OccurredAt
            };
        // Keyset (cursor) pagination on the monotonic Id: the caller passes the Id of the last row it
        // has seen and receives strictly older rows. Unlike skip/take, this stays stable when new
        // audit rows are appended between page loads — offset paging would repeat or skip rows.
        public async Task<PagedResult<FinancialAuditDto>> GetBookingAuditAsync(int bookingId, int? beforeId, int take,
            CancellationToken cancellationToken = default)
        {
            if (!await _context.Bookings.AsNoTracking().AnyAsync(b => b.Id == bookingId, cancellationToken))
                throw new KeyNotFoundException("Booking not found.");
            take = Math.Clamp(take, 1, 200);
            var query = _context.FinancialWorkflowAuditEntries.AsNoTracking().Where(a => a.BookingId == bookingId);
            if (beforeId.HasValue)
                query = query.Where(a => a.Id < beforeId.Value);
            var rows = await query.OrderByDescending(a => a.Id)
                .Select(AuditProjection).Take(take + 1).ToListAsync(cancellationToken);
            return new PagedResult<FinancialAuditDto>
            {
                Items = rows.Take(take).ToList(),
                HasMore = rows.Count > take
            };
        }
        public async Task<FinancialEvidenceDto> UploadEvidenceAsync(FinancialEvidenceOwnerType ownerType, int ownerId,
            FinancialEvidenceUpload upload, FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var validated = FinanceAttachmentFileValidator.Validate(new FinanceAttachmentUpload
            {
                Content = upload.Content, FileName = upload.FileName, Length = upload.Length
            });
            var ownership = await ResolveEvidenceOwnerAsync(ownerType, ownerId, cancellationToken);
            var stored = await _storage.SaveAsync(upload.Content, validated.Extension, cancellationToken);
            try
            {
                var evidence = new FinancialEvidence
                {
                    CommissionId = ownerType == FinancialEvidenceOwnerType.Commission ? ownerId : null,
                    PayoutId = ownerType == FinancialEvidenceOwnerType.CommissionPayout ? ownerId : null,
                    RebateId = ownerType == FinancialEvidenceOwnerType.Rebate ? ownerId : null,
                    RebateDisbursementId = ownerType == FinancialEvidenceOwnerType.RebateDisbursement ? ownerId : null,
                    StoredFileName = stored, OriginalFileName = validated.OriginalFileName,
                    ContentType = validated.ContentType, FileSize = validated.FileSize,
                    UploadedByUserId = actor.UserId, UploadedByName = actor.DisplayName, UploadedAt = DateTime.UtcNow
                };
                _context.FinancialEvidence.Add(evidence);
                Audit(FinancialWorkflowAction.EvidenceUploaded, actor, ownership.PartnerId, ownership.CustomerId,
                    ownership.BookingId, ownership.CommissionId, ownership.PayoutId, ownership.RebateId,
                    ownership.DisbursementId, reason: evidence.OriginalFileName);
                await _context.SaveChangesAsync(cancellationToken);
                return MapEvidence(evidence);
            }
            catch
            {
                try
                {
                    await _storage.DeleteAsync(stored, CancellationToken.None);
                }
                catch (Exception cleanupError)
                {
                    _logger.LogError(cleanupError,
                        "Could not remove orphaned commission/rebate evidence file {StoredFileName}", stored);
                }
                throw;
            }
        }
        public async Task<FinancialEvidenceDownload> DownloadEvidenceAsync(int evidenceId, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default)
        {
            var evidence = await _context.FinancialEvidence.AsNoTracking().SingleOrDefaultAsync(e => e.Id == evidenceId, cancellationToken)
                ?? throw new KeyNotFoundException("Evidence file not found.");
            var ownerType = evidence.CommissionId.HasValue ? FinancialEvidenceOwnerType.Commission
                : evidence.PayoutId.HasValue ? FinancialEvidenceOwnerType.CommissionPayout
                : evidence.RebateId.HasValue ? FinancialEvidenceOwnerType.Rebate
                : FinancialEvidenceOwnerType.RebateDisbursement;
            var ownerId = evidence.CommissionId ?? evidence.PayoutId ?? evidence.RebateId ?? evidence.RebateDisbursementId!.Value;
            var ownership = await ResolveEvidenceOwnerAsync(ownerType, ownerId, cancellationToken);
            var stream = await _storage.OpenReadAsync(evidence.StoredFileName, cancellationToken)
                ?? throw new FileNotFoundException("The evidence metadata exists, but its private file is missing.");
            try
            {
                Audit(FinancialWorkflowAction.EvidenceDownloaded, actor, ownership.PartnerId, ownership.CustomerId,
                    ownership.BookingId, ownership.CommissionId, ownership.PayoutId, ownership.RebateId,
                    ownership.DisbursementId, reason: evidence.OriginalFileName);
                await _context.SaveChangesAsync(cancellationToken);
                return new FinancialEvidenceDownload { Content = stream, FileName = evidence.OriginalFileName, ContentType = evidence.ContentType };
            }
            catch { await stream.DisposeAsync(); throw; }
        }
        public async Task HandleBookingCancelledAsync(int bookingId, string reason, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default)
        {
            var cleanReason = Required(reason, "Cancellation reason", 2000);
            var commissions = await _context.BookingCommissions.Include(c => c.Payouts).ThenInclude(p => p.Reversals)
                .Where(c => c.BookingId == bookingId).ToListAsync(cancellationToken);
            foreach (var commission in commissions.Where(c => c.Status is not (BookingCommissionStatus.Cancelled
                         or BookingCommissionStatus.Reversed)))
            {
                var previous = commission.Status;
                commission.Status = NetPaid(commission) > 0m ? BookingCommissionStatus.ReversalRequired : BookingCommissionStatus.Cancelled;
                commission.CancellationOrReversalReason = cleanReason; commission.UpdatedAt = DateTime.UtcNow;
                Audit(commission.Status == BookingCommissionStatus.ReversalRequired
                        ? FinancialWorkflowAction.CommissionReversalRequired : FinancialWorkflowAction.CommissionCancelled,
                    actor, commission.PartnerId, bookingId: bookingId, commissionId: commission.Id,
                    oldCommission: previous, newCommission: commission.Status, reason: cleanReason);
            }
            var rebates = await _context.CustomerRebates.Include(r => r.Disbursements).ThenInclude(d => d.Reversals)
                .Where(r => r.BookingId == bookingId).ToListAsync(cancellationToken);
            foreach (var rebate in rebates.Where(r => r.Status is not (CustomerRebateStatus.Cancelled
                         or CustomerRebateStatus.Reversed)))
            {
                var previous = rebate.Status;
                rebate.Status = NetDisbursed(rebate) > 0m ? CustomerRebateStatus.ReversalRequired : CustomerRebateStatus.Cancelled;
                rebate.CancellationOrReversalReason = cleanReason; rebate.UpdatedAt = DateTime.UtcNow;
                Audit(rebate.Status == CustomerRebateStatus.ReversalRequired
                        ? FinancialWorkflowAction.RebateReversalRequired : FinancialWorkflowAction.RebateCancelled,
                    actor, customerId: rebate.CustomerId, bookingId: bookingId, rebateId: rebate.Id,
                    oldRebate: previous, newRebate: rebate.Status, reason: cleanReason);
            }
            // The booking service persists these lifecycle changes in the same transaction as cancellation.
        }
        private async Task<EvidenceOwnership> ResolveEvidenceOwnerAsync(FinancialEvidenceOwnerType type, int id,
            CancellationToken cancellationToken)
        {
            return type switch
            {
                FinancialEvidenceOwnerType.Commission => await _context.BookingCommissions.AsNoTracking()
                    .Where(c => c.Id == id).Select(c => new EvidenceOwnership(c.PartnerId, c.Booking.CustomerId,
                        c.BookingId, c.Id, null, null, null)).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new KeyNotFoundException("Commission not found."),
                FinancialEvidenceOwnerType.CommissionPayout => await _context.CommissionPayouts.AsNoTracking()
                    .Where(p => p.Id == id).Select(p => new EvidenceOwnership(p.Commission.PartnerId,
                        p.Commission.Booking.CustomerId, p.Commission.BookingId, p.CommissionId, p.Id, null, null))
                    .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("Commission payout not found."),
                FinancialEvidenceOwnerType.Rebate => await _context.CustomerRebates.AsNoTracking()
                    .Where(r => r.Id == id).Select(r => new EvidenceOwnership(null, r.CustomerId, r.BookingId,
                        null, null, r.Id, null)).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new KeyNotFoundException("Rebate not found."),
                FinancialEvidenceOwnerType.RebateDisbursement => await _context.RebateDisbursements.AsNoTracking()
                    .Where(d => d.Id == id).Select(d => new EvidenceOwnership(null, d.Rebate.CustomerId,
                        d.Rebate.BookingId, null, null, d.RebateId, d.Id)).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new KeyNotFoundException("Rebate disbursement not found."),
                _ => throw new InvalidOperationException("Evidence owner type is invalid.")
            };
        }
        private static ThirdPartyAttributionDto MapAttribution(ThirdPartyAttribution a) => new()
        {
            Id = a.Id, PartnerId = a.PartnerId, PartnerName = a.Partner.Name, LeadId = a.LeadId,
            CustomerId = a.CustomerId, BookingId = a.BookingId, RelationshipType = a.RelationshipType,
            IntroducedAt = a.IntroducedAt, SourceDetails = a.SourceDetails, Notes = a.Notes,
            IsPrimary = a.IsPrimary, AllocationPercent = a.AllocationPercent, AssignedAt = a.AssignedAt,
            ConcurrencyToken = Token(a.RowVersion)
        };
        private static BookingCommissionDto MapCommission(BookingCommission c, string? bookingReference = null)
        {
            var paid = NetPaid(c);
            return new BookingCommissionDto
            {
                Id = c.Id, BookingId = c.BookingId, BookingReference = bookingReference ?? c.Booking.BookingReference,
                PartnerId = c.PartnerId, PartnerName = c.PartnerNameSnapshot, AttributionId = c.AttributionId, RuleId = c.RuleId,
                RuleRevisionId = c.RuleRevisionId, RuleRevisionNumber = c.RuleRevision != null ? c.RuleRevision.RevisionNumber : null,
                RuleNameSnapshot = c.RuleNameSnapshot, RulePriority = c.RulePrioritySnapshot,
                IsManual = c.IsManual, ManualReason = c.ManualReason,
                AllocationPercent = c.AllocationPercentSnapshot,
                CalculationType = c.CalculationType, PercentageRate = c.PercentageRate, FixedAmount = c.FixedAmount,
                CalculationBasis = c.CalculationBasis, BasisAmount = c.BasisAmount, CalculatedAmount = c.CalculatedAmount,
                AdjustmentAmount = c.AdjustmentAmount, AdjustmentReason = c.AdjustmentReason, FinalAmount = c.FinalAmount,
                PaidAmount = paid, OutstandingAmount = Math.Max(0m, Money(c.FinalAmount - paid)),
                RecoveryRequiredAmount = c.Status == BookingCommissionStatus.ReversalRequired ? paid : 0m,
                Status = c.Status, CreatedAt = c.CreatedAt,
                CancellationOrReversalReason = c.CancellationOrReversalReason,
                ConcurrencyToken = Token(c.RowVersion),
                Payouts = c.Payouts.OrderByDescending(p => p.PaymentDate).Select(p => new MoneyMovementDto
                {
                    Id = p.Id, FinanceAccountId = p.FinanceAccountId, FinanceAccountName = p.FinanceAccount?.Name,
                    Amount = p.Amount, ReversedAmount = p.Reversals.Sum(r => r.Amount), Date = p.PaymentDate,
                    PaymentMethod = p.PaymentMethod, Reference = p.PaymentReference, Notes = p.Notes,
                    Evidence = p.Evidence.OrderByDescending(e => e.UploadedAt).Select(MapEvidence).ToList(),
                    ConcurrencyToken = Token(p.RowVersion)
                }).ToList(), Evidence = c.Evidence.OrderByDescending(e => e.UploadedAt).Select(MapEvidence).ToList()
            };
        }
        private static CustomerRebateDto MapRebate(CustomerRebate r, string? bookingReference = null)
        {
            var paid = NetDisbursed(r);
            return new CustomerRebateDto
            {
                Id = r.Id, BookingId = r.BookingId, BookingReference = bookingReference ?? r.Booking.BookingReference,
                CustomerId = r.CustomerId, CustomerName = r.Customer.FullName, CalculationType = r.CalculationType,
                PercentageRate = r.PercentageRate, FixedAmount = r.FixedAmount, CalculationBasis = r.CalculationBasis,
                BasisAmount = r.BasisAmount, CalculatedAmount = r.CalculatedAmount, AdjustmentAmount = r.AdjustmentAmount,
                AdjustmentReason = r.AdjustmentReason, FinalAmount = r.FinalAmount,
                AppliedOrPaidAmount = paid, OutstandingAmount = Math.Max(0m, Money(r.FinalAmount - paid)),
                RecoveryRequiredAmount = r.Status == CustomerRebateStatus.ReversalRequired ? paid : 0m,
                Reason = r.Reason, Method = r.Method, Status = r.Status, Notes = r.Notes, CreatedAt = r.CreatedAt,
                CancellationOrReversalReason = r.CancellationOrReversalReason,
                ConcurrencyToken = Token(r.RowVersion), Disbursements = r.Disbursements.OrderByDescending(d => d.AppliedAt)
                    .Select(d => new MoneyMovementDto
                    {
                        Id = d.Id, FinanceAccountId = d.FinanceAccountId, FinanceAccountName = d.FinanceAccount?.Name,
                        InstallmentId = d.InstallmentId, RebateMethod = d.Method, Amount = d.Amount,
                        ReversedAmount = d.Reversals.Sum(x => x.Amount), Date = d.AppliedAt,
                        PaymentMethod = d.PaymentMethod, Reference = d.Reference, Notes = d.Notes,
                        Evidence = d.Evidence.OrderByDescending(e => e.UploadedAt).Select(MapEvidence).ToList(),
                        ConcurrencyToken = Token(d.RowVersion)
                    }).ToList(), Evidence = r.Evidence.OrderByDescending(e => e.UploadedAt).Select(MapEvidence).ToList()
            };
        }
        private static FinancialEvidenceDto MapEvidence(FinancialEvidence e) => new()
        {
            Id = e.Id, OriginalFileName = e.OriginalFileName, ContentType = e.ContentType,
            FileSize = e.FileSize, UploadedByName = e.UploadedByName, UploadedAt = e.UploadedAt
        };
        private sealed record EvidenceOwnership(int? PartnerId, int? CustomerId, int BookingId,
            int? CommissionId, int? PayoutId, int? RebateId, int? DisbursementId);
    }
}
