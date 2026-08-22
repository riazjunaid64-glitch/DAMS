using DAMS.Application.Common;
using DAMS.Application.DTOs.EmployeeDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/staff")]
    [Authorize(Roles = LeadRoles.Staff)]
    public class StaffController : ControllerBase
    {
        private readonly ILeadUserContextResolver _resolver;
        private readonly IStaffManagementService _staff;

        public StaffController(ILeadUserContextResolver resolver, IStaffManagementService staff)
        {
            _resolver = resolver;
            _staff = staff;
        }

        [HttpGet("directory")]
        public async Task<IActionResult> GetDirectory(CancellationToken cancellationToken)
        {
            try
            {
                var actor = await _resolver.ResolveAsync(User, cancellationToken);
                return Ok(await _staff.GetDirectoryAsync(actor, cancellationToken));
            }
            catch (LeadAuthorizationException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
        }

        [HttpGet("accounts")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<List<StaffAccountDto>> GetAccounts(CancellationToken cancellationToken) =>
            _staff.GetAccountsAsync(cancellationToken);

        [HttpGet("linkable-users")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<List<LinkableUserDto>> GetLinkableUsers(CancellationToken cancellationToken) =>
            _staff.GetLinkableUsersAsync(cancellationToken);

        [HttpGet("customer-lookup")]
        [Authorize(Roles = LeadRoles.AdminOrManager)]
        public async Task<List<CustomerLookupDto>> SearchCustomers(
            [FromQuery] string search,
            CancellationToken cancellationToken)
        {
            var actor = await _resolver.ResolveAsync(User, cancellationToken);
            return await _staff.SearchCustomersAsync(actor, search, cancellationToken);
        }

        [HttpPost("accounts")]
        [Authorize(Roles = LeadRoles.Admin)]
        public async Task<IActionResult> Create(
            [FromBody] CreateStaffAccountDto dto,
            CancellationToken cancellationToken)
        {
            try
            {
                // The inviter is the authenticated Admin, resolved from the token — never a
                // user id the request body could have chosen.
                var actor = await _resolver.ResolveAsync(User, cancellationToken);
                return Ok(await _staff.CreateAsync(actor, dto, cancellationToken));
            }
            catch (LeadAuthorizationException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Sends a waiting staff member a new activation link and invalidates the previous
        /// one. Never activates the account and never produces a password.
        /// </summary>
        [HttpPost("accounts/{employeeId:int}/resend-invitation")]
        [Authorize(Roles = LeadRoles.Admin)]
        public async Task<IActionResult> ResendInvitation(
            int employeeId,
            CancellationToken cancellationToken)
        {
            try
            {
                var actor = await _resolver.ResolveAsync(User, cancellationToken);
                return Ok(await _staff.ResendInvitationAsync(actor, employeeId, cancellationToken));
            }
            catch (LeadAuthorizationException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("accounts/{employeeId:int}")]
        [Authorize(Roles = LeadRoles.Admin)]
        public async Task<IActionResult> Update(
            int employeeId,
            [FromBody] UpdateStaffAccountDto dto,
            CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _staff.UpdateAsync(employeeId, dto, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
