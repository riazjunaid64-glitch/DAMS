using DAMS.Application.DTOs.CustomerDtos;
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
    public class CustomerController : ControllerBase
    {
        private readonly ICustomerService _customerService;
        private readonly ICustomerAccountLinkService _accountLinks;

        public CustomerController(ICustomerService customerService, ICustomerAccountLinkService accountLinks)
        {
            _customerService = customerService;
            _accountLinks = accountLinks;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateCustomerDto dto)
        {
            try
            {
                var adminUserId = GetUserId();
                var result = await _customerService.CreateCustomerAsync(
                    dto,
                    adminUserId,
                    User.FindFirstValue(ClaimTypes.Name));
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] CustomerSource? source,
            [FromQuery] CustomerStatus? status,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var filter = new CustomerFilterDto
            {
                Source = source,
                Status = status,
                SearchTerm = search,
                Page = page,
                PageSize = pageSize
            };

            var result = await _customerService.GetCustomersAsync(filter);
            return Ok(result);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _customerService.GetCustomerByIdAsync(id);
            if (result == null)
                return NotFound(new { message = "Customer not found." });
            return Ok(result);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerDto dto)
        {
            try
            {
                var result = await _customerService.UpdateCustomerAsync(id, dto);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ── Account ownership ───────────────────────────────────────────────────────
        //
        // The claim flow, in the one form DAMS can actually deliver today. The story's preferred
        // route is an OTP to a phone number already on the customer's record; DAMS has no SMS
        // transport (NotificationChannel is InApp, Email and WebPush only), so building that would
        // mean inventing an untested delivery path and trusting it with portal access on day one.
        // What is implemented instead is the alternative the story names for exactly this case:
        // an administrator verifies the person's identity out of band and records how.
        //
        // A matching email, CNIC or phone number is not evidence and does not appear anywhere in
        // this surface. What does appear is a named administrator, a reason, and an audit row that
        // outlives both.

        /// <summary>Attaches a verified client login to an existing customer record.</summary>
        [HttpPost("{id:int}/account-link")]
        public async Task<IActionResult> LinkAccount(
            int id, [FromBody] LinkCustomerAccountDto dto, CancellationToken cancellationToken)
        {
            var result = await _accountLinks.LinkByAdministratorAsync(
                id, dto.UserId, GetUserId(), dto.Reason, dto.AllowReassign, cancellationToken);

            return result.Outcome switch
            {
                CustomerAccountLinkOutcome.CustomerNotFound or CustomerAccountLinkOutcome.UserNotFound
                    => NotFound(new { message = result.Message }),
                // A conflict is not a client error to be retried blindly — it is a decision waiting
                // for somebody. 409 says so, and the body says what confirming would mean.
                CustomerAccountLinkOutcome.Conflict
                    => Conflict(new { message = result.Message, currentUserId = result.LinkedUserId }),
                CustomerAccountLinkOutcome.AccountNotEligible
                    => BadRequest(new { message = result.Message }),
                _ => Ok(new { message = result.Message, userId = result.LinkedUserId })
            };
        }

        /// <summary>Removes a customer's account link. Their bookings stop being visible to it.</summary>
        [HttpDelete("{id:int}/account-link")]
        public async Task<IActionResult> UnlinkAccount(
            int id, [FromBody] UnlinkCustomerAccountDto dto, CancellationToken cancellationToken)
        {
            var result = await _accountLinks.UnlinkByAdministratorAsync(
                id, GetUserId(), dto.Reason, cancellationToken);

            return result.Outcome == CustomerAccountLinkOutcome.CustomerNotFound
                ? NotFound(new { message = result.Message })
                : Ok(new { message = result.Message });
        }

        /// <summary>Every decision ever taken about who owns this customer, refusals included.</summary>
        [HttpGet("{id:int}/account-link/history")]
        public async Task<IActionResult> AccountLinkHistory(int id, CancellationToken cancellationToken) =>
            Ok(await _accountLinks.GetHistoryAsync(id, cancellationToken));

        private int GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out var id) ? id : 0;
        }
    }
}
