using DAMS.Application.Common;
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
        public IActionResult CreateBookingRequest([FromBody] CreateBookingRequestDto dto)
        {
            // Every enquiry now arrives through the Lead CRM and becomes a booking with Convert.
            // Nothing in the app calls this any more; it stays routable only so old clients get a
            // clear answer instead of a 404. The other request calls, and all old data, are untouched.
            return BadRequest(new { message = "Booking requests are closed. Please contact our sales team." });
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

            // The same non-disclosing denial the customer portal gives. A 403 here would have
            // separated "somebody else's request" from "no such request", which is exactly what
            // somebody walking the id space is trying to learn.
            if (role is not (AppRoles.Admin or AppRoles.Accountant) && result.UserId?.ToString() != userIdClaim)
                return NotFound(new { message = "Booking request not found." });

            return Ok(result);
        }

        [HttpGet]
        [Authorize(Roles = AppRoles.AdminOrAccountant)]
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

        // Approving converts the request's lead (marks it Won, creates the customer and booking),
        // and rejecting closes its held enquiries. Both are Lead CRM decisions, so the Accountant,
        // who can read every request, does not make them.
        [HttpPost("{id:int}/approve")]
        [Authorize(Roles = AppRoles.Admin)]
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
        [Authorize(Roles = AppRoles.Admin)]
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
        [Authorize(Roles = AppRoles.AdminOrAccountant)]
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
