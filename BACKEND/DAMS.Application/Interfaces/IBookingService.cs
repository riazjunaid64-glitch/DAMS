using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Domain.Entities;

namespace DAMS.Application.Interfaces
{
    public interface IBookingService
    {
        /// <summary>Admin creates a booking for a walk-in / phone customer.</summary>
        Task<BookingResponseDto> CreateBookingAsync(CreateBookingDto dto, int adminUserId, CancellationToken cancellationToken = default);

        Task<BookingResponseDto?> GetBookingByIdAsync(int id);

        Task<BookingListDto> GetBookingsAsync(BookingFilterDto filter);

        /// <summary>
        /// Cancels a booking and records its cancellation settlement (customer cash received,
        /// refund decided, retained amount) atomically with releasing the unit and running the
        /// existing commission/rebate cancellation lifecycle. Idempotent on dto.IdempotencyKey.
        /// </summary>
        Task<BookingResponseDto> CancelBookingAsync(int id, CancelBookingDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default);

        /// <summary>Pays a previously-deferred (PayLater) cancellation refund. Idempotent on
        /// dto.IdempotencyKey; rejects if the refund was already paid.</summary>
        Task<BookingResponseDto> PayCancellationRefundAsync(int bookingId, PayCancellationRefundDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the negotiated terms (sale price, discount, booking amount required, due date)
        /// on a booking that is still AwaitingBookingAmount. Required before booking-amount
        /// payments can be recorded for request-derived bookings.
        /// </summary>
        Task<BookingResponseDto> UpdateBookingFinancialsAsync(int id, UpdateBookingFinancialsDto dto, int adminUserId);

        /// <summary>
        /// Records a (possibly partial) booking-amount payment. When the received total
        /// reaches the required amount, the booking moves to PaymentPlanActive and the
        /// unit moves to OnPaymentPlan.
        /// </summary>
        Task<BookingResponseDto> RecordBookingAmountPaymentAsync(int bookingId, RecordBookingAmountPaymentDto dto, int adminUserId, CancellationToken cancellationToken = default);

        /// <summary>Marks possession as handed over on an active payment plan.</summary>
        Task<BookingResponseDto> GivePossessionAsync(int id, DateTime? possessionDate, int adminUserId);

        /// <summary>
        /// Completes the sale once the booking amount and every installment are fully
        /// paid. Moves the unit to Sold.
        /// </summary>
        Task<BookingResponseDto> CompleteSaleAsync(int id, int adminUserId);

        Task<List<BookingPaymentDto>> GetBookingPaymentsAsync(int bookingId);

        /// <summary>
        /// Builds a render-ready receipt payload for a single payment (read-only).
        /// </summary>
        Task<PaymentReceiptDto> GetPaymentReceiptAsync(int bookingId, int paymentId);

        Task<List<BookingResponseDto>> GetBookingsByCustomerEmailAsync(string email, int? userId = null);

        Task<BookingResponseDto?> GetBookingByIdForCustomerEmailAsync(int id, string email, int? userId = null);

        Task<bool> CustomerOwnsBookingByEmailAsync(int bookingId, string email, int? userId = null);
    }
}
