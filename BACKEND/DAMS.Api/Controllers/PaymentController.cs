using DAMS.Application.DTOs.PaymentDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _paymentService;

        public PaymentController(IPaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        /// <summary>
        /// Process a payment (down payment or installment)
        /// Validates: Amount > 0, doesn't exceed booking total, installment not already paid
        /// Updates: Installment status to Paid, Booking AmountPaid, marks booking Completed if fully paid
        /// Admin only
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(PaymentResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<PaymentResponseDto>> ProcessPayment([FromBody] CreatePaymentRequestDto request)
        {
            try
            {
                var payment = await _paymentService.ProcessPaymentAsync(request);
                return CreatedAtAction(nameof(GetPayment), new { id = payment.Id }, payment);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while processing payment.", error = ex.Message });
            }
        }

        /// <summary>
        /// Get payment details by ID (Public)
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(PaymentResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PaymentResponseDto>> GetPayment(int id)
        {
            var payment = await _paymentService.GetPaymentByIdAsync(id);
            if (payment == null)
                return NotFound(new { message = $"Payment with ID {id} not found." });

            return Ok(payment);
        }

        /// <summary>
        /// Get all payments for a booking with summary (Public)
        /// </summary>
        [HttpGet("booking/{bookingId}")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public async Task<ActionResult> GetBookingPayments(int bookingId)
        {
            try
            {
                var payments = await _paymentService.GetBookingPaymentsAsync(bookingId);
                var totalPaid = payments.Sum(p => p.Amount);
                var downPayments = await _paymentService.GetDownPaymentsAsync(bookingId);

                return Ok(new
                {
                    bookingId,
                    payments,
                    totalPaid,
                    downPaymentCount = downPayments.Count,
                    downPaymentTotal = downPayments.Sum(p => p.Amount),
                    installmentPaymentCount = payments.Count - downPayments.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while retrieving payments.", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all down payments for a booking (Public)
        /// Down payments are payments with null InstallmentId
        /// </summary>
        [HttpGet("booking/{bookingId}/down-payments")]
        [ProducesResponseType(typeof(List<PaymentResponseDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult> GetDownPayments(int bookingId)
        {
            try
            {
                var downPayments = await _paymentService.GetDownPaymentsAsync(bookingId);
                var total = downPayments.Sum(p => p.Amount);

                return Ok(new
                {
                    bookingId,
                    downPayments,
                    total,
                    count = downPayments.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while retrieving down payments.", error = ex.Message });
            }
        }

        /// <summary>
        /// Get total paid amount for a booking (Public)
        /// </summary>
        [HttpGet("booking/{bookingId}/total-paid")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public async Task<ActionResult> GetTotalPaid(int bookingId)
        {
            try
            {
                var totalPaid = await _paymentService.GetTotalPaidAsync(bookingId);
                return Ok(new { bookingId, totalPaid });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while calculating total paid.", error = ex.Message });
            }
        }

        /// <summary>
        /// Validate payment amount (check if payment would exceed booking total)
        /// </summary>
        [HttpPost("validate")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public async Task<ActionResult> ValidatePaymentAmount([FromBody] ValidatePaymentRequestDto request)
        {
            try
            {
                var isValid = await _paymentService.ValidatePaymentAmountAsync(request.BookingId, request.Amount);
                return Ok(new { isValid, message = isValid ? "Payment amount is valid." : "Payment would exceed booking total." });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred during validation.", error = ex.Message });
            }
        }
    }

    public class ValidatePaymentRequestDto
    {
        public int BookingId { get; set; }
        public decimal Amount { get; set; }
    }
}
