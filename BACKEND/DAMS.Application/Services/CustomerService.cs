using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class CustomerService : ICustomerService
    {
        private readonly AppDbContext _context;

        public CustomerService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<CustomerResponseDto> CreateCustomerAsync(CreateCustomerDto dto, int? createdByUserId)
        {
            var customer = new Customer
            {
                FullName = dto.FullName.Trim(),
                FatherName = string.IsNullOrWhiteSpace(dto.FatherName) ? null : dto.FatherName.Trim(),
                Phone = NormalizePhone(dto.Phone),
                CNIC = string.IsNullOrWhiteSpace(dto.CNIC) ? null : dto.CNIC.Trim(),
                Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant(),
                Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim(),
                Source = dto.Source,
                SourceNotes = string.IsNullOrWhiteSpace(dto.SourceNotes) ? null : dto.SourceNotes.Trim(),
                Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
                Status = CustomerStatus.Active,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            return Map(customer, 0);
        }

        public async Task<CustomerResponseDto?> GetCustomerByIdAsync(int id)
        {
            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (customer == null)
                return null;

            var bookingsCount = await _context.Bookings.CountAsync(b => b.CustomerId == id);
            return Map(customer, bookingsCount);
        }

        public async Task<CustomerListDto> GetCustomersAsync(CustomerFilterDto filter)
        {
            var query = _context.Customers.AsNoTracking().AsQueryable();

            if (filter.Source.HasValue)
                query = query.Where(c => c.Source == filter.Source.Value);

            if (filter.Status.HasValue)
                query = query.Where(c => c.Status == filter.Status.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(c =>
                    c.FullName.ToLower().Contains(term) ||
                    c.Phone.Contains(term) ||
                    (c.CNIC != null && c.CNIC.ToLower().Contains(term)) ||
                    (c.Email != null && c.Email.ToLower().Contains(term)));
            }

            var totalCount = await query.CountAsync();

            var page = filter.Page < 1 ? 1 : filter.Page;
            var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;

            var items = await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new CustomerResponseDto
                {
                    Id = c.Id,
                    FullName = c.FullName,
                    FatherName = c.FatherName,
                    Phone = c.Phone,
                    CNIC = c.CNIC,
                    Email = c.Email,
                    Address = c.Address,
                    Source = c.Source,
                    SourceNotes = c.SourceNotes,
                    Status = c.Status,
                    UserId = c.UserId,
                    Notes = c.Notes,
                    BookingsCount = _context.Bookings.Count(b => b.CustomerId == c.Id),
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync();

            return new CustomerListDto
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<CustomerResponseDto> UpdateCustomerAsync(int id, UpdateCustomerDto dto)
        {
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id);
            if (customer == null)
                throw new InvalidOperationException("Customer not found.");

            customer.FullName = dto.FullName.Trim();
            customer.FatherName = string.IsNullOrWhiteSpace(dto.FatherName) ? null : dto.FatherName.Trim();
            customer.Phone = NormalizePhone(dto.Phone);
            customer.CNIC = string.IsNullOrWhiteSpace(dto.CNIC) ? null : dto.CNIC.Trim();
            customer.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant();
            customer.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
            customer.Status = dto.Status;
            customer.SourceNotes = string.IsNullOrWhiteSpace(dto.SourceNotes) ? null : dto.SourceNotes.Trim();
            customer.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            customer.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var bookingsCount = await _context.Bookings.CountAsync(b => b.CustomerId == id);
            return Map(customer, bookingsCount);
        }

        public async Task<CustomerResolution> FindOrCreateCustomerAsync(
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
            int? linkUserId = null)
        {
            var normalizedPhone = NormalizePhone(phone);
            var normalizedCnic = string.IsNullOrWhiteSpace(cnic) ? null : cnic.Trim();
            var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

            Customer? existing = null;
            var matchedByStrongIdentifier = false;

            if (normalizedCnic != null)
            {
                existing = await _context.Customers.FirstOrDefaultAsync(c => c.CNIC == normalizedCnic);
                matchedByStrongIdentifier = existing != null;
            }

            if (existing == null)
                existing = await _context.Customers.FirstOrDefaultAsync(c => c.Phone == normalizedPhone);

            if (existing == null && normalizedEmail != null)
            {
                existing = await _context.Customers.FirstOrDefaultAsync(c => c.Email == normalizedEmail);
                matchedByStrongIdentifier = existing != null;
            }

            if (existing != null)
            {
                // A blocked customer must not silently re-enter the pipeline through
                // "new customer" details that match their record.
                if (existing.Status == CustomerStatus.Blocked)
                    throw new InvalidOperationException(
                        "These details match a blocked customer. The booking cannot proceed.");

                // Link this customer to the login account if it isn't already, so their
                // bookings surface under "My Projects" regardless of the email they typed.
                // Only link on a CNIC or email match — a phone-only match (shared family
                // number, typo) must not expose another person's bookings to this login.
                if (linkUserId.HasValue && existing.UserId == null && matchedByStrongIdentifier)
                {
                    existing.UserId = linkUserId.Value;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
                return new CustomerResolution(existing.Id, WasCreated: false);
            }

            var customer = new Customer
            {
                UserId = linkUserId,
                FullName = fullName.Trim(),
                FatherName = string.IsNullOrWhiteSpace(fatherName) ? null : fatherName.Trim(),
                Phone = normalizedPhone,
                CNIC = normalizedCnic,
                Email = normalizedEmail,
                Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
                DateOfBirth = dateOfBirth,
                Nationality = string.IsNullOrWhiteSpace(nationality) ? null : nationality.Trim(),
                Occupation = string.IsNullOrWhiteSpace(occupation) ? null : occupation.Trim(),
                Whatsapp = string.IsNullOrWhiteSpace(whatsapp) ? null : whatsapp.Trim(),
                Source = source,
                SourceNotes = string.IsNullOrWhiteSpace(sourceNotes) ? null : sourceNotes.Trim(),
                Status = CustomerStatus.Active,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            return new CustomerResolution(customer.Id, WasCreated: true);
        }

        // "0300-1234567", "0300 1234567" and "03001234567" must all match the same
        // customer, so strip everything except digits (and a leading +).
        private static string NormalizePhone(string phone)
        {
            var trimmed = phone.Trim();
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            return trimmed.StartsWith('+') ? "+" + digits : digits;
        }

        private static CustomerResponseDto Map(Customer c, int bookingsCount)
        {
            return new CustomerResponseDto
            {
                Id = c.Id,
                FullName = c.FullName,
                FatherName = c.FatherName,
                Phone = c.Phone,
                CNIC = c.CNIC,
                Email = c.Email,
                Address = c.Address,
                Source = c.Source,
                SourceNotes = c.SourceNotes,
                Status = c.Status,
                UserId = c.UserId,
                Notes = c.Notes,
                BookingsCount = bookingsCount,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            };
        }
    }
}
