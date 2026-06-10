using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Client")]
    public class MyProjectsController : ControllerBase
    {
        private readonly IBookingService _bookingService;
        private readonly IInstallmentService _installmentService;

        public MyProjectsController(IBookingService bookingService, IInstallmentService installmentService)
        {
            _bookingService = bookingService;
            _installmentService = installmentService;
        }

        [HttpGet]
        public async Task<IActionResult> GetMyProjects()
        {
            var email = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { message = "Email not found in your account." });

            var items = await _bookingService.GetBookingsByCustomerEmailAsync(email);
            return Ok(items);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetMyProject(int id)
        {
            var email = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { message = "Email not found in your account." });

            var result = await _bookingService.GetBookingByIdForCustomerEmailAsync(id, email);
            if (result == null)
                return NotFound(new { message = "Project purchase not found for your account." });

            return Ok(result);
        }

        [HttpGet("{id:int}/installments")]
        public async Task<IActionResult> GetInstallmentSchedule(int id)
        {
            var email = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { message = "Email not found in your account." });

            if (!await _bookingService.CustomerOwnsBookingByEmailAsync(id, email))
                return NotFound(new { message = "Project purchase not found for your account." });

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

        [HttpGet("{id:int}/payments/{paymentId:int}/receipt")]
        public async Task<IActionResult> GetPaymentReceipt(int id, int paymentId)
        {
            var email = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { message = "Email not found in your account." });

            if (!await _bookingService.CustomerOwnsBookingByEmailAsync(id, email))
                return NotFound(new { message = "Project purchase not found for your account." });

            try
            {
                var result = await _bookingService.GetPaymentReceiptAsync(id, paymentId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
