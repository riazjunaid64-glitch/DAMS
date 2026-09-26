using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;
using DAMS.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAMS.Application.Services
{
    public class CustomerService : ICustomerService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CustomerService> _logger;

        public CustomerService(AppDbContext context)
            : this(context, NullLogger<CustomerService>.Instance)
        {
        }

        public CustomerService(AppDbContext context, ILogger<CustomerService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<CustomerResponseDto> CreateCustomerAsync(
            CreateCustomerDto dto,
            int? createdByUserId,
            string? createdByName = null)
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
            await TryAssignDefaultDocumentsAsync(customer,
                createdByUserId.HasValue
                    ? new CustomerDocumentActor(createdByUserId.Value, createdByName ?? "Admin")
                    : null);

            return Map(customer, 0, CustomerDocumentCompletion.Calculate(customer.DocumentRequirements));
        }

        public async Task<CustomerResponseDto?> GetCustomerByIdAsync(int id)
        {
            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (customer == null)
                return null;

            var bookingsCount = await _context.Bookings.CountAsync(b => b.CustomerId == id);
            var requirements = await _context.CustomerDocumentRequirements
                .AsNoTracking()
                .Where(r => r.CustomerId == id)
                .ToListAsync();
            return Map(customer, bookingsCount, CustomerDocumentCompletion.Calculate(requirements));
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

            var customerIds = items.Select(i => i.Id).ToArray();
            var requirementRows = customerIds.Length == 0
                ? []
                : await _context.CustomerDocumentRequirements.AsNoTracking()
                    .Where(r => customerIds.Contains(r.CustomerId))
                    .Select(r => new CustomerDocumentRequirement
                    {
                        CustomerId = r.CustomerId,
                        IsRequired = r.IsRequired,
                        Status = r.Status,
                        // Needed by CustomerDocumentCompletion.Calculate to compute PostponedDue;
                        // omitting it made the list badge silently under-report overdue postponements
                        // versus the detail/checklist views that share the same rule.
                        PostponedUntil = r.PostponedUntil
                    })
                    .ToListAsync();
            var summaries = requirementRows
                .GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => CustomerDocumentCompletion.Calculate(g));
            foreach (var item in items)
                item.DocumentSummary = summaries.GetValueOrDefault(item.Id)
                    ?? CustomerDocumentCompletion.Calculate([]);

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

            if (dto.WasProvided(nameof(dto.FullName))) customer.FullName = dto.FullName.Trim();
            if (dto.WasProvided(nameof(dto.FatherName))) customer.FatherName = string.IsNullOrWhiteSpace(dto.FatherName) ? null : dto.FatherName.Trim();
            if (dto.WasProvided(nameof(dto.Phone))) customer.Phone = NormalizePhone(dto.Phone);
            if (dto.WasProvided(nameof(dto.CNIC))) customer.CNIC = string.IsNullOrWhiteSpace(dto.CNIC) ? null : dto.CNIC.Trim();
            if (dto.WasProvided(nameof(dto.Email))) customer.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant();
            if (dto.WasProvided(nameof(dto.Address))) customer.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
            if (dto.WasProvided(nameof(dto.Status))) customer.Status = dto.Status;
            if (dto.WasProvided(nameof(dto.SourceNotes))) customer.SourceNotes = string.IsNullOrWhiteSpace(dto.SourceNotes) ? null : dto.SourceNotes.Trim();
            if (dto.WasProvided(nameof(dto.Notes))) customer.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            customer.UpdatedAt = DateTime.UtcNow;
            // Customer.UserId is absent from the list above, and that omission is the rule rather
            // than an oversight: contact details describe how to reach somebody, ownership
            // describes whose bookings these are. Correcting a typo in an email address must not
            // move a customer's payment history to whoever else holds the new address, and must
            // not take it away from the login that has always owned it.

            await _context.SaveChangesAsync();

            var bookingsCount = await _context.Bookings.CountAsync(b => b.CustomerId == id);
            var requirements = await _context.CustomerDocumentRequirements.AsNoTracking()
                .Where(r => r.CustomerId == id)
                .ToListAsync();
            return Map(customer, bookingsCount, CustomerDocumentCompletion.Calculate(requirements));
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
            string? whatsapp = null)
        {
            if (_context.Database.CurrentTransaction == null)
            {
                // Wrapped in an execution strategy because the DbContext has retry-on-failure
                // enabled, which is incompatible with a bare BeginTransactionAsync.
                var strategy = _context.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                    var resolution = await FindOrCreateCustomerCoreAsync(
                        fullName, phone, cnic, email, address, source, sourceNotes, createdByUserId,
                        fatherName, dateOfBirth, nationality, occupation, whatsapp);
                    await transaction.CommitAsync();
                    return resolution;
                });
            }

            return await FindOrCreateCustomerCoreAsync(
                fullName, phone, cnic, email, address, source, sourceNotes, createdByUserId,
                fatherName, dateOfBirth, nationality, occupation, whatsapp);
        }

        private async Task<CustomerResolution> FindOrCreateCustomerCoreAsync(
            string fullName,
            string phone,
            string? cnic,
            string? email,
            string? address,
            CustomerSource source,
            string? sourceNotes,
            int? createdByUserId,
            string? fatherName,
            DateTime? dateOfBirth,
            string? nationality,
            string? occupation,
            string? whatsapp)
        {
            var normalizedPhone = NormalizePhone(phone);
            var normalizedCnic = string.IsNullOrWhiteSpace(cnic) ? null : cnic.Trim();
            var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

            // Deduplication only. Which record this is, never whose it is — see the comment on the
            // match below.
            Customer? existing = null;
            if (normalizedCnic != null)
                existing = await _context.Customers.FirstOrDefaultAsync(c => c.CNIC == normalizedCnic);

            if (existing == null)
            {
                existing = await _context.Customers.FirstOrDefaultAsync(c => c.Phone == normalizedPhone);

                if (existing != null)
                    EnsureWeakMatchDoesNotConflict(existing, normalizedCnic, normalizedEmail);
            }

            if (existing == null && normalizedEmail != null)
            {
                existing = await _context.Customers.FirstOrDefaultAsync(c => c.Email == normalizedEmail);
            }

            if (existing != null)
            {
                // A blocked customer must not silently re-enter the pipeline through
                // "new customer" details that match their record.
                if (existing.Status == CustomerStatus.Blocked)
                    throw new InvalidOperationException(
                        "These details match a blocked customer. The booking cannot proceed.");

                // An existing record is NOT claimed here, whatever matched.
                //
                // This used to attach linkUserId to any unowned customer found by CNIC or email,
                // on the reasoning that a strong identifier match means it is the same person. It
                // does not. It means somebody typed a value that is also on file — and a CNIC is
                // printed on documents, shared with agents and photocopied at every office, while
                // an email address is simply whatever the form said. Neither is a secret, so
                // neither can be the thing that hands a login somebody's payment history.
                //
                // Matching still does its real job above: it stops DAMS creating a duplicate CRM
                // record. Deciding who owns that record is a different question with a different
                // standard of proof, and it belongs to ICustomerAccountLinkService.
                return new CustomerResolution(existing.Id, WasCreated: false);
            }

            var customer = new Customer
            {
                // Deliberately unowned. A brand-new CRM record is not evidence about who may read
                // it later; ICustomerAccountLinkService attaches a login when, and only when, one of
                // its three trusted paths says so.
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
            await TryAssignDefaultDocumentsAsync(customer, null);

            return new CustomerResolution(customer.Id, WasCreated: true);
        }

        private async Task TryAssignDefaultDocumentsAsync(Customer customer, CustomerDocumentActor? actor)
        {
            try
            {
                await CustomerDocumentAssignment.ReconcileCustomerAsync(_context, customer, actor);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Documents are advisory. A missing table during a rolling deployment, a transient
                // database failure, or an assignment conflict must never turn a committed customer
                // (and therefore a booking) into a failed core workflow. Remove failed tracked rows so
                // the scoped context can safely continue; the reconciliation worker retries later.
                var advisoryEntries = _context.ChangeTracker.Entries()
                             .Where(e => e.Entity is CustomerDocumentRequirement or CustomerDocumentAuditEntry)
                             .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                             .ToList();
                var failedRequirements = advisoryEntries.Select(e => e.Entity)
                    .OfType<CustomerDocumentRequirement>().ToList();
                foreach (var entry in advisoryEntries)
                    entry.State = EntityState.Detached;
                foreach (var requirement in failedRequirements)
                    customer.DocumentRequirements.Remove(requirement);

                _logger.LogWarning(ex,
                    "Customer {CustomerId} was created without its advisory document defaults; reconciliation will retry.",
                    customer.Id);
            }
        }

        private static void EnsureWeakMatchDoesNotConflict(
            Customer existing, string? normalizedCnic, string? normalizedEmail)
        {
            if (normalizedCnic != null &&
                !string.IsNullOrWhiteSpace(existing.CNIC) &&
                !string.Equals(existing.CNIC, normalizedCnic, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "These details match an existing customer by phone, but the CNIC is different. Choose the customer explicitly or create a separate record.");
            }

            if (normalizedEmail != null &&
                !string.IsNullOrWhiteSpace(existing.Email) &&
                !string.Equals(existing.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "These details match an existing customer by phone, but the email is different. Choose the customer explicitly or create a separate record.");
            }
        }

        // "0300-1234567", "0300 1234567" and "03001234567" must all match the same
        // customer, so strip everything except digits (and a leading +).
        private static string NormalizePhone(string phone)
        {
            var trimmed = phone.Trim();
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            return trimmed.StartsWith('+') ? "+" + digits : digits;
        }

        private static CustomerResponseDto Map(
            Customer c,
            int bookingsCount,
            DAMS.Application.DTOs.CustomerDocumentDtos.CustomerDocumentSummaryDto? documentSummary = null)
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
                UpdatedAt = c.UpdatedAt,
                DocumentSummary = documentSummary ?? CustomerDocumentCompletion.Calculate([])
            };
        }
    }
}
