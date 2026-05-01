using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BookingController : ControllerBase
    {
        private readonly IBookingService _bookingService;
        private readonly IInstallmentService _installmentService;
        private readonly IPaymentService _paymentService;

        public BookingController(
            IBookingService bookingService,
            IInstallmentService installmentService,
            IPaymentService paymentService)
        {
            _bookingService = bookingService;
            _installmentService = installmentService;
            _paymentService = paymentService;
        }

        /// <summary>
        /// Create a new booking (Admin only)
        /// Validates: Unit available, down payment valid, only one active booking per unit
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(BookingResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<BookingResponseDto>> CreateBooking([FromBody] CreateBookingRequestDto request)
        {
            try
            {
                var booking = await _bookingService.CreateBookingAsync(request);
                return CreatedAtAction(nameof(GetBooking), new { id = booking.Id }, booking);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new { message = "An error occurred while creating booking.", error = ex.Message });
            }
        }

        /// <summary>
        /// Get booking details by ID (Public)
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(BookingResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BookingResponseDto>> GetBooking(int id)
        {
            var booking = await _bookingService.GetBookingByIdAsync(id);
            if (booking == null)
                return NotFound(new { message = $"Booking with ID {id} not found." });

            return Ok(booking);
        }

        /// <summary>
        /// Get all bookings for authenticated client
        /// </summary>
        [HttpGet("client/{clientId}")]
        [Authorize]
        [ProducesResponseType(typeof(List<BookingResponseDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<BookingResponseDto>>> GetClientBookings(int clientId)
        {
            var bookings = await _bookingService.GetClientBookingsAsync(clientId);
            return Ok(bookings);
        }

        /// <summary>
        /// Cancel a booking (Admin only)
        /// Reverts unit to Available and soft-deletes booking
        /// Cannot cancel if fully paid
        /// </summary>
        [HttpPost("{id}/cancel")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult> CancelBooking(int id, [FromBody] CancelBookingRequestDto? request = null)
        {
            try
            {
                var result = await _bookingService.CancelBookingAsync(id, request?.Reason);
                if (result)
                    return Ok(new { message = "Booking cancelled successfully." });

                return BadRequest(new { message = "Failed to cancel booking." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while cancelling booking.", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all payments for a booking (Public)
        /// </summary>
        [HttpGet("{bookingId}/payments")]
        [ProducesResponseType(typeof(List<object>), StatusCodes.Status200OK)]
        public async Task<ActionResult> GetBookingPayments(int bookingId)
        {
            try
            {
                var payments = await _paymentService.GetBookingPaymentsAsync(bookingId);
                return Ok(new { bookingId, payments, totalPaid = payments.Sum(p => p.Amount) });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while retrieving payments.", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all installments for a booking (Public)
        /// </summary>
        [HttpGet("{bookingId}/installments")]
        [ProducesResponseType(typeof(List<object>), StatusCodes.Status200OK)]
        public async Task<ActionResult> GetBookingInstallments(int bookingId)
        {
            try
            {
                var installments = await _installmentService.GetBookingInstallmentsAsync(bookingId);
                var pending = await _installmentService.GetPendingInstallmentsAsync(bookingId);
                return Ok(new { bookingId, installments, pendingCount = pending.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while retrieving installments.", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all pending (unpaid) installments for a booking (Public)
        /// </summary>
        [HttpGet("{bookingId}/installments/pending")]
        [ProducesResponseType(typeof(List<object>), StatusCodes.Status200OK)]
        public async Task<ActionResult> GetPendingInstallments(int bookingId)
        {
            try
            {
                var pendingInstallments = await _installmentService.GetPendingInstallmentsAsync(bookingId);
                var totalPending = pendingInstallments.Sum(i => i.Amount);
                return Ok(new { bookingId, installments = pendingInstallments, totalPending });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while retrieving pending installments.", error = ex.Message });
            }
        }
    }
}
