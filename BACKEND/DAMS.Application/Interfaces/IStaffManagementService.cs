using DAMS.Application.Common;
using DAMS.Application.DTOs.EmployeeDtos;

namespace DAMS.Application.Interfaces
{
    public interface IStaffManagementService
    {
        Task<List<StaffDirectoryDto>> GetDirectoryAsync(
            LeadUserContext actor,
            CancellationToken cancellationToken = default);

        Task<List<StaffAccountDto>> GetAccountsAsync(CancellationToken cancellationToken = default);

        Task<List<LinkableUserDto>> GetLinkableUsersAsync(CancellationToken cancellationToken = default);

        Task<List<CustomerLookupDto>> SearchCustomersAsync(
            LeadUserContext actor,
            string search,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Provisions DAMS access for a staff member. A new login is created with no password
        /// and invited to choose one; the account is kept even if the invitation email fails.
        /// </summary>
        /// <param name="actor">The authenticated Admin. Recorded as the inviter, so it is
        /// never taken from the request body.</param>
        Task<StaffAccountProvisionResult> CreateAsync(
            LeadUserContext actor,
            CreateStaffAccountDto dto,
            CancellationToken cancellationToken = default);

        Task<StaffAccountDto> UpdateAsync(
            int employeeId,
            UpdateStaffAccountDto dto,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a waiting staff member a fresh activation link, invalidating the previous
        /// one. Only a login that is still Invited can be resent to; the account status is
        /// never changed and no password is ever generated.
        /// </summary>
        Task<StaffInvitationResult> ResendInvitationAsync(
            LeadUserContext actor,
            int employeeId,
            CancellationToken cancellationToken = default);
    }
}
