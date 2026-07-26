using DAMS.Application.DTOs.BookingRequestDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingRequestController : ControllerBase
    {
        private readonly IBookingRequestService _bookingRequestService;

        public BookingRequestController(IBookingRequestService bookingRequestService)
        {
            _bookingRequestService = bookingRequestService;
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateBookingRequest([FromBody] CreateBookingRequestDto dto)
        {
            int? userId = null;
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out var parsedUserId))
            {
                userId = parsedUserId;
            }

            try
            {
                var result = await _bookingRequestService.CreateBookingRequestAsync(dto, userId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetBookingRequest([FromRoute] int id)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = User.FindFirstValue(ClaimTypes.Role);

            var result = await _bookingRequestService.GetBookingRequestByIdAsync(id);
            if (result == null)
                return NotFound(new { message = "Booking request not found." });

            if (role != "Admin" && result.UserId?.ToString() != userIdClaim)
                return Forbid();

            return Ok(result);
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetBookingRequests(
            [FromQuery] BookingRequestStatus? status,
            [FromQuery] int? projectId,
            [FromQuery] string? search,
            [FromQuery] DateTime? requestedFrom,
            [FromQuery] DateTime? requestedTo,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string sortBy = "RequestedAt",
            [FromQuery] bool sortDesc = true)
        {
            var filter = new BookingRequestFilterDto
            {
                Status = status,
                ProjectId = projectId,
                SearchTerm = search,
                RequestedFrom = requestedFrom,
                RequestedTo = requestedTo,
                Page = Math.Max(page, 1),
                PageSize = Math.Clamp(pageSize, 1, 100),
                SortBy = sortBy,
                SortDescending = sortDesc
            };

            var result = await _bookingRequestService.GetBookingRequestsAsync(filter);
            return Ok(result);
        }

        [HttpGet("my-requests")]
        [Authorize]
        public async Task<IActionResult> GetMyBookingRequests()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var result = await _bookingRequestService.GetMyBookingRequestsAsync(userId);
            return Ok(result);
        }

        [HttpPost("{id:int}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveBookingRequest([FromRoute] int id)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var adminUserId))
                return Unauthorized();

            try
            {
                var result = await _bookingRequestService.ApproveBookingRequestAsync(id, adminUserId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id:int}/reject")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RejectBookingRequest([FromRoute] int id, [FromBody] RejectBookingRequestDto? dto)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var adminUserId))
                return Unauthorized();

            try
            {
                var result = await _bookingRequestService.RejectBookingRequestAsync(id, adminUserId, dto?.RejectionReason);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Customer withdraws their own pending request; the unit returns to the market.
        [HttpPost("{id:int}/cancel")]
        [Authorize]
        public async Task<IActionResult> CancelBookingRequest([FromRoute] int id)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            try
            {
                var result = await _bookingRequestService.CancelBookingRequestAsync(id, userId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("stats")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetStats()
        {
            var stats = await _bookingRequestService.GetBookingRequestStatsAsync();
            return Ok(stats);
        }

        [HttpGet("unit/{unitId:int}/has-pending")]
        [Authorize]
        public async Task<IActionResult> HasPendingRequest([FromRoute] int unitId)
        {
            var hasPending = await _bookingRequestService.HasPendingRequestForUnitAsync(unitId);
            return Ok(new { hasPending });
        }
    }
}
