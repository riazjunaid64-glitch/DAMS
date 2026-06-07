using DAMS.Application.DTOs.InstallmentDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class InstallmentService : IInstallmentService
    {
        private readonly AppDbContext _context;

        public InstallmentService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<InstallmentScheduleDto> GetScheduleAsync(int bookingId)
        {
            var booking = await _context.Bookings
                .AsNoTracking()
                .Include(b => b.Installments)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            var canRegenerate = await CanRegenerateAsync(bookingId);
            var paidByInstallment = await GetPaidByInstallmentAsync(bookingId);
            return MapSchedule(booking, canRegenerate, paidByInstallment);
        }

        public async Task<InstallmentScheduleDto> RecordInstallmentPaymentAsync(
            int bookingId, int installmentId, RecordInstallmentPaymentDto dto, int adminUserId)
        {
            if (dto.Amount <= 0m)
                throw new InvalidOperationException("Payment amount must be greater than zero.");

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            if (booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Cannot record a payment against a cancelled booking.");

            if (booking.Status != BookingStatus.PaymentPlanActive)
                throw new InvalidOperationException("Installment payments can only be recorded while the payment plan is active.");

            var installment = await _context.Installments
                .FirstOrDefaultAsync(i => i.Id == installmentId && i.BookingId == bookingId);

            if (installment == null)
                throw new InvalidOperationException("Installment not found for this booking.");

            if (installment.Status == InstallmentStatus.Paid)
                throw new InvalidOperationException("This installment is already fully paid.");

            var alreadyPaid = await _context.Payments
                .Where(p => p.InstallmentId == installmentId && p.Type == PaymentType.Installment)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            var remaining = installment.Amount - alreadyPaid;
            if (dto.Amount > remaining)
                throw new InvalidOperationException(
                    $"Payment exceeds the remaining installment balance. Remaining is {remaining:0.00}.");

            var payment = new Payment
            {
                BookingId = booking.Id,
                InstallmentId = installment.Id,
                Type = PaymentType.Installment,
                Amount = dto.Amount,
                PaymentMethod = dto.PaymentMethod,
                PaymentReference = string.IsNullOrWhiteSpace(dto.PaymentReference) ? null : dto.PaymentReference.Trim(),
                Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
                ReceiptNumber = await GenerateReceiptNumberAsync(),
                RecordedByUserId = adminUserId,
                PaidAt = dto.PaidAt ?? DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _context.Payments.Add(payment);

            var newPaid = alreadyPaid + dto.Amount;
            if (newPaid >= installment.Amount)
            {
                installment.Status = InstallmentStatus.Paid;
                installment.PaidAt = payment.PaidAt;
            }
            else
            {
                installment.Status = InstallmentStatus.PartiallyPaid;
                installment.PaidAt = null;
            }

            booking.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return await GetScheduleAsync(bookingId);
        }

        private async Task<Dictionary<int, decimal>> GetPaidByInstallmentAsync(int bookingId)
        {
            return await _context.Payments
                .AsNoTracking()
                .Where(p => p.BookingId == bookingId
                            && p.InstallmentId != null
                            && p.Type == PaymentType.Installment)
                .GroupBy(p => p.InstallmentId!.Value)
                .Select(g => new { InstallmentId = g.Key, Paid = g.Sum(p => p.Amount) })
                .ToDictionaryAsync(x => x.InstallmentId, x => x.Paid);
        }

        // Globally unique sequential receipt number, e.g. RCP-000001.
        private async Task<string> GenerateReceiptNumberAsync()
        {
            // Fetch only the highest existing receipt number from the database instead of
            // materialising every payment row. Numbers are zero-padded ("RCP-000001"), so the
            // longest value wins, and among equal lengths the lexicographically greatest is the
            // numeric maximum.
            var latest = await _context.Payments
                .Where(p => p.ReceiptNumber != null && p.ReceiptNumber.StartsWith("RCP-"))
                .Select(p => p.ReceiptNumber!)
                .OrderByDescending(r => r.Length)
                .ThenByDescending(r => r)
                .FirstOrDefaultAsync();

            var max = 0;
            if (latest != null && int.TryParse(latest.Substring(4), out var n))
                max = n;

            return $"RCP-{(max + 1):D6}";
        }

        public async Task<InstallmentScheduleDto> GenerateScheduleAsync(int bookingId, GenerateInstallmentPlanDto dto, int adminUserId)
        {
            ValidatePlanInput(dto);

            var booking = await _context.Bookings
                .Include(b => b.Installments)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            if (booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Cannot generate installments for a cancelled booking.");

            if (booking.Status != BookingStatus.PaymentPlanActive)
                throw new InvalidOperationException("Installment schedule can only be generated when the booking is on an active payment plan.");

            if (booking.BookingAmountReceived < booking.BookingAmountRequired)
                throw new InvalidOperationException("Booking amount must be fully received before generating installments.");

            var hasExisting = booking.Installments.Count > 0;
            if (hasExisting)
            {
                if (!dto.Regenerate)
                    throw new InvalidOperationException("An installment schedule already exists. Set regenerate to true to replace it.");

                if (!await CanRegenerateAsync(bookingId))
                    throw new InvalidOperationException(
                        "Schedule cannot be regenerated because installment payments exist or installments are no longer all pending.");
            }

            var possessionAmount = dto.PossessionAmount;
            if (possessionAmount > 0m && !dto.PossessionDueDate.HasValue)
                throw new InvalidOperationException("Possession due date is required when a possession amount is set.");

            var installmentPool = dto.AgreedSalePrice - booking.BookingAmountReceived - possessionAmount;
            if (installmentPool <= 0m)
                throw new InvalidOperationException("Installment pool must be greater than zero after booking amount and possession amount.");

            if (hasExisting)
            {
                _context.Installments.RemoveRange(booking.Installments);
                booking.Installments.Clear();
            }

            booking.AgreedSalePrice = dto.AgreedSalePrice;
            booking.DiscountAmount = dto.DiscountAmount;
            booking.DiscountReason = string.IsNullOrWhiteSpace(dto.DiscountReason) ? null : dto.DiscountReason.Trim();
            booking.InstallmentFrequency = dto.Frequency;
            booking.NumberOfInstallments = dto.NumberOfInstallments;
            booking.InstallmentPlanStartDate = dto.InstallmentStartDate.Date;
            booking.PossessionAmount = possessionAmount;
            booking.PossessionDueDate = possessionAmount > 0m ? dto.PossessionDueDate?.Date : null;
            booking.InstallmentPlanGeneratedAt = DateTime.UtcNow;
            booking.InstallmentPlanGeneratedByUserId = adminUserId;
            booking.UpdatedAt = DateTime.UtcNow;

            var installments = BuildInstallmentRows(booking.Id, dto, installmentPool);
            _context.Installments.AddRange(installments);

            booking.TotalInstallmentAmount = installments.Sum(i => i.Amount);

            await _context.SaveChangesAsync();

            // Reload for mapping with ids assigned.
            await _context.Entry(booking).Collection(b => b.Installments).LoadAsync();

            // A freshly generated schedule has no payments yet.
            return MapSchedule(booking, canRegenerate: true, new Dictionary<int, decimal>());
        }

        private static void ValidatePlanInput(GenerateInstallmentPlanDto dto)
        {
            if (dto.AgreedSalePrice <= 0m)
                throw new InvalidOperationException("Agreed sale price must be greater than zero.");

            if (dto.NumberOfInstallments < 1)
                throw new InvalidOperationException("Number of installments must be at least 1.");

            if (dto.DiscountAmount < 0m)
                throw new InvalidOperationException("Discount amount cannot be negative.");

            if (dto.PossessionAmount < 0m)
                throw new InvalidOperationException("Possession amount cannot be negative.");
        }

        private static List<Installment> BuildInstallmentRows(int bookingId, GenerateInstallmentPlanDto dto, decimal installmentPool)
        {
            var rows = new List<Installment>();

            if (dto.PossessionAmount > 0m)
            {
                rows.Add(new Installment
                {
                    BookingId = bookingId,
                    SequenceNumber = 0,
                    Type = InstallmentType.Possession,
                    DueDate = dto.PossessionDueDate!.Value.Date,
                    Amount = dto.PossessionAmount,
                    Status = InstallmentStatus.Pending,
                    Notes = "Possession payment"
                });
            }

            var perInstallment = Math.Round(installmentPool / dto.NumberOfInstallments, 2, MidpointRounding.AwayFromZero);
            var allocated = 0m;

            for (var i = 1; i <= dto.NumberOfInstallments; i++)
            {
                var amount = i == dto.NumberOfInstallments
                    ? installmentPool - allocated
                    : perInstallment;

                allocated += amount;

                rows.Add(new Installment
                {
                    BookingId = bookingId,
                    SequenceNumber = i,
                    Type = InstallmentType.Regular,
                    DueDate = CalculateDueDate(dto.InstallmentStartDate, dto.Frequency, i),
                    Amount = amount,
                    Status = InstallmentStatus.Pending
                });
            }

            return rows;
        }

        private static DateTime CalculateDueDate(DateTime startDate, InstallmentFrequency frequency, int sequenceNumber)
        {
            var months = frequency switch
            {
                InstallmentFrequency.Monthly => sequenceNumber,
                InstallmentFrequency.Quarterly => sequenceNumber * 3,
                InstallmentFrequency.HalfYearly => sequenceNumber * 6,
                InstallmentFrequency.Yearly => sequenceNumber * 12,
                _ => sequenceNumber
            };

            return startDate.Date.AddMonths(months);
        }

        private async Task<bool> CanRegenerateAsync(int bookingId)
        {
            var installments = await _context.Installments
                .AsNoTracking()
                .Where(i => i.BookingId == bookingId)
                .ToListAsync();

            if (installments.Count == 0)
                return false;

            if (installments.Any(i => i.Status != InstallmentStatus.Pending))
                return false;

            var hasInstallmentPayments = await _context.Payments
                .AnyAsync(p => p.BookingId == bookingId
                               && p.InstallmentId != null
                               && p.Type == PaymentType.Installment);

            return !hasInstallmentPayments;
        }

        private InstallmentScheduleDto MapSchedule(Booking booking, bool canRegenerate, Dictionary<int, decimal> paidByInstallment)
        {
            var hasSchedule = booking.Installments.Count > 0;
            var canGenerate = booking.Status == BookingStatus.PaymentPlanActive
                              && booking.BookingAmountReceived >= booking.BookingAmountRequired
                              && (!hasSchedule || canRegenerate);

            var installmentPool = booking.AgreedSalePrice - booking.BookingAmountReceived - booking.PossessionAmount;
            var today = DateTime.UtcNow.Date;

            var items = booking.Installments
                .OrderBy(i => i.Type == InstallmentType.Possession ? 0 : 1)
                .ThenBy(i => i.SequenceNumber)
                .Select(i =>
                {
                    var paid = paidByInstallment.TryGetValue(i.Id, out var p) ? p : 0m;
                    var remaining = i.Amount - paid;
                    var isPaid = i.Status == InstallmentStatus.Paid;
                    var isOverdue = !isPaid && i.DueDate.Date < today;

                    // Effective status is derived so partial-payment info is never lost
                    // and overdue does not need a background job.
                    var effectiveStatus = isPaid
                        ? InstallmentStatus.Paid
                        : isOverdue
                            ? InstallmentStatus.Overdue
                            : paid > 0m
                                ? InstallmentStatus.PartiallyPaid
                                : InstallmentStatus.Pending;

                    return new InstallmentScheduleItemDto
                    {
                        Id = i.Id,
                        SequenceNumber = i.SequenceNumber,
                        Type = i.Type,
                        DueDate = i.DueDate,
                        Amount = i.Amount,
                        Status = effectiveStatus,
                        AmountPaid = paid,
                        RemainingBalance = remaining,
                        IsOverdue = isOverdue,
                        PaidAt = i.PaidAt,
                        Notes = i.Notes
                    };
                })
                .ToList();

            return new InstallmentScheduleDto
            {
                BookingId = booking.Id,
                BookingReference = booking.BookingReference,
                BookingStatus = booking.Status,
                AgreedSalePrice = booking.AgreedSalePrice,
                DiscountAmount = booking.DiscountAmount,
                BookingAmountReceived = booking.BookingAmountReceived,
                PossessionAmount = booking.PossessionAmount,
                InstallmentPool = Math.Max(0m, installmentPool),
                Frequency = booking.InstallmentFrequency,
                NumberOfInstallments = booking.NumberOfInstallments,
                InstallmentStartDate = booking.InstallmentPlanStartDate,
                PossessionDueDate = booking.PossessionDueDate,
                GeneratedAt = booking.InstallmentPlanGeneratedAt,
                HasSchedule = hasSchedule,
                CanGenerate = canGenerate,
                CanRegenerate = canRegenerate,
                ScheduleTotal = items.Sum(i => i.Amount),
                SchedulePaid = items.Sum(i => i.AmountPaid),
                ScheduleRemaining = items.Sum(i => i.RemainingBalance),
                Items = items
            };
        }
    }
}
