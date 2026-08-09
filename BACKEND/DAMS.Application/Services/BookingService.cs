using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace DAMS.Application.Services
{
    public class BookingService : IBookingService
    {
        private readonly AppDbContext _context;
        private readonly ICustomerService _customerService;
        private readonly INotificationEventService? _notifications;
        private readonly ICommissionBookingLifecycle? _commissionLifecycle;

        /// <param name="notifications">
        /// Optional on purpose: booking and payment work must be able to run without the
        /// notification platform present. Every call into it happens after the money is
        /// committed and is wrapped so a notification problem cannot undo a payment.
        /// </param>
        public BookingService(AppDbContext context, ICustomerService customerService,
            INotificationEventService? notifications = null, ICommissionBookingLifecycle? commissionLifecycle = null)
        {
            _context = context;
            _customerService = customerService;
            _notifications = notifications;
            _commissionLifecycle = commissionLifecycle;
        }

        /// <summary>
        /// Raises a business notification after the fact. Deliberately swallowing: the
        /// notification platform has its own reconciliation sweep for anything missed here,
        /// and nothing it does may turn a successful payment into a failed request.
        /// </summary>
        private async Task NotifyQuietlyAsync(Func<INotificationEventService, Task> action)
        {
            if (_notifications == null)
                return;

            try
            {
                await action(_notifications);
            }
            catch (Exception)
            {
                // Intentionally ignored — see the reconciliation sweep.
            }
        }

        public async Task<BookingResponseDto> CreateBookingAsync(CreateBookingDto dto, int adminUserId)
        {
            var unit = await _context.Units
                .Include(u => u.Project)
                .FirstOrDefaultAsync(u => u.Id == dto.UnitId);

            if (unit == null)
                throw new InvalidOperationException("Unit not found.");

            if (unit.Status != UnitStatus.Available)
                throw new InvalidOperationException("This unit is not available for booking.");

            await EnsureNoActiveBookingAsync(unit.Id);

            int customerId;
            if (dto.CustomerId.HasValue)
            {
                var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == dto.CustomerId.Value);
                if (customer == null)
                    throw new InvalidOperationException("Customer not found.");
                if (customer.Status == CustomerStatus.Blocked)
                    throw new InvalidOperationException("This customer is blocked and cannot be booked.");
                customerId = customer.Id;
            }
            else if (dto.NewCustomer != null)
            {
                var resolution = await _customerService.FindOrCreateCustomerAsync(
                    dto.NewCustomer.FullName,
                    dto.NewCustomer.Phone,
                    dto.NewCustomer.CNIC,
                    dto.NewCustomer.Email,
                    dto.NewCustomer.Address,
                    dto.Source,
                    dto.NewCustomer.SourceNotes,
                    adminUserId,
                    dto.NewCustomer.FatherName,
                    dto.NewCustomer.DateOfBirth,
                    dto.NewCustomer.Nationality,
                    dto.NewCustomer.Occupation,
                    dto.NewCustomer.Whatsapp);
                customerId = resolution.CustomerId;
            }
            else
            {
                throw new InvalidOperationException("Provide an existing customer id or new customer details.");
            }

            var agreedSalePrice = dto.AgreedSalePrice ?? unit.Price;
            if (agreedSalePrice <= 0m)
                throw new InvalidOperationException("Agreed sale price must be greater than zero.");

            var discountPercent = dto.DiscountPercent ?? 0m;
            if (discountPercent < 0m || discountPercent > 100m)
                throw new InvalidOperationException("Discount percent must be between 0 and 100.");
            var discount = Math.Round(agreedSalePrice * discountPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var netSalePrice = agreedSalePrice - discount;

            var bookingAmountRequired = dto.BookingAmountRequired ?? 0m;
            if (bookingAmountRequired < 0m)
                throw new InvalidOperationException("Booking amount required cannot be negative.");
            if (bookingAmountRequired > netSalePrice)
                throw new InvalidOperationException("Booking amount required cannot exceed the discounted sale price.");

            var applicationAmountReceived = dto.ApplicationAmountReceived ?? 0m;
            if (applicationAmountReceived < 0m)
                throw new InvalidOperationException("Application amount received cannot be negative.");
            if (applicationAmountReceived > 0m && bookingAmountRequired <= 0m)
                throw new InvalidOperationException("Set a booking amount required before recording an amount received with the application.");
            if (applicationAmountReceived > bookingAmountRequired)
                throw new InvalidOperationException("Application amount received cannot exceed the booking amount required.");

            var booking = new Booking
            {
                CustomerId = customerId,
                UnitId = unit.Id,
                BookingRequestId = null,
                Source = dto.Source,
                AssignedSalesUserId = dto.AssignedSalesUserId,
                Status = BookingStatus.AwaitingBookingAmount,
                ListPrice = unit.Price,
                AgreedSalePrice = agreedSalePrice,
                DiscountAmount = discount,
                DiscountReason = string.IsNullOrWhiteSpace(dto.DiscountReason) ? null : dto.DiscountReason.Trim(),
                BookingAmountRequired = bookingAmountRequired,
                BookingAmountReceived = 0m,
                TotalInstallmentAmount = netSalePrice - bookingAmountRequired,
                BookingDate = DateTime.UtcNow,
                BookingAmountDueDate = dto.BookingAmountDueDate,
                CustomerNotes = string.IsNullOrWhiteSpace(dto.CustomerNotes) ? null : dto.CustomerNotes.Trim(),
                InternalNotes = string.IsNullOrWhiteSpace(dto.InternalNotes) ? null : dto.InternalNotes.Trim(),
                // Application Form snapshot
                SerialNo = string.IsNullOrWhiteSpace(dto.SerialNo) ? null : dto.SerialNo.Trim(),
                ApartmentCategory = string.IsNullOrWhiteSpace(dto.ApartmentCategory) ? null : dto.ApartmentCategory.Trim(),
                Tower = string.IsNullOrWhiteSpace(dto.Tower) ? null : dto.Tower.Trim(),
                IsCorner = dto.IsCorner,
                PricePerSft = dto.PricePerSft,
                DiscountPercent = discountPercent,
                ReferenceId = string.IsNullOrWhiteSpace(dto.ReferenceId) ? null : dto.ReferenceId.Trim(),
                PaymentThrough = string.IsNullOrWhiteSpace(dto.PaymentThrough) ? null : dto.PaymentThrough.Trim(),
                ApplicationPaymentType = string.IsNullOrWhiteSpace(dto.ApplicationPaymentType) ? null : dto.ApplicationPaymentType.Trim(),
                ApplicationAmountReceived = dto.ApplicationAmountReceived,
                ApplicationDate = dto.ApplicationDate,
                NextOfKinName = string.IsNullOrWhiteSpace(dto.NextOfKinName) ? null : dto.NextOfKinName.Trim(),
                NextOfKinRelation = string.IsNullOrWhiteSpace(dto.NextOfKinRelation) ? null : dto.NextOfKinRelation.Trim(),
                NextOfKinContact = string.IsNullOrWhiteSpace(dto.NextOfKinContact) ? null : dto.NextOfKinContact.Trim(),
                NextOfKinCnic = string.IsNullOrWhiteSpace(dto.NextOfKinCnic) ? null : dto.NextOfKinCnic.Trim(),
                NextOfKinDob = dto.NextOfKinDob,
                NextOfKinAddress = string.IsNullOrWhiteSpace(dto.NextOfKinAddress) ? null : dto.NextOfKinAddress.Trim(),
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

            // Set inside the transaction, notified after it: the receipt notification must
            // never be part of what makes the booking succeed or fail.
            int? applicationPaymentId = null;

            await RunInTransactionAsync(async () =>
            {
                await PersistNewBookingAsync(booking, unit);

                // Money collected with the application form is a real booking-amount payment —
                // otherwise it never reaches Finance revenue, receipts, or booking progress.
                if (applicationAmountReceived > 0m)
                {
                    var payment = new Payment
                    {
                        BookingId = booking.Id,
                        InstallmentId = null,
                        Type = PaymentType.BookingAmount,
                        Amount = applicationAmountReceived,
                        PaymentMethod = ParseApplicationPaymentMethod(dto.ApplicationPaymentType),
                        PaymentReference = string.IsNullOrWhiteSpace(dto.PaymentThrough) ? null : dto.PaymentThrough.Trim(),
                        Notes = "Received with the application form.",
                        RecordedByUserId = adminUserId,
                        PaidAt = dto.ApplicationDate ?? DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Payments.Add(payment);

                    booking.BookingAmountReceived = applicationAmountReceived;
                    if (booking.BookingAmountReceived >= booking.BookingAmountRequired)
                    {
                        booking.Status = BookingStatus.PaymentPlanActive;
                        booking.BookingAmountConfirmedDate = DateTime.UtcNow;
                        booking.InstallmentPlanStartDate ??= DateTime.UtcNow;
                        unit.Status = UnitStatus.OnPaymentPlan;
                        unit.UpdatedAt = DateTime.UtcNow;
                    }

                    await SaveWithUniqueReceiptNumberAsync(payment);
                    applicationPaymentId = payment.Id;
                }
            });

            if (applicationPaymentId.HasValue)
                await NotifyQuietlyAsync(n => n.NotifyPaymentRecordedAsync(applicationPaymentId.Value));

            return await GetResponseAsync(booking.Id);
        }

        /// <summary>
        /// Runs booking persistence atomically. When a caller (lead conversion) has already
        /// opened a transaction on this DbContext, the work joins that one instead — EF
        /// rejects nested transactions, and the outer caller must be able to roll the
        /// booking back with the rest of its own changes.
        /// </summary>
        private async Task RunInTransactionAsync(Func<Task> action)
        {
            if (_context.Database.CurrentTransaction != null)
            {
                await action();
                return;
            }

            // Wrapped in an execution strategy because the DbContext has retry-on-failure
            // enabled, which is incompatible with a bare BeginTransactionAsync.
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await action();
                await transaction.CommitAsync();
            });
        }

        public async Task<BookingResponseDto?> GetBookingByIdAsync(int id)
        {
            var exists = await _context.Bookings.AnyAsync(b => b.Id == id);
            if (!exists) return null;
            return await GetResponseAsync(id);
        }

        public async Task<BookingListDto> GetBookingsAsync(BookingFilterDto filter)
        {
            var query = _context.Bookings
                .AsNoTracking()
                .Include(b => b.Customer)
                .Include(b => b.Unit).ThenInclude(u => u.Project)
                .AsQueryable();

            if (filter.Status.HasValue)
                query = query.Where(b => b.Status == filter.Status.Value);

            if (filter.ProjectId.HasValue)
                query = query.Where(b => b.Unit.ProjectId == filter.ProjectId.Value);

            if (filter.CustomerId.HasValue)
                query = query.Where(b => b.CustomerId == filter.CustomerId.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(b =>
                    b.BookingReference.ToLower().Contains(term) ||
                    b.Customer.FullName.ToLower().Contains(term) ||
                    b.Customer.Phone.Contains(term) ||
                    b.Unit.UnitNumber.ToLower().Contains(term) ||
                    b.Unit.Project.ProjectName.ToLower().Contains(term));
            }

            var totalCount = await query.CountAsync();

            var page = filter.Page < 1 ? 1 : filter.Page;
            var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;

            var entities = await query
                .OrderByDescending(b => b.BookingDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = entities.Select(MapProjection).ToList();

            return new BookingListDto
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<BookingResponseDto> CancelBookingAsync(int id, string? reason, int adminUserId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new InvalidOperationException("A booking cancellation reason is required.");

            var booking = await _context.Bookings
                .Include(b => b.Unit)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            if (booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Booking is already cancelled.");

            if (booking.Status is BookingStatus.PossessionGiven or BookingStatus.SaleCompleted)
                throw new InvalidOperationException("A booking that reached possession or completion cannot be cancelled here.");

            booking.Status = BookingStatus.Cancelled;
            booking.InternalNotes = AppendNote(booking.InternalNotes,
                $"Cancelled by user {adminUserId}" +
                (string.IsNullOrWhiteSpace(reason) ? "." : $": {reason.Trim()}"));
            booking.UpdatedAt = DateTime.UtcNow;

            // Release the unit back to the market.
            booking.Unit.Status = UnitStatus.Available;
            booking.Unit.UpdatedAt = DateTime.UtcNow;

            if (_commissionLifecycle != null)
                await _commissionLifecycle.HandleBookingCancelledAsync(booking.Id, reason.Trim(),
                    new FinancialWorkflowActor(adminUserId, $"Admin #{adminUserId}"));

            await _context.SaveChangesAsync();

            await NotifyQuietlyAsync(n => n.NotifyBookingStatusAsync(
                booking.Id, NotificationType.BookingCancelled, reason, adminUserId));

            return await GetResponseAsync(booking.Id);
        }

        public async Task<BookingResponseDto> UpdateBookingFinancialsAsync(int id, UpdateBookingFinancialsDto dto, int adminUserId)
        {
            var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            if (booking.Status != BookingStatus.AwaitingBookingAmount)
                throw new InvalidOperationException("Financial terms can only be edited while the booking is awaiting the booking amount.");

            if (dto.AgreedSalePrice <= 0m)
                throw new InvalidOperationException("Agreed sale price must be greater than zero.");

            if (dto.BookingAmountRequired <= 0m)
                throw new InvalidOperationException("Booking amount required must be greater than zero.");

            if (dto.DiscountPercent < 0m || dto.DiscountPercent > 100m)
                throw new InvalidOperationException("Discount percent must be between 0 and 100.");

            var discountAmount = Math.Round(dto.AgreedSalePrice * dto.DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var netSalePrice = dto.AgreedSalePrice - discountAmount;

            if (dto.BookingAmountRequired > netSalePrice)
                throw new InvalidOperationException("Booking amount required cannot exceed the discounted sale price.");

            // Cannot drop the required amount below what has already been received.
            if (dto.BookingAmountRequired < booking.BookingAmountReceived)
                throw new InvalidOperationException(
                    $"Booking amount required cannot be less than the amount already received ({booking.BookingAmountReceived:0.00}).");

            booking.AgreedSalePrice = dto.AgreedSalePrice;
            booking.DiscountPercent = dto.DiscountPercent;
            booking.DiscountAmount = discountAmount;
            booking.DiscountReason = string.IsNullOrWhiteSpace(dto.DiscountReason) ? null : dto.DiscountReason.Trim();
            booking.BookingAmountRequired = dto.BookingAmountRequired;
            booking.BookingAmountDueDate = dto.BookingAmountDueDate;
            booking.TotalInstallmentAmount = netSalePrice - dto.BookingAmountRequired;
            booking.UpdatedAt = DateTime.UtcNow;

            // If terms now mean the booking amount is already covered, advance the workflow.
            if (booking.BookingAmountReceived >= booking.BookingAmountRequired)
            {
                var unit = await _context.Units.FirstOrDefaultAsync(u => u.Id == booking.UnitId);
                booking.Status = BookingStatus.PaymentPlanActive;
                booking.BookingAmountConfirmedDate ??= DateTime.UtcNow;
                booking.InstallmentPlanStartDate ??= DateTime.UtcNow;
                if (unit != null)
                {
                    unit.Status = UnitStatus.OnPaymentPlan;
                    unit.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            return await GetResponseAsync(booking.Id);
        }

        public async Task<BookingResponseDto> RecordBookingAmountPaymentAsync(int bookingId, RecordBookingAmountPaymentDto dto, int adminUserId)
        {
            if (dto.Amount <= 0m)
                throw new InvalidOperationException("Payment amount must be greater than zero.");

            var booking = await _context.Bookings
                .Include(b => b.Unit)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            if (booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Cannot record a payment against a cancelled booking.");

            if (booking.Status != BookingStatus.AwaitingBookingAmount)
                throw new InvalidOperationException("Booking amount has already been fully received for this booking.");

            if (booking.BookingAmountRequired <= 0m)
                throw new InvalidOperationException("Set a booking amount required on this booking before recording payments.");

            var bookingCredits = await BookingCreditPolicy.GetNonCashCreditsAsync(_context, booking.Id);
            var netSalePrice = booking.AgreedSalePrice - booking.DiscountAmount;
            // Booking-level credits reduce the total owed, so the cash still required for the
            // booking-amount milestone can never exceed the sale's remaining balance. Without this
            // cap a credit larger than (net price - booking amount) would demand more cash than the
            // sale allows, permanently deadlocking the booking in AwaitingBookingAmount.
            var effectiveRequired = BookingCreditPolicy.EffectiveBookingAmountRequired(booking, bookingCredits);
            var totalCollected = await _context.Payments.Where(p => p.BookingId == booking.Id)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var overallRemaining = Math.Max(0m, netSalePrice - totalCollected - bookingCredits);
            var remaining = Math.Max(0m, Math.Min(effectiveRequired - booking.BookingAmountReceived, overallRemaining));
            if (dto.Amount > remaining)
                throw new InvalidOperationException(
                    $"Payment exceeds the remaining booking amount. Remaining is {remaining:0.00}.");

            var payment = new Payment
            {
                BookingId = booking.Id,
                InstallmentId = null,
                Type = PaymentType.BookingAmount,
                Amount = dto.Amount,
                PaymentMethod = dto.PaymentMethod,
                PaymentReference = string.IsNullOrWhiteSpace(dto.PaymentReference) ? null : dto.PaymentReference.Trim(),
                Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
                RecordedByUserId = adminUserId,
                PaidAt = dto.PaidAt ?? DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _context.Payments.Add(payment);

            booking.BookingAmountReceived += dto.Amount;
            booking.UpdatedAt = DateTime.UtcNow;

            // Fully received (in cash, net of any booking-level credits) -> activate the payment
            // plan stage and move the unit accordingly.
            if (booking.BookingAmountReceived >= effectiveRequired)
            {
                booking.Status = BookingStatus.PaymentPlanActive;
                booking.BookingAmountConfirmedDate = DateTime.UtcNow;
                booking.InstallmentPlanStartDate ??= DateTime.UtcNow;

                booking.Unit.Status = UnitStatus.OnPaymentPlan;
                booking.Unit.UpdatedAt = DateTime.UtcNow;
            }

            try
            {
                await SaveWithUniqueReceiptNumberAsync(payment);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "This booking was updated by another payment. No payment was recorded; reload and try again.");
            }

            // The money is committed. Everything below is best-effort.
            await NotifyQuietlyAsync(n => n.NotifyPaymentRecordedAsync(payment.Id));

            return await GetResponseAsync(booking.Id);
        }

        public async Task<BookingResponseDto> GivePossessionAsync(int id, DateTime? possessionDate, int adminUserId)
        {
            var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            if (booking.Status != BookingStatus.PaymentPlanActive)
                throw new InvalidOperationException("Possession can only be given while the payment plan is active.");

            booking.Status = BookingStatus.PossessionGiven;
            booking.PossessionDate = possessionDate ?? DateTime.UtcNow;
            booking.InternalNotes = AppendNote(booking.InternalNotes,
                $"Possession given by user {adminUserId}.");
            booking.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await NotifyQuietlyAsync(n => n.NotifyBookingStatusAsync(
                booking.Id, NotificationType.PossessionGiven, null, adminUserId));

            return await GetResponseAsync(booking.Id);
        }

        public async Task<BookingResponseDto> CompleteSaleAsync(int id, int adminUserId)
        {
            var booking = await _context.Bookings
                .Include(b => b.Unit)
                .Include(b => b.Installments)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            if (booking.Status is not (BookingStatus.PaymentPlanActive or BookingStatus.PossessionGiven))
                throw new InvalidOperationException("Only an active or possession-given booking can be completed.");

            var rebateCredits = await BookingCreditPolicy.GetNonCashCreditsAsync(_context, booking.Id);
            var effectiveBookingAmountRequired = BookingCreditPolicy.EffectiveBookingAmountRequired(booking, rebateCredits);
            var totalOutstanding = booking.AgreedSalePrice - booking.DiscountAmount
                - booking.Payments.Sum(p => p.Amount) - rebateCredits;
            if (totalOutstanding > 0m)
            {
                if (booking.BookingAmountReceived < effectiveBookingAmountRequired)
                    throw new InvalidOperationException("Booking amount has not been fully received or credited yet.");
                var unpaidInstallments = booking.Installments.Count(i => i.Status != InstallmentStatus.Paid);
                throw new InvalidOperationException($"The sale still has {totalOutstanding:0.00} outstanding across {unpaidInstallments} installment(s).");
            }

            // A schedule must be settled per-installment before completion. Booking-level credits
            // (balance reduction / credit note) lower the overall balance but are not allocated to any
            // installment and never mark one Paid, so without this guard they can drive totalOutstanding
            // to zero while installments stay unpaid — leaving the booking completed and overdue at once.
            if (booking.Installments.Count != 0 && booking.Installments.Any(i => i.Status != InstallmentStatus.Paid))
                throw new InvalidOperationException(
                    "The sale cannot be completed while installments remain unpaid. Apply each remaining credit as an installment adjustment, or collect the installment, before completing.");

            booking.Status = BookingStatus.SaleCompleted;
            booking.CompletionDate = DateTime.UtcNow;
            booking.InternalNotes = AppendNote(booking.InternalNotes,
                $"Sale completed by user {adminUserId}.");
            booking.UpdatedAt = DateTime.UtcNow;

            booking.Unit.Status = UnitStatus.Sold;
            booking.Unit.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await NotifyQuietlyAsync(n => n.NotifyBookingStatusAsync(
                booking.Id, NotificationType.SaleCompleted, null, adminUserId));

            return await GetResponseAsync(booking.Id);
        }

        public async Task<List<BookingPaymentDto>> GetBookingPaymentsAsync(int bookingId)
        {
            var exists = await _context.Bookings.AnyAsync(b => b.Id == bookingId);
            if (!exists)
                throw new InvalidOperationException("Booking not found.");

            return await _context.Payments
                .AsNoTracking()
                .Where(p => p.BookingId == bookingId)
                .OrderByDescending(p => p.PaidAt)
                .Select(p => new BookingPaymentDto
                {
                    Id = p.Id,
                    BookingId = p.BookingId,
                    InstallmentId = p.InstallmentId,
                    Type = p.Type,
                    Amount = p.Amount,
                    PaymentMethod = p.PaymentMethod,
                    PaymentReference = p.PaymentReference,
                    ReceiptNumber = p.ReceiptNumber,
                    Notes = p.Notes,
                    PaidAt = p.PaidAt
                })
                .ToListAsync();
        }

        public async Task<PaymentReceiptDto> GetPaymentReceiptAsync(int bookingId, int paymentId)
        {
            var payment = await _context.Payments
                .AsNoTracking()
                .Include(p => p.Booking).ThenInclude(b => b.Customer)
                .Include(p => p.Booking).ThenInclude(b => b.Unit).ThenInclude(u => u.Project)
                .Include(p => p.Installment)
                .FirstOrDefaultAsync(p => p.Id == paymentId && p.BookingId == bookingId);

            if (payment == null)
                throw new InvalidOperationException("Payment not found for this booking.");

            var booking = payment.Booking;
            var customer = booking.Customer;
            var unit = booking.Unit;
            var project = unit?.Project;

            string? receivedByName = null;
            if (payment.RecordedByUserId.HasValue)
            {
                receivedByName = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.UserId == payment.RecordedByUserId.Value)
                    .Select(u => u.FullName)
                    .FirstOrDefaultAsync();
            }

            var unitNumber = unit?.UnitNumber ?? string.Empty;
            string? block = null;
            var dashIndex = unitNumber.IndexOf('-');
            if (dashIndex > 0)
                block = unitNumber.Substring(0, dashIndex).Trim();

            return new PaymentReceiptDto
            {
                PaymentId = payment.Id,
                ReceiptNumber = payment.ReceiptNumber,
                PaidAt = payment.PaidAt,
                ReceivedByName = receivedByName,
                BookingReference = booking.BookingReference,

                CustomerName = customer?.FullName ?? string.Empty,
                FatherName = customer?.FatherName,
                CustomerPhone = customer?.Phone,
                CustomerCnic = customer?.CNIC,
                CustomerAddress = customer?.Address,

                ProjectName = project?.ProjectName ?? string.Empty,
                UnitType = unit?.UnitType ?? string.Empty,
                UnitNumber = unitNumber,
                Block = string.IsNullOrWhiteSpace(block) ? null : block,
                FloorNumber = unit?.FloorNumber ?? 0,
                UnitSize = unit?.Size ?? 0m,

                Type = payment.Type,
                PaymentMethod = payment.PaymentMethod,
                PaymentReference = payment.PaymentReference,
                Amount = payment.Amount,

                InstallmentSequence = payment.Installment?.SequenceNumber,
                InstallmentType = payment.Installment?.Type
            };
        }

        private async Task EnsureNoActiveBookingAsync(int unitId)
        {
            var hasActive = await _context.Bookings
                .AnyAsync(b => b.UnitId == unitId && b.Status != BookingStatus.Cancelled);

            if (hasActive)
                throw new InvalidOperationException("This unit already has an active booking.");
        }

        // Callers must wrap this in a transaction: the reference depends on the generated
        // id, so the row is saved twice, and a failure in between must roll back both.
        private async Task PersistNewBookingAsync(Booking booking, Unit unit)
        {
            // Unique placeholder so concurrent creates never collide on the unique
            // BookingReference index before the id-based reference is known.
            booking.BookingReference = $"BK-PENDING-{Guid.NewGuid():N}";

            _context.Bookings.Add(booking);

            unit.Status = UnitStatus.Reserved;
            unit.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Reference depends on the generated id.
            booking.BookingReference = $"BK-{booking.Id:D6}";
            await _context.SaveChangesAsync();
        }

        private async Task<BookingResponseDto> GetResponseAsync(int id)
        {
            var booking = await _context.Bookings
                .AsNoTracking()
                .Include(b => b.Customer)
                .Include(b => b.Unit).ThenInclude(u => u.Project)
                .Include(b => b.Payments)
                .Include(b => b.Installments)
                .AsSplitQuery()
                .FirstAsync(b => b.Id == id);

            return MapProjection(booking);
        }

        private static BookingResponseDto MapProjection(Booking b)
        {
            var installmentTotals = ComputeInstallmentTotals(b);

            return new BookingResponseDto
            {
                Id = b.Id,
                BookingReference = b.BookingReference,
                CustomerId = b.CustomerId,
                CustomerName = b.Customer != null ? b.Customer.FullName : string.Empty,
                CustomerPhone = b.Customer != null ? b.Customer.Phone : string.Empty,
                CustomerFatherName = b.Customer != null ? b.Customer.FatherName : null,
                CustomerCnic = b.Customer != null ? b.Customer.CNIC : null,
                CustomerEmail = b.Customer != null ? b.Customer.Email : null,
                CustomerAddress = b.Customer != null ? b.Customer.Address : null,
                CustomerDateOfBirth = b.Customer != null ? b.Customer.DateOfBirth : null,
                CustomerNationality = b.Customer != null ? b.Customer.Nationality : null,
                CustomerOccupation = b.Customer != null ? b.Customer.Occupation : null,
                CustomerWhatsapp = b.Customer != null ? b.Customer.Whatsapp : null,
                UnitId = b.UnitId,
                UnitNumber = b.Unit != null ? b.Unit.UnitNumber : string.Empty,
                UnitType = b.Unit != null ? b.Unit.UnitType : string.Empty,
                UnitFloorNumber = b.Unit != null ? b.Unit.FloorNumber : 0,
                UnitSize = b.Unit != null ? b.Unit.Size : 0m,
                ProjectId = b.Unit != null ? b.Unit.ProjectId : 0,
                ProjectName = b.Unit != null && b.Unit.Project != null ? b.Unit.Project.ProjectName : string.Empty,
                BookingRequestId = b.BookingRequestId,
                Source = b.Source,
                Status = b.Status,
                ListPrice = b.ListPrice,
                AgreedSalePrice = b.AgreedSalePrice,
                DiscountAmount = b.DiscountAmount,
                DiscountReason = b.DiscountReason,
                BookingAmountRequired = b.BookingAmountRequired,
                BookingAmountReceived = b.BookingAmountReceived,
                TotalInstallmentAmount = b.TotalInstallmentAmount,
                InstallmentPaid = installmentTotals.Paid,
                InstallmentRemaining = installmentTotals.Remaining,
                HasInstallmentSchedule = installmentTotals.HasSchedule,
                BookingDate = b.BookingDate,
                BookingAmountDueDate = b.BookingAmountDueDate,
                BookingAmountConfirmedDate = b.BookingAmountConfirmedDate,
                PossessionDate = b.PossessionDate,
                CompletionDate = b.CompletionDate,
                CustomerNotes = b.CustomerNotes,
                InternalNotes = b.InternalNotes,
                SerialNo = b.SerialNo,
                ApartmentCategory = b.ApartmentCategory,
                Tower = b.Tower,
                IsCorner = b.IsCorner,
                PricePerSft = b.PricePerSft,
                DiscountPercent = b.DiscountPercent,
                ReferenceId = b.ReferenceId,
                PaymentThrough = b.PaymentThrough,
                ApplicationPaymentType = b.ApplicationPaymentType,
                ApplicationAmountReceived = b.ApplicationAmountReceived,
                ApplicationDate = b.ApplicationDate,
                NextOfKinName = b.NextOfKinName,
                NextOfKinRelation = b.NextOfKinRelation,
                NextOfKinContact = b.NextOfKinContact,
                NextOfKinCnic = b.NextOfKinCnic,
                NextOfKinDob = b.NextOfKinDob,
                NextOfKinAddress = b.NextOfKinAddress,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt,
                Payments = b.Payments == null
                    ? new List<BookingPaymentDto>()
                    : b.Payments
                        .OrderByDescending(p => p.PaidAt)
                        .Select(p => new BookingPaymentDto
                        {
                            Id = p.Id,
                            BookingId = p.BookingId,
                            InstallmentId = p.InstallmentId,
                            Type = p.Type,
                            Amount = p.Amount,
                            PaymentMethod = p.PaymentMethod,
                            PaymentReference = p.PaymentReference,
                            ReceiptNumber = p.ReceiptNumber,
                            Notes = p.Notes,
                            PaidAt = p.PaidAt
                        })
                        .ToList()
            };
        }

        public async Task<List<BookingResponseDto>> GetBookingsByCustomerEmailAsync(string email, int? userId = null)
        {
            if (string.IsNullOrWhiteSpace(email) && !userId.HasValue)
                return new List<BookingResponseDto>();

            var normalized = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

            var entities = await _context.Bookings
                .AsNoTracking()
                .Include(b => b.Customer)
                .Include(b => b.Unit).ThenInclude(u => u.Project)
                .Include(b => b.Payments)
                .Include(b => b.Installments)
                .AsSplitQuery()
                .Where(b => b.Customer != null
                         && b.Status != BookingStatus.Cancelled
                         && ((userId.HasValue && b.Customer.UserId == userId.Value)
                             || (normalized != null && b.Customer.Email != null && b.Customer.Email.ToLower() == normalized)))
                .OrderByDescending(b => b.BookingDate)
                .ToListAsync();

            return entities.Select(b => SanitizeForClient(MapProjection(b))).ToList();
        }

        public async Task<BookingResponseDto?> GetBookingByIdForCustomerEmailAsync(int id, string email, int? userId = null)
        {
            if (!await CustomerOwnsBookingByEmailAsync(id, email, userId))
                return null;

            var dto = await GetResponseAsync(id);
            return dto == null ? null : SanitizeForClient(dto);
        }

        public async Task<bool> CustomerOwnsBookingByEmailAsync(int bookingId, string email, int? userId = null)
        {
            if (string.IsNullOrWhiteSpace(email) && !userId.HasValue)
                return false;

            var normalized = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

            return await _context.Bookings
                .AsNoTracking()
                .AnyAsync(b => b.Id == bookingId
                            && b.Status != BookingStatus.Cancelled
                            && b.Customer != null
                            && ((userId.HasValue && b.Customer.UserId == userId.Value)
                                || (normalized != null && b.Customer.Email != null && b.Customer.Email.ToLower() == normalized)));
        }

        private static (decimal Paid, decimal Remaining, bool HasSchedule) ComputeInstallmentTotals(Booking b)
        {
            var installments = b.Installments?.ToList() ?? new List<Installment>();
            if (installments.Count == 0)
                return (0m, 0m, false);

            var paidByInstallment = (b.Payments ?? new List<Payment>())
                .Where(p => p.InstallmentId.HasValue && p.Type == PaymentType.Installment)
                .GroupBy(p => p.InstallmentId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

            var total = installments.Sum(i => i.Amount);
            var paid = installments.Sum(i =>
                paidByInstallment.TryGetValue(i.Id, out var amount) ? amount : 0m);

            return (paid, Math.Max(0m, total - paid), true);
        }

        private static BookingResponseDto SanitizeForClient(BookingResponseDto dto)
        {
            dto.InternalNotes = null;
            return dto;
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

        private static string AppendNote(string? existing, string note)
        {
            var entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC] {note}";
            return string.IsNullOrWhiteSpace(existing) ? entry : $"{existing}\n{entry}";
        }

        private static PaymentMethod ParseApplicationPaymentMethod(string? applicationPaymentType)
        {
            if (string.IsNullOrWhiteSpace(applicationPaymentType))
                return PaymentMethod.Cash;

            var compact = applicationPaymentType.Replace(" ", "").Replace("-", "");
            return Enum.TryParse<PaymentMethod>(compact, true, out var method) ? method : PaymentMethod.Cash;
        }

        // Receipt numbers are read-max-then-insert; two concurrent payments can pick the
        // same number and the unique index rejects the loser with a raw 500. Retry the
        // save with a freshly generated number instead.
        private async Task SaveWithUniqueReceiptNumberAsync(Payment payment)
        {
            const int maxAttempts = 5;
            for (var attempt = 1; ; attempt++)
            {
                payment.ReceiptNumber = await GenerateReceiptNumberAsync();
                try
                {
                    await _context.SaveChangesAsync();
                    return;
                }
                catch (DbUpdateException ex) when (attempt < maxAttempts && IsReceiptNumberCollision(ex))
                {
                    // Another payment claimed this number between read and insert; retry.
                }
            }
        }

        private static bool IsReceiptNumberCollision(DbUpdateException ex)
        {
            return ex.InnerException is SqlException { Number: 2601 or 2627 } sql
                   && sql.Message.Contains("ReceiptNumber", StringComparison.OrdinalIgnoreCase);
        }
    }
}
