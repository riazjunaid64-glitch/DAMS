using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.InstallmentDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class BookingController : ControllerBase
    {
        private readonly IBookingService _bookingService;
        private readonly IInstallmentService _installmentService;

        public BookingController(IBookingService bookingService, IInstallmentService installmentService)
        {
            _bookingService = bookingService;
            _installmentService = installmentService;
        }

        // Admin creates a booking directly for a walk-in / phone customer.
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateBookingDto dto)
        {
            try
            {
                var adminUserId = GetUserId();
                var result = await _bookingService.CreateBookingAsync(dto, adminUserId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] BookingStatus? status,
            [FromQuery] int? projectId,
            [FromQuery] int? customerId,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var filter = new BookingFilterDto
            {
                Status = status,
                ProjectId = projectId,
                CustomerId = customerId,
                SearchTerm = search,
                Page = page,
                PageSize = pageSize
            };

            var result = await _bookingService.GetBookingsAsync(filter);
            return Ok(result);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _bookingService.GetBookingByIdAsync(id);
            if (result == null)
                return NotFound(new { message = "Booking not found." });
            return Ok(result);
        }

        [HttpGet("customer/{customerId:int}")]
        public async Task<IActionResult> GetByCustomer(int customerId)
        {
            var filter = new BookingFilterDto { CustomerId = customerId, PageSize = 100 };
            var result = await _bookingService.GetBookingsAsync(filter);
            return Ok(result);
        }

        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, [FromBody] CancelBookingDto? dto)
        {
            try
            {
                var adminUserId = GetUserId();
                var result = await _bookingService.CancelBookingAsync(id, dto?.Reason, adminUserId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Record a (possibly partial) booking-amount payment.
        [HttpPost("{id:int}/booking-amount-payment")]
        public async Task<IActionResult> RecordBookingAmountPayment(int id, [FromBody] RecordBookingAmountPaymentDto dto)
        {
            try
            {
                var adminUserId = GetUserId();
                var result = await _bookingService.RecordBookingAmountPaymentAsync(id, dto, adminUserId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id:int}/payments")]
        public async Task<IActionResult> GetPayments(int id)
        {
            try
            {
                var result = await _bookingService.GetBookingPaymentsAsync(id);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpGet("{id:int}/installments")]
        public async Task<IActionResult> GetInstallmentSchedule(int id)
        {
            try
            {
                var result = await _installmentService.GetScheduleAsync(id);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // Record a (possibly partial) payment against a single installment.
        [HttpPost("{id:int}/installments/{installmentId:int}/payment")]
        public async Task<IActionResult> RecordInstallmentPayment(int id, int installmentId, [FromBody] RecordInstallmentPaymentDto dto)
        {
            try
            {
                var adminUserId = GetUserId();
                var result = await _installmentService.RecordInstallmentPaymentAsync(id, installmentId, dto, adminUserId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id:int}/installment-plan/generate")]
        public async Task<IActionResult> GenerateInstallmentPlan(int id, [FromBody] GenerateInstallmentPlanDto dto)
        {
            try
            {
                var adminUserId = GetUserId();
                var result = await _installmentService.GenerateScheduleAsync(id, dto, adminUserId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private int GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out var id) ? id : 0;
        }
    }
}
