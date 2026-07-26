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
            string search,
            CancellationToken cancellationToken = default);

        Task<StaffAccountDto> CreateAsync(
            CreateStaffAccountDto dto,
            CancellationToken cancellationToken = default);

        Task<StaffAccountDto> UpdateAsync(
            int employeeId,
            UpdateStaffAccountDto dto,
            CancellationToken cancellationToken = default);
    }
}
