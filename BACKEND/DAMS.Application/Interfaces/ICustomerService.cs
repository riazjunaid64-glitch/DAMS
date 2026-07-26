using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Domain.Enums;

namespace DAMS.Application.Interfaces
{
    public interface ICustomerService
    {
        Task<CustomerResponseDto> CreateCustomerAsync(CreateCustomerDto dto, int? createdByUserId);

        Task<CustomerResponseDto?> GetCustomerByIdAsync(int id);

        Task<CustomerListDto> GetCustomersAsync(CustomerFilterDto filter);

        Task<CustomerResponseDto> UpdateCustomerAsync(int id, UpdateCustomerDto dto);

        /// <summary>
        /// Finds an existing customer (by CNIC, then phone, then email) or creates a new one.
        /// Used by booking, booking-request approval and lead conversion. The result states
        /// whether a customer was created, which conversion has to report and audit.
        /// </summary>
        Task<CustomerResolution> FindOrCreateCustomerAsync(
            string fullName,
            string phone,
            string? cnic,
            string? email,
            string? address,
            CustomerSource source,
            string? sourceNotes,
            int? createdByUserId,
            string? fatherName = null,
            DateTime? dateOfBirth = null,
            string? nationality = null,
            string? occupation = null,
            string? whatsapp = null,
            int? linkUserId = null);
    }
}
