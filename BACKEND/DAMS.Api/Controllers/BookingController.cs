using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingController : ControllerBase
    {
        private readonly IBookingService _bookingService;

        public BookingController(IBookingService bookingService)
        {
            _bookingService = bookingService;
        }

        [Authorize(Roles = "Client")]
        [HttpPost]
        public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequestDto dto)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var clientUserId))
                return Unauthorized();

            try
            {
                var result = await _bookingService.CreateBookingAsync(clientUserId, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Lists bookings for a client. Callers may only access their own records unless they are Admin.
        /// </summary>
        [Authorize]
        [HttpGet("client/{clientId:int}")]
        public async Task<IActionResult> GetBookingsForClient([FromRoute] int clientId)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var callerId))
                return Unauthorized();

            if (role != "Admin" && callerId != clientId)
                return Forbid();

            var list = await _bookingService.GetBookingsForClientAsync(clientId);
            return Ok(list);
        }
    }
}
