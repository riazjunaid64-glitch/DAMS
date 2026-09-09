using DAMS.Api.Security;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    /// <summary>
    /// Everything a signed-in buyer can see about their own purchases.
    ///
    /// <para>
    /// One authorization rule governs the whole controller, and it is deliberately the only one:
    /// the authenticated subject is <see cref="ClaimTypes.NameIdentifier"/>, and a resource is
    /// reachable when the booking behind it belongs to a Customer owned by that user id. The
    /// email on the token is never consulted. It used to be, as a fallback for bookings whose
    /// Customer had no login attached, and that fallback meant anybody who could register an
    /// address could read the financial history of whoever else had it on file.
    /// </para>
    ///
    /// <para>
    /// Every denial here is a 404 with the same sentence. A client asking for somebody else's
    /// booking must not be able to tell it apart from asking for one that does not exist.
    /// </para>
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = DamsPolicies.VerifiedClient)]
    public class MyProjectsController : ControllerBase
    {
        private const string NotYours = "Project purchase not found for your account.";

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
            var userId = GetUserId();
            if (userId == null)
                return Unauthorized(new { message = "Your session is not valid. Sign in again." });

            return Ok(await _bookingService.GetBookingsForUserAsync(userId.Value));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetMyProject(int id)
        {
            var userId = GetUserId();
            if (userId == null)
                return Unauthorized(new { message = "Your session is not valid. Sign in again." });

            var result = await _bookingService.GetBookingForUserAsync(id, userId.Value);
            if (result == null)
                return NotFound(new { message = NotYours });

            return Ok(result);
        }

        [HttpGet("{id:int}/installments")]
        public async Task<IActionResult> GetInstallmentSchedule(int id)
        {
            var userId = GetUserId();
            if (userId == null)
                return Unauthorized(new { message = "Your session is not valid. Sign in again." });

            if (!await _bookingService.UserOwnsBookingAsync(id, userId.Value))
                return NotFound(new { message = NotYours });

            try
            {
                return Ok(await _installmentService.GetScheduleAsync(id));
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// A receipt needs two facts, and the second is as important as the first: the booking
        /// must belong to this login, and the payment must belong to that booking. Checking only
        /// the booking would let a client walk the payment id space and pull other people's
        /// receipts through a booking they legitimately own — the second check is enforced by
        /// <c>GetPaymentReceiptAsync</c>, which matches on the pair and throws when they do not
        /// belong together.
        /// </summary>
        [HttpGet("{id:int}/payments/{paymentId:int}/receipt")]
        public async Task<IActionResult> GetPaymentReceipt(int id, int paymentId)
        {
            var userId = GetUserId();
            if (userId == null)
                return Unauthorized(new { message = "Your session is not valid. Sign in again." });

            if (!await _bookingService.UserOwnsBookingAsync(id, userId.Value))
                return NotFound(new { message = NotYours });

            try
            {
                return Ok(await _bookingService.GetPaymentReceiptAsync(id, paymentId));
            }
            catch (InvalidOperationException)
            {
                // Same answer as a booking that is not theirs. Whether the payment exists under
                // somebody else's booking is not something this endpoint may reveal.
                return NotFound(new { message = "Receipt not found for this purchase." });
            }
        }

        private int? GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out var id) ? id : (int?)null;
        }
    }
}
