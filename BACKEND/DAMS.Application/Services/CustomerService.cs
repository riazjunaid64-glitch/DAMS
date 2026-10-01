using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
            // The duplicate check and the insert are one serializable unit, so two requests with the same
            // phone or CNIC cannot both pass the check; the loser gets the same 409 as any duplicate.
            var customer = await RunSerializableAsync(async () =>
            {
                await EnsureUniquePhoneAndCnicAsync(dto.Phone, dto.CNIC, excludeCustomerId: null);
                var created = BuildCustomer(dto, createdByUserId);
                _context.Customers.Add(created);
                await SaveNewCustomerWithDocumentsAsync(created,
                    createdByUserId.HasValue
                        ? new CustomerDocumentActor(createdByUserId.Value, createdByName ?? "Admin")
                        : null);
                return created;
            });

            return await BuildResponseAsync(customer);
        }

        private static Customer BuildCustomer(CreateCustomerDto dto, int? createdByUserId)
        {
            var customer = new Customer
            {
                FullName = dto.FullName.Trim(),
                FatherName = string.IsNullOrWhiteSpace(dto.FatherName) ? null : dto.FatherName.Trim(),
                CNIC = string.IsNullOrWhiteSpace(dto.CNIC) ? null : dto.CNIC.Trim(),
                Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant(),
                Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim(),
                DateOfBirth = dto.DateOfBirth,
                Nationality = string.IsNullOrWhiteSpace(dto.Nationality) ? null : dto.Nationality.Trim(),
                Occupation = string.IsNullOrWhiteSpace(dto.Occupation) ? null : dto.Occupation.Trim(),
                Whatsapp = string.IsNullOrWhiteSpace(dto.Whatsapp) ? null : dto.Whatsapp.Trim(),
                // Customers created here are always walk-ins; the caller does not choose.
                Source = CustomerSource.WalkIn,
                SourceNotes = string.IsNullOrWhiteSpace(dto.SourceNotes) ? null : dto.SourceNotes.Trim(),
                Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
                Status = CustomerStatus.Active,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow
            };
            ApplyPhone(customer, dto.Phone);
            return customer;
        }

        /// <summary>
        /// Runs one unit under Serializable isolation through the retrying execution strategy (a bare
        /// transaction is refused when retry-on-failure is on). A deadlock between two racers is
        /// retried, and the retry then sees the winner's row. In-memory databases and callers that
        /// already hold a transaction run it as is.
        /// </summary>
        private async Task<T> RunSerializableAsync<T>(Func<Task<T>> work)
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null)
                return await work();
            return await ExecuteRetryingSerializableAsync(work);
        }

        /// <summary>
        /// The strategy retries this delegate on the same context. A failed save leaves the customer
        /// and document rows it added still tracked, and the next save would insert them again beside
        /// the retry's own rows. Only what this attempt tracked is dropped, so a caller's earlier
        /// entities stay.
        /// </summary>
        private async Task<T> ExecuteRetryingSerializableAsync<T>(Func<Task<T>> work)
        {
            var attempt = 0;
            HashSet<object>? trackedBefore = null;
            return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (attempt++ > 0)
                    DetachEntitiesTrackedSince(trackedBefore);
                trackedBefore = _context.ChangeTracker.Entries().Select(entry => entry.Entity).ToHashSet();

                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var result = await work();
                await transaction.CommitAsync();
                return result;
            });
        }

        public async Task<CustomerResponseDto?> GetCustomerByIdAsync(int id)
        {
            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (customer == null)
                return null;

            return await BuildResponseAsync(customer);
        }

        private const int MinCnicSearchDigits = 5;

        public async Task<CustomerListDto> GetCustomersAsync(CustomerFilterDto filter)
        {
            var query = ApplyListFilters(_context.Customers.AsNoTracking(), filter);

            // Summary follows search (and any legacy source/status filters) but not the card filter.
            // Counts and the page are all computed in SQL; only the requested page is read.
            var totalCustomers = await query.CountAsync();
            var needing = query.Where(c => _context.CustomerDocumentRequirements.Any(r =>
                r.CustomerId == c.Id && r.IsRequired && r.Status == CustomerDocumentStatus.Needed));
            var documentsNeededCount = await needing.CountAsync();

            var listQuery = filter.DocumentsNeededOnly ? needing : query;
            var totalCount = filter.DocumentsNeededOnly ? documentsNeededCount : totalCustomers;

            var page = filter.Page < 1 ? 1 : filter.Page;
            var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;

            // Newest first; page after the card filter so summaries stay stable when a card is clicked.
            var pageIds = await listQuery
                .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => c.Id)
                .ToListAsync();

            var neededByCustomer = pageIds.Count == 0
                ? new Dictionary<int, int>()
                : await LoadDocumentsNeededByCustomerAsync(pageIds);

            var pageCustomers = pageIds.Count == 0
                ? []
                : await _context.Customers.AsNoTracking()
                    .Where(c => pageIds.Contains(c.Id))
                    .ToListAsync();
            var byId = pageCustomers.ToDictionary(c => c.Id);

            var bookingCounts = pageIds.Count == 0
                ? new Dictionary<int, int>()
                : await _context.Bookings.AsNoTracking()
                    .Where(b => pageIds.Contains(b.CustomerId))
                    .GroupBy(b => b.CustomerId)
                    .Select(g => new { CustomerId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.CustomerId, x => x.Count);

            var items = pageIds
                .Where(id => byId.ContainsKey(id))
                .Select(id =>
                {
                    var c = byId[id];
                    return new CustomerListItemDto
                    {
                        Id = c.Id,
                        FullName = c.FullName,
                        Phone = c.Phone,
                        CNIC = c.CNIC,
                        IsBlocked = c.Status == CustomerStatus.Blocked,
                        BookingsCount = bookingCounts.GetValueOrDefault(id),
                        DocumentsNeeded = neededByCustomer.GetValueOrDefault(id)
                    };
                })
                .ToList();

            return new CustomerListDto
            {
                Items = items,
                TotalCount = totalCount,
                TotalCustomers = totalCustomers,
                DocumentsNeededCount = documentsNeededCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<CustomerResponseDto> UpdateCustomerAsync(int id, UpdateCustomerDto dto)
        {
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id);
            if (customer == null)
                throw new InvalidOperationException("Customer not found.");

            var phoneForCheck = dto.WasProvided(nameof(dto.Phone)) ? dto.Phone : customer.Phone;
            var cnicForCheck = dto.WasProvided(nameof(dto.CNIC)) ? dto.CNIC : customer.CNIC;
            if (dto.WasProvided(nameof(dto.Phone)) || dto.WasProvided(nameof(dto.CNIC)))
                await EnsureUniquePhoneAndCnicAsync(phoneForCheck, cnicForCheck, excludeCustomerId: id);

            if (dto.WasProvided(nameof(dto.FullName))) customer.FullName = dto.FullName.Trim();
            if (dto.WasProvided(nameof(dto.FatherName))) customer.FatherName = string.IsNullOrWhiteSpace(dto.FatherName) ? null : dto.FatherName.Trim();
            if (dto.WasProvided(nameof(dto.Phone))) ApplyPhone(customer, dto.Phone);
            if (dto.WasProvided(nameof(dto.CNIC))) customer.CNIC = string.IsNullOrWhiteSpace(dto.CNIC) ? null : dto.CNIC.Trim();
            if (dto.WasProvided(nameof(dto.Email))) customer.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant();
            if (dto.WasProvided(nameof(dto.Address))) customer.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
            if (dto.WasProvided(nameof(dto.Whatsapp))) customer.Whatsapp = string.IsNullOrWhiteSpace(dto.Whatsapp) ? null : dto.Whatsapp.Trim();
            if (dto.WasProvided(nameof(dto.DateOfBirth))) customer.DateOfBirth = dto.DateOfBirth;
            if (dto.WasProvided(nameof(dto.Nationality))) customer.Nationality = string.IsNullOrWhiteSpace(dto.Nationality) ? null : dto.Nationality.Trim();
            if (dto.WasProvided(nameof(dto.Occupation))) customer.Occupation = string.IsNullOrWhiteSpace(dto.Occupation) ? null : dto.Occupation.Trim();
            if (dto.WasProvided(nameof(dto.SourceNotes))) customer.SourceNotes = string.IsNullOrWhiteSpace(dto.SourceNotes) ? null : dto.SourceNotes.Trim();
            if (dto.WasProvided(nameof(dto.Notes))) customer.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            customer.UpdatedAt = DateTime.UtcNow;
            // Customer.UserId is absent from the list above, and that omission is the rule rather
            // than an oversight: contact details describe how to reach somebody, ownership
            // describes whose bookings these are. Correcting a typo in an email address must not
            // move a customer's payment history to whoever else holds the new address, and must
            // not take it away from the login that has always owned it.

            await _context.SaveChangesAsync();

            return await BuildResponseAsync(customer);
        }

        public async Task<CustomerResponseDto> BlockCustomerAsync(int id, string reason, int? byUserId)
        {
            var trimmed = reason?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                throw new InvalidOperationException("Enter the reason for blocking this customer.");
            if (trimmed.Length > 500)
                throw new InvalidOperationException("The reason can be at most 500 characters.");

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new InvalidOperationException("Customer not found.");
            if (customer.Status == CustomerStatus.Blocked)
                throw new InvalidOperationException("This customer is already blocked.");

            var now = DateTime.UtcNow;
            customer.Status = CustomerStatus.Blocked;
            customer.BlockedReason = trimmed;
            customer.BlockedAt = now;
            customer.BlockedByUserId = byUserId;
            customer.UpdatedAt = now;
            _context.CustomerStatusLogs.Add(new CustomerStatusLog
            {
                CustomerId = id, Action = CustomerStatusAction.Blocked, Reason = trimmed, ByUserId = byUserId, At = now
            });
            await _context.SaveChangesAsync();
            return await BuildResponseAsync(customer);
        }

        public async Task<CustomerResponseDto> UnblockCustomerAsync(int id, int? byUserId)
        {
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new InvalidOperationException("Customer not found.");
            if (customer.Status != CustomerStatus.Blocked)
                throw new InvalidOperationException("This customer is not blocked.");

            var now = DateTime.UtcNow;
            customer.Status = CustomerStatus.Active;
            customer.BlockedReason = null;
            customer.BlockedAt = null;
            customer.BlockedByUserId = null;
            customer.UpdatedAt = now;
            _context.CustomerStatusLogs.Add(new CustomerStatusLog
            {
                CustomerId = id, Action = CustomerStatusAction.Unblocked, ByUserId = byUserId, At = now
            });
            await _context.SaveChangesAsync();
            return await BuildResponseAsync(customer);
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
                return await ExecuteRetryingSerializableAsync(() => FindOrCreateCustomerCoreAsync(
                    fullName, phone, cnic, email, address, source, sourceNotes, createdByUserId,
                    fatherName, dateOfBirth, nationality, occupation, whatsapp));
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
            var nationalPhone = LeadContactNormalizer.NormalizePhoneOrNull(phone);
            var normalizedCnic = string.IsNullOrWhiteSpace(cnic) ? null : cnic.Trim();
            var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
            var cnicDigits = CustomerIdentityNormalizer.DigitsOnlyOrNull(normalizedCnic);

            // Deduplication only. Which record this is, never whose it is — see the comment on the
            // match below.
            Customer? existing = null;
            if (cnicDigits != null)
            {
                existing = await _context.Customers
                    .FirstOrDefaultAsync(c =>
                        c.CNIC != null &&
                        c.CNIC.Replace("-", "").Replace(" ", "") == cnicDigits);
            }

            if (existing == null && nationalPhone != null)
            {
                // Match the national number, not the display phone. "03001234567", "+92 300 1234567",
                // "923001234567" and "00923001234567" are one subscriber. More than one customer
                // with that number is a choice, not a guess.
                var phoneMatches = await _context.Customers
                    .Where(c => c.NormalizedPhone == nationalPhone)
                    .OrderBy(c => c.Id)
                    .Take(2)
                    .ToListAsync();

                if (phoneMatches.Count > 1)
                    throw new InvalidOperationException(
                        "More than one customer has this phone, choose the customer.");

                existing = phoneMatches.Count == 1 ? phoneMatches[0] : null;
                if (existing != null)
                    EnsureWeakMatchDoesNotConflict(existing, normalizedCnic, normalizedEmail, matchedByPhone: true);
            }

            if (existing == null && normalizedEmail != null)
            {
                existing = await _context.Customers.FirstOrDefaultAsync(c => c.Email == normalizedEmail);
                if (existing != null)
                    EnsureWeakMatchDoesNotConflict(existing, normalizedCnic, normalizedEmail, matchedByPhone: false);
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
            ApplyPhone(customer, phone);

            _context.Customers.Add(customer);
            await SaveNewCustomerWithDocumentsAsync(customer, null);

            return new CustomerResolution(customer.Id, WasCreated: true);
        }

        private async Task EnsureUniquePhoneAndCnicAsync(string? phone, string? cnic, int? excludeCustomerId)
        {
            var nationalPhone = LeadContactNormalizer.NormalizePhoneOrNull(phone);
            if (nationalPhone != null && nationalPhone.Length >= LeadContactNormalizer.MinUsablePhoneDigits)
            {
                var match = await _context.Customers.AsNoTracking()
                    .Where(c => c.NormalizedPhone == nationalPhone
                                && (!excludeCustomerId.HasValue || c.Id != excludeCustomerId.Value))
                    .OrderBy(c => c.Id)
                    .Select(c => new { c.Id, c.FullName })
                    .FirstOrDefaultAsync();
                if (match != null)
                    throw CustomerConflictException.Phone(match.Id, match.FullName);
            }

            var cnicDigits = CustomerIdentityNormalizer.DigitsOnlyOrNull(cnic);
            if (cnicDigits != null)
            {
                var match = await _context.Customers.AsNoTracking()
                    .Where(c => c.CNIC != null
                                && c.CNIC.Replace("-", "").Replace(" ", "") == cnicDigits
                                && (!excludeCustomerId.HasValue || c.Id != excludeCustomerId.Value))
                    .OrderBy(c => c.Id)
                    .Select(c => new { c.Id, c.FullName })
                    .FirstOrDefaultAsync();
                if (match != null)
                    throw CustomerConflictException.Cnic(match.Id, match.FullName);
            }
        }

        private async Task<Dictionary<int, int>> LoadDocumentsNeededByCustomerAsync(IReadOnlyList<int> customerIds)
        {
            var rows = await _context.CustomerDocumentRequirements.AsNoTracking()
                .Where(r => customerIds.Contains(r.CustomerId) && r.IsRequired)
                .Select(r => new { r.CustomerId, r.Status })
                .ToListAsync();

            return rows
                .GroupBy(r => r.CustomerId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Count(r => CustomerDocumentsNeeded.IsNeeded(r.Status)));
        }

        private static IQueryable<Customer> ApplyListFilters(
            IQueryable<Customer> query,
            CustomerFilterDto filter)
        {
            if (filter.Source.HasValue)
                query = query.Where(c => c.Source == filter.Source.Value);

            if (filter.Status.HasValue)
                query = query.Where(c => c.Status == filter.Status.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var search = filter.SearchTerm.Trim();
                var term = search.ToLower();
                // Something a person typed as a phone number or CNIC: digits with spaces, dashes,
                // brackets or a plus. "0300-1234567", "0300 1234567", "03001234567" and "+92 300 1234567"
                // all reduce to the same national number, and a CNIC matches with or without dashes.
                var numeric = search.All(c => char.IsDigit(c) || c is ' ' or '-' or '+' or '(' or ')');
                var phoneDigits = numeric ? LeadContactNormalizer.NormalizePhone(search) : string.Empty;
                var cnicDigits = numeric ? CustomerIdentityNormalizer.DigitsOnly(search) : string.Empty;
                var matchPhone = phoneDigits.Length >= LeadContactNormalizer.MinUsablePhoneDigits;
                var matchCnic = cnicDigits.Length >= MinCnicSearchDigits;
                query = query.Where(c =>
                    c.FullName.ToLower().Contains(term) ||
                    (matchPhone && c.NormalizedPhone != null && c.NormalizedPhone.Contains(phoneDigits)) ||
                    (matchCnic && c.CNIC != null && c.CNIC.Replace("-", "").Replace(" ", "").Contains(cnicDigits)) ||
                    // Non-numeric search still finds a phone/CNIC typed as a fragment of the display form.
                    (!matchPhone && !matchCnic && (
                        c.Phone.Contains(term) ||
                        (c.CNIC != null && c.CNIC.ToLower().Contains(term)))));
            }

            return query;
        }

        /// <summary>
        /// Writes the customer and the documents whose switch is on in one save. If the document
        /// rows cannot be written and the transaction is still usable, the customer is saved alone
        /// and the background job fills the gap. A deadlock, any other transient failure, or an
        /// aborted transaction is left for <see cref="RunSerializableAsync"/> to retry; saving
        /// again on that transaction is what raised "Cannot issue SAVE TRANSACTION when there is
        /// no active transaction."
        /// </summary>
        private async Task SaveNewCustomerWithDocumentsAsync(Customer customer, CustomerDocumentActor? actor)
        {
            var prepared = false;
            try
            {
                await CustomerDocumentAssignment.AddDefaultsForNewCustomerAsync(_context, customer, actor);
                prepared = true;
                await _context.SaveChangesAsync();
                return;
            }
            catch (Exception ex) when (CanSaveCustomerWithoutDocuments(ex))
            {
                if (prepared)
                    DetachAdvisoryDocuments(customer);
                _logger.LogWarning(ex, prepared
                    ? "Customer was created without its document defaults; reconciliation will retry."
                    : "Document defaults could not be prepared; the customer is saved without them.");
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            {
                _context.Entry(customer).State = EntityState.Detached;
                throw;
            }
        }

        /// <summary>
        /// True only for a document failure that left this transaction committable. Transient SQL
        /// errors are the execution strategy's to retry, and a transaction SQL Server has already
        /// aborted cannot take another save.
        /// </summary>
        private bool CanSaveCustomerWithoutDocuments(Exception exception)
        {
            if (IsTransientDatabaseFailure(exception))
                return false;
            return TransactionRemainsUsable();
        }

        private bool IsTransientDatabaseFailure(Exception exception)
        {
            if (!_context.Database.IsRelational())
                return false;

            // The same rule the context's retrying strategy uses, including error numbers it was
            // told to add. ShouldRetryOn is the supported extension point for that rule.
            var configured = _context.Database.CreateExecutionStrategy() as SqlServerRetryingExecutionStrategy;
            var probe = new TransientFailureProbe(_context, configured?.AdditionalErrorNumbers?.ToArray());
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (probe.IsTransient(current))
                    return true;
            }
            return false;
        }

        /// <summary>Drops entities this attempt started tracking, and unhooks them from anything that stays.</summary>
        private void DetachEntitiesTrackedSince(HashSet<object>? alreadyTracked)
        {
            var created = _context.ChangeTracker.Entries()
                .Where(entry => alreadyTracked == null || !alreadyTracked.Contains(entry.Entity))
                .Select(entry => entry.Entity)
                .ToList();
            if (created.Count == 0)
                return;

            var dropping = created.ToHashSet();
            foreach (var audit in created.OfType<CustomerDocumentAuditEntry>())
            {
                if (audit.Requirement != null && !dropping.Contains(audit.Requirement))
                    audit.Requirement.AuditEntries.Remove(audit);
            }
            foreach (var requirement in created.OfType<CustomerDocumentRequirement>())
            {
                if (requirement.Customer != null && !dropping.Contains(requirement.Customer))
                    requirement.Customer.DocumentRequirements.Remove(requirement);
                if (requirement.Category != null && !dropping.Contains(requirement.Category))
                    requirement.Category.Requirements.Remove(requirement);
            }

            foreach (var entity in created.OfType<CustomerDocumentAuditEntry>())
                Detach(entity);
            foreach (var entity in created.OfType<CustomerDocumentRequirement>())
                Detach(entity);
            foreach (var entity in created.OfType<Customer>())
                Detach(entity);
            foreach (var entity in created)
                Detach(entity);
        }

        private void Detach(object entity)
        {
            var entry = _context.Entry(entity);
            if (entry.State != EntityState.Detached)
                entry.State = EntityState.Detached;
        }

        /// <summary>
        /// <see cref="SqlServerRetryingExecutionStrategy.ShouldRetryOn"/> is protected. This is how
        /// a failure the strategy would retry is recognised without the internal detector.
        /// </summary>
        private sealed class TransientFailureProbe : SqlServerRetryingExecutionStrategy
        {
            public TransientFailureProbe(DbContext context, IEnumerable<int>? additionalErrorNumbers)
                : base(context, 1, TimeSpan.Zero, additionalErrorNumbers ?? [])
            {
            }

            public bool IsTransient(Exception exception) => ShouldRetryOn(exception);
        }

        /// <summary>
        /// Asks SQL Server directly. Going through EF would try to take a savepoint, which is the
        /// command that fails once the transaction is already gone.
        /// </summary>
        private bool TransactionRemainsUsable()
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction == null)
                return true;

            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                    return false;
                using var command = connection.CreateCommand();
                command.Transaction = _context.Database.CurrentTransaction.GetDbTransaction();
                command.CommandText = "SELECT XACT_STATE()";
                // 1 is committable. 0 means no transaction, -1 means it can only be rolled back.
                return Convert.ToInt32(command.ExecuteScalar()) == 1;
            }
            catch
            {
                return false;
            }
        }

        private void DetachAdvisoryDocuments(Customer customer)
        {
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
        }

        private static void EnsureWeakMatchDoesNotConflict(
            Customer existing, string? normalizedCnic, string? normalizedEmail, bool matchedByPhone)
        {
            if (normalizedCnic != null &&
                !string.IsNullOrWhiteSpace(existing.CNIC) &&
                !string.Equals(
                    CustomerIdentityNormalizer.DigitsOnly(existing.CNIC),
                    CustomerIdentityNormalizer.DigitsOnly(normalizedCnic),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(matchedByPhone
                    ? "These details match an existing customer by phone, but the CNIC is different. Choose the customer explicitly or create a separate record."
                    : "This email belongs to another customer.");
            }

            // An email match already proved the addresses are the same, so only a phone match
            // can still be holding a different address.
            if (!matchedByPhone)
                return;

            if (normalizedEmail != null &&
                !string.IsNullOrWhiteSpace(existing.Email) &&
                !string.Equals(existing.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "These details match an existing customer by phone, but the email is different. Choose the customer explicitly or create a separate record.");
            }
        }

        // Display form kept on Customer.Phone. The match key is NormalizedPhone.
        private static void ApplyPhone(Customer customer, string phone)
        {
            customer.Phone = NormalizePhone(phone);
            customer.NormalizedPhone = LeadContactNormalizer.NormalizePhoneOrNull(phone);
        }

        // Display form stored on Phone. "0300-1234567" and "0300 1234567" become the same
        // digits, and a leading + is kept. Matching uses NormalizedPhone, not this string.
        private static string NormalizePhone(string phone)
        {
            var trimmed = phone.Trim();
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            return trimmed.StartsWith('+') ? "+" + digits : digits;
        }

        private async Task<CustomerResponseDto> BuildResponseAsync(Customer c)
        {
            var bookingsCount = await _context.Bookings.CountAsync(b => b.CustomerId == c.Id);
            var needed = (await LoadDocumentsNeededByCustomerAsync([c.Id])).GetValueOrDefault(c.Id);
            var blockedByName = c.BlockedByUserId.HasValue
                ? await _context.Users.AsNoTracking()
                    .Where(u => u.UserId == c.BlockedByUserId.Value)
                    .Select(u => u.FullName)
                    .FirstOrDefaultAsync()
                : null;

            return new CustomerResponseDto
            {
                Id = c.Id,
                FullName = c.FullName,
                FatherName = c.FatherName,
                Phone = c.Phone,
                CNIC = c.CNIC,
                Email = c.Email,
                Address = c.Address,
                DateOfBirth = c.DateOfBirth,
                Nationality = c.Nationality,
                Occupation = c.Occupation,
                Whatsapp = c.Whatsapp,
                Source = c.Source,
                SourceNotes = c.SourceNotes,
                Status = c.Status,
                BlockedReason = c.BlockedReason,
                BlockedByName = blockedByName,
                BlockedAt = c.BlockedAt,
                UserId = c.UserId,
                Notes = c.Notes,
                BookingsCount = bookingsCount,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                DocumentsNeeded = needed
            };
        }
    }
}
