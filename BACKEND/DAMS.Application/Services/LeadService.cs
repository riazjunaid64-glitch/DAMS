using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DAMS.Application.Services
{
    public class LeadService : ILeadService
    {
        private const string DefaultSourceCode = "manual";
        private const string WebsiteSourceCode = "website";
        private const string BookingRequestProvider = "dams_booking_request";

        private readonly AppDbContext _context;
        private readonly ICustomerService _customerService;
        private readonly IBookingService _bookingService;
        private readonly ILeadNotificationService _notifications;
        private readonly LeadAlertOptions _alertOptions;

        public LeadService(
            AppDbContext context,
            ICustomerService customerService,
            IBookingService bookingService,
            ILeadNotificationService notifications,
            IOptions<LeadAlertOptions> alertOptions)
        {
            _context = context;
            _customerService = customerService;
            _bookingService = bookingService;
            _notifications = notifications;
            _alertOptions = alertOptions.Value;
        }

        // ── Ingestion ───────────────────────────────────────────────────────────────

        public async Task<LeadIntakeResultDto> IngestAsync(
            LeadIntakeDto dto, LeadUserContext? actor, CancellationToken cancellationToken = default)
        {
            var normalizedPhone = LeadContactNormalizer.NormalizePhone(dto.Phone);
            if (normalizedPhone.Length < 7)
                throw new InvalidOperationException("A usable phone number is required to create a lead.");

            var normalizedWhatsapp = LeadContactNormalizer.NormalizePhoneOrNull(dto.WhatsappNumber);
            var normalizedEmail = LeadContactNormalizer.NormalizeEmail(dto.Email);

            var source = await ResolveSourceAsync(dto.SourceCode, cancellationToken);

            // 1. Same external submission replayed — return what we already stored.
            var provider = LeadContactNormalizer.Clean(dto.ExternalProvider);
            var externalId = LeadContactNormalizer.Clean(dto.ExternalLeadId);
            if (provider != null && externalId != null)
            {
                var existingExternal = await _context.Leads
                    .AsNoTracking()
                    .Where(l => l.ExternalProvider == provider && l.ExternalLeadId == externalId)
                    .Select(l => l.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingExternal != 0)
                {
                    return new LeadIntakeResultDto
                    {
                        AlreadyIngested = true,
                        Message = "This submission had already been received; the existing lead was returned.",
                        Lead = await LoadResponseAsync(existingExternal, cancellationToken)
                    };
                }
            }

            // 2. Someone we already know?
            var match = await FindDuplicateAsync(normalizedPhone, normalizedWhatsapp, normalizedEmail, cancellationToken);

            if (match is { LeadId: not null })
            {
                if (!dto.AllowDuplicate)
                {
                    return new LeadIntakeResultDto
                    {
                        IsDuplicate = true,
                        Match = match,
                        Message = $"An open lead ({match.LeadReference}) already exists for this contact. " +
                                  "Enrich it instead, or resubmit with allowDuplicate to add this enquiry to it."
                    };
                }

                var enriched = await EnrichExistingLeadAsync(match.LeadId.Value, dto, source, actor, cancellationToken);
                return new LeadIntakeResultDto
                {
                    IsDuplicate = true,
                    EnrichedExisting = true,
                    Match = match,
                    Message = "An existing lead matched this contact and was enriched with the new enquiry.",
                    Lead = enriched
                };
            }

            var lead = BuildLead(dto, source, normalizedPhone, normalizedWhatsapp, normalizedEmail, actor);

            await ApplyInitialAssignmentAsync(lead, dto, actor, cancellationToken);

            _context.Leads.Add(lead);

            LeadTimeline.Record(_context, lead, LeadActivityType.LeadCreated,
                $"Lead created from {source.Name}.", actor, a => a.Notes = dto.Notes);
            LeadTimeline.Record(_context, lead, LeadActivityType.SourceRecorded,
                $"Source recorded: {source.Name}.", actor,
                a => a.NewValue = BuildSourceTrail(dto, source));

            try
            {
                await SaveNewLeadAsync(lead, cancellationToken);
            }
            catch (DbUpdateException ex) when (IsExternalDuplicate(ex))
            {
                // Two copies of the same webhook arrived at once; the index rejected the
                // loser. Discard only what this call tried to insert — a caller such as the
                // website request flow may still have unsaved changes of its own — then
                // return the winner instead of a 500.
                DetachPendingInserts();
                var winnerId = await _context.Leads
                    .AsNoTracking()
                    .Where(l => l.ExternalProvider == provider && l.ExternalLeadId == externalId)
                    .Select(l => l.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (winnerId == 0)
                    throw;

                return new LeadIntakeResultDto
                {
                    AlreadyIngested = true,
                    Message = "This submission had already been received; the existing lead was returned.",
                    Lead = await LoadResponseAsync(winnerId, cancellationToken)
                };
            }

            await NotifyNewLeadAsync(lead, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return new LeadIntakeResultDto
            {
                Match = match,
                Message = match?.CustomerId != null
                    ? $"Lead created. Note: an existing customer ({match.CustomerName}) matches these details."
                    : "Lead created.",
                Lead = await LoadResponseAsync(lead.Id, cancellationToken)
            };
        }

        private Lead BuildLead(
            LeadIntakeDto dto,
            LeadSource source,
            string normalizedPhone,
            string? normalizedWhatsapp,
            string? normalizedEmail,
            LeadUserContext? actor)
        {
            var externalProvider = LeadContactNormalizer.Clean(dto.ExternalProvider);

            return new Lead
            {
                FirstName = dto.FirstName.Trim(),
                LastName = LeadContactNormalizer.Clean(dto.LastName),
                Phone = dto.Phone.Trim(),
                NormalizedPhone = normalizedPhone,
                WhatsappNumber = LeadContactNormalizer.Clean(dto.WhatsappNumber),
                NormalizedWhatsapp = normalizedWhatsapp,
                Email = LeadContactNormalizer.NormalizeEmail(dto.Email),
                NormalizedEmail = normalizedEmail,
                Address = LeadContactNormalizer.Clean(dto.Address),
                City = LeadContactNormalizer.Clean(dto.City),
                PreferredContactMethod = dto.PreferredContactMethod,
                PreferredContactTime = LeadContactNormalizer.Clean(dto.PreferredContactTime),
                LeadSourceId = source.Id,
                Source = source,
                SourceDetails = LeadContactNormalizer.Clean(dto.SourceDetails),
                CampaignName = LeadContactNormalizer.Clean(dto.CampaignName),
                CampaignReference = LeadContactNormalizer.Clean(dto.CampaignReference),
                AdReference = LeadContactNormalizer.Clean(dto.AdReference),
                ExternalProvider = externalProvider,
                ExternalLeadId = LeadContactNormalizer.Clean(dto.ExternalLeadId),
                ExternalFormReference = LeadContactNormalizer.Clean(dto.ExternalFormReference),
                ExternalSubmittedAt = dto.ExternalSubmittedAt,
                IntegrationPayload = LeadContactNormalizer.Clean(dto.IntegrationPayload),
                IntegrationStatus = externalProvider == null
                    ? LeadIntegrationStatus.NotApplicable
                    : LeadIntegrationStatus.Processed,
                InterestedProjectId = dto.InterestedProjectId,
                InterestedUnitId = dto.InterestedUnitId,
                PropertyType = LeadContactNormalizer.Clean(dto.PropertyType),
                PreferredLocation = LeadContactNormalizer.Clean(dto.PreferredLocation),
                BudgetMin = dto.BudgetMin,
                BudgetMax = dto.BudgetMax,
                PurchaseIntent = dto.PurchaseIntent,
                Notes = LeadContactNormalizer.Clean(dto.Notes),
                Stage = LeadStage.New,
                AssignmentState = LeadAssignmentState.Unassigned,
                CreatedByUserId = actor?.UserId,
                CreatedAt = DateTime.UtcNow
            };
        }

        private async Task ApplyInitialAssignmentAsync(
            Lead lead, LeadIntakeDto dto, LeadUserContext? actor, CancellationToken cancellationToken)
        {
            var employeeAutoAssigned = false;

            // A sales employee capturing a walk-in or phone lead must retain access to the
            // record they just created. They cannot choose another owner, so DAMS assigns it
            // to their linked employee record automatically.
            if (actor?.IsEmployee == true &&
                dto.AssignedEmployeeId == null &&
                dto.AssignedTeamId == null)
            {
                if (!actor.EmployeeId.HasValue)
                    throw new InvalidOperationException(
                        "Your login is not linked to an employee record. Ask an Admin to complete staff setup.");

                dto.AssignedEmployeeId = actor.EmployeeId;
                dto.AssignedTeamId = actor.TeamId;
                employeeAutoAssigned = true;
            }

            if (dto.AssignedEmployeeId == null && dto.AssignedTeamId == null)
                return;

            if (actor == null ||
                (!actor.IsAdmin && !actor.IsManager &&
                 !(employeeAutoAssigned && actor.IsEmployee && dto.AssignedEmployeeId == actor.EmployeeId
                   && (dto.AssignedTeamId == null || dto.AssignedTeamId == actor.TeamId))))
                throw new LeadAuthorizationException("Only an admin or manager can assign a lead on creation.");

            if (dto.AssignedEmployeeId.HasValue)
            {
                var employee = await LoadAssignableEmployeeAsync(dto.AssignedEmployeeId.Value, actor, cancellationToken);
                lead.AssignedEmployeeId = employee.Id;
                lead.AssignedTeamId = dto.AssignedTeamId ?? employee.TeamId;
            }
            else
            {
                await EnsureTeamAssignableAsync(dto.AssignedTeamId!.Value, actor, cancellationToken);
                lead.AssignedTeamId = dto.AssignedTeamId;
            }

            lead.AssignmentState = LeadAssignmentState.Assigned;
            lead.AssignedAt = DateTime.UtcNow;
            lead.AssignedByUserId = actor.UserId;
            lead.Stage = LeadStage.FirstContactPending;

            _context.LeadAssignmentHistories.Add(new LeadAssignmentHistory
            {
                Lead = lead,
                AssignedEmployeeId = lead.AssignedEmployeeId,
                AssignedTeamId = lead.AssignedTeamId,
                Reason = "Assigned on creation.",
                AssignedByUserId = actor.UserId,
                AssignedByName = actor.DisplayName,
                AssignedAt = lead.AssignedAt.Value
            });

            LeadTimeline.Record(_context, lead, LeadActivityType.LeadAssigned,
                "Lead assigned on creation.", actor,
                a => a.NewValue = lead.AssignedEmployeeId?.ToString());
        }

        private async Task NotifyNewLeadAsync(Lead lead, CancellationToken cancellationToken)
        {
            var name = FullName(lead);
            await _notifications.QueueForSupervisorsAsync(
                lead,
                LeadNotificationType.NewLeadReceived,
                $"New lead: {name}",
                $"{name} arrived through {lead.Source?.Name ?? "an enquiry channel"}.",
                "created",
                cancellationToken: cancellationToken);

            if (lead.AssignedEmployeeId.HasValue)
                await NotifyOwnerAsync(lead, LeadNotificationType.LeadAssigned,
                    $"Lead assigned: {name}", "This lead is now yours to work.", "assigned",
                    cancellationToken);
        }

        private async Task NotifyOwnerAsync(
            Lead lead, LeadNotificationType type, string title, string? body, string suffix,
            CancellationToken cancellationToken, bool isEscalation = false)
        {
            if (lead.AssignedEmployeeId == null)
                return;

            var ownerUserId = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Id == lead.AssignedEmployeeId)
                .Select(e => e.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (ownerUserId == null)
                return;

            await _notifications.QueueAsync(lead.Id, ownerUserId.Value, type, title, body,
                $"{type}:{lead.Id}:{ownerUserId.Value}:{suffix}", isEscalation, cancellationToken);
        }

        private async Task<LeadResponseDto> EnrichExistingLeadAsync(
            int leadId, LeadIntakeDto dto, LeadSource source, LeadUserContext? actor, CancellationToken cancellationToken)
        {
            var lead = await _context.Leads.FirstAsync(l => l.Id == leadId, cancellationToken);

            // Only ever fill gaps. The original source, owner and history are untouchable.
            lead.LastName ??= LeadContactNormalizer.Clean(dto.LastName);
            lead.Address ??= LeadContactNormalizer.Clean(dto.Address);
            lead.City ??= LeadContactNormalizer.Clean(dto.City);
            lead.PropertyType ??= LeadContactNormalizer.Clean(dto.PropertyType);
            lead.PreferredLocation ??= LeadContactNormalizer.Clean(dto.PreferredLocation);
            lead.PreferredContactTime ??= LeadContactNormalizer.Clean(dto.PreferredContactTime);
            lead.CampaignName ??= LeadContactNormalizer.Clean(dto.CampaignName);
            lead.CampaignReference ??= LeadContactNormalizer.Clean(dto.CampaignReference);
            lead.AdReference ??= LeadContactNormalizer.Clean(dto.AdReference);
            lead.BudgetMin ??= dto.BudgetMin;
            lead.BudgetMax ??= dto.BudgetMax;
            lead.InterestedProjectId ??= dto.InterestedProjectId;
            lead.InterestedUnitId ??= dto.InterestedUnitId;

            if (lead.WhatsappNumber == null && !string.IsNullOrWhiteSpace(dto.WhatsappNumber))
            {
                lead.WhatsappNumber = dto.WhatsappNumber.Trim();
                lead.NormalizedWhatsapp = LeadContactNormalizer.NormalizePhoneOrNull(dto.WhatsappNumber);
            }

            if (lead.Email == null && !string.IsNullOrWhiteSpace(dto.Email))
            {
                lead.Email = LeadContactNormalizer.NormalizeEmail(dto.Email);
                lead.NormalizedEmail = lead.Email;
            }

            if (lead.PurchaseIntent == LeadPurchaseIntent.Unknown)
                lead.PurchaseIntent = dto.PurchaseIntent;

            // Adopt the provider reference when the lead has none, so replaying this exact
            // submission later is recognised as already ingested rather than re-enriching.
            var provider = LeadContactNormalizer.Clean(dto.ExternalProvider);
            var externalId = LeadContactNormalizer.Clean(dto.ExternalLeadId);
            if (provider != null && externalId != null && lead.ExternalLeadId == null)
            {
                lead.ExternalProvider = provider;
                lead.ExternalLeadId = externalId;
                lead.ExternalFormReference ??= LeadContactNormalizer.Clean(dto.ExternalFormReference);
                lead.ExternalSubmittedAt ??= dto.ExternalSubmittedAt;
                lead.IntegrationStatus = LeadIntegrationStatus.Processed;
            }

            // A repeat enquiry is a signal, not noise: it is appended, never overwritten.
            var trail = BuildSourceTrail(dto, source);
            lead.SourceDetails = Append(lead.SourceDetails, trail, 500);
            if (!string.IsNullOrWhiteSpace(dto.Notes))
                lead.Notes = Append(lead.Notes, dto.Notes.Trim(), 2000);

            lead.UpdatedAt = DateTime.UtcNow;

            LeadTimeline.Record(_context, lead, LeadActivityType.LeadEnriched,
                $"New enquiry received through {source.Name} for an existing lead.", actor,
                a =>
                {
                    a.Notes = dto.Notes;
                    a.NewValue = trail;
                });

            await NotifyOwnerAsync(lead, LeadNotificationType.NewLeadReceived,
                $"Repeat enquiry: {FullName(lead)}",
                $"A new enquiry arrived through {source.Name} for a lead you own.",
                $"repeat:{DateTime.UtcNow:yyyyMMddHHmm}", cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            return await LoadResponseRequiredAsync(lead.Id, cancellationToken);
        }

        private async Task<LeadDuplicateMatchDto?> FindDuplicateAsync(
            string normalizedPhone, string? normalizedWhatsapp, string? normalizedEmail, CancellationToken cancellationToken)
        {
            // Closed leads are history: a fresh enquiry from someone we lost last year is a
            // genuinely new opportunity, so only open leads count as duplicates.
            var openLead = await _context.Leads
                .AsNoTracking()
                .Where(l => !LeadStageRules.ClosedStages.Contains(l.Stage))
                .Where(l =>
                    l.NormalizedPhone == normalizedPhone
                    || (normalizedWhatsapp != null && (l.NormalizedWhatsapp == normalizedWhatsapp || l.NormalizedPhone == normalizedWhatsapp))
                    || (normalizedEmail != null && l.NormalizedEmail == normalizedEmail))
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => new
                {
                    l.Id,
                    l.LeadReference,
                    l.Stage,
                    l.NormalizedPhone,
                    l.NormalizedWhatsapp,
                    l.NormalizedEmail,
                    OwnerName = l.AssignedEmployee != null ? l.AssignedEmployee.FullName : null
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (openLead != null)
            {
                var matchedOn = openLead.NormalizedPhone == normalizedPhone ? "phone"
                    : normalizedWhatsapp != null && (openLead.NormalizedWhatsapp == normalizedWhatsapp || openLead.NormalizedPhone == normalizedWhatsapp) ? "whatsapp"
                    : "email";

                return new LeadDuplicateMatchDto
                {
                    MatchedOn = matchedOn,
                    LeadId = openLead.Id,
                    LeadReference = openLead.LeadReference,
                    LeadStage = openLead.Stage,
                    LeadOwnerName = openLead.OwnerName
                };
            }

            // No open lead, but this may still be a customer we already sold to. Report it so
            // staff can link rather than duplicate; it does not block lead creation.
            // Customer phones keep their local form ("03001234567") while lead numbers are
            // stored without the trunk/country prefix, so match on the trailing digits.
            var customer = await _context.Customers
                .AsNoTracking()
                .Where(c => c.Phone.EndsWith(normalizedPhone)
                            || (normalizedEmail != null && c.Email == normalizedEmail))
                .Select(c => new { c.Id, c.FullName, c.Phone, c.Email })
                .FirstOrDefaultAsync(cancellationToken);

            if (customer == null)
                return null;

            return new LeadDuplicateMatchDto
            {
                MatchedOn = customer.Phone.EndsWith(normalizedPhone, StringComparison.Ordinal) ? "phone" : "email",
                CustomerId = customer.Id,
                CustomerName = customer.FullName
            };
        }

        private async Task<LeadSource> ResolveSourceAsync(string? code, CancellationToken cancellationToken)
        {
            var wanted = string.IsNullOrWhiteSpace(code) ? DefaultSourceCode : code.Trim().ToLowerInvariant();

            var source = await _context.LeadSources.FirstOrDefaultAsync(s => s.Code == wanted, cancellationToken)
                ?? throw new InvalidOperationException($"Unknown lead source '{wanted}'.");

            if (!source.IsActive)
                throw new InvalidOperationException($"Lead source '{source.Name}' is no longer active.");

            return source;
        }

        // Reference depends on the generated id, so the row is saved twice behind a unique
        // placeholder — the same approach Booking uses for BookingReference.
        private async Task SaveNewLeadAsync(Lead lead, CancellationToken cancellationToken)
        {
            lead.LeadReference = $"LD-PENDING-{Guid.NewGuid():N}";
            await _context.SaveChangesAsync(cancellationToken);

            lead.LeadReference = $"LD-{lead.Id:D6}";
            await _context.SaveChangesAsync(cancellationToken);
        }

        // ── Reads ───────────────────────────────────────────────────────────────────

        public async Task<LeadResponseDto?> GetByIdAsync(int id, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureStaff(ctx);

            return await LeadAccess.Scope(_context.Leads.AsNoTracking(), ctx)
                .Where(l => l.Id == id)
                .Select(LeadMapping.ToResponse(_context))
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<LeadListDto> GetLeadsAsync(LeadFilterDto filter, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureStaff(ctx);

            var query = LeadAccess.Scope(_context.Leads.AsNoTracking(), ctx);

            if (filter.Stage.HasValue)
                query = query.Where(l => l.Stage == filter.Stage.Value);

            if (filter.AssignmentState.HasValue)
                query = query.Where(l => l.AssignmentState == filter.AssignmentState.Value);

            if (filter.Qualification.HasValue)
                query = query.Where(l => l.Qualification == filter.Qualification.Value);

            if (filter.LeadSourceId.HasValue)
                query = query.Where(l => l.LeadSourceId == filter.LeadSourceId.Value);

            if (filter.AssignedEmployeeId.HasValue)
                query = query.Where(l => l.AssignedEmployeeId == filter.AssignedEmployeeId.Value);

            if (filter.AssignedTeamId.HasValue)
                query = query.Where(l => l.AssignedTeamId == filter.AssignedTeamId.Value);

            if (filter.ProjectId.HasValue)
                query = query.Where(l => l.InterestedProjectId == filter.ProjectId.Value);

            if (!string.IsNullOrWhiteSpace(filter.CampaignName))
            {
                var campaign = filter.CampaignName.Trim();
                query = query.Where(l => l.CampaignName == campaign);
            }

            if (filter.Unassigned == true)
                query = query.Where(l => l.AssignedEmployeeId == null && l.AssignedTeamId == null);

            if (filter.OverdueOnly == true)
            {
                var now = DateTime.UtcNow;
                query = query.Where(l => l.NextActionAt != null && l.NextActionAt < now
                                         && !LeadStageRules.ClosedStages.Contains(l.Stage));
            }

            if (filter.InactiveOnly == true)
            {
                var inactiveBefore = DateTime.UtcNow.AddDays(-Math.Max(1, _alertOptions.InactivityDays));
                query = query.Where(l => !LeadStageRules.ClosedStages.Contains(l.Stage)
                    && (l.LastActivityAt ?? l.CreatedAt) < inactiveBefore);
            }

            if (filter.UnitId.HasValue)
                query = query.Where(l => l.InterestedUnitId == filter.UnitId.Value);

            if (filter.CreatedFrom.HasValue)
                query = query.Where(l => l.CreatedAt >= filter.CreatedFrom.Value);

            if (filter.CreatedTo.HasValue)
            {
                var to = filter.CreatedTo.Value.Date.AddDays(1);
                query = query.Where(l => l.CreatedAt < to);
            }

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                var digits = LeadContactNormalizer.NormalizePhone(filter.SearchTerm);
                query = query.Where(l =>
                    l.FirstName.ToLower().Contains(term)
                    || (l.LastName != null && l.LastName.ToLower().Contains(term))
                    || l.LeadReference.ToLower().Contains(term)
                    || (l.NormalizedEmail != null && l.NormalizedEmail.Contains(term))
                    || (digits != "" && l.NormalizedPhone.Contains(digits))
                    || (digits != "" && l.NormalizedWhatsapp != null && l.NormalizedWhatsapp.Contains(digits)));
            }

            var totalCount = await query.CountAsync(cancellationToken);

            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize, 1, 100);

            query = (filter.SortBy?.ToLowerInvariant()) switch
            {
                "lastactivity" => filter.SortDescending
                    ? query.OrderByDescending(l => l.LastActivityAt).ThenByDescending(l => l.Id)
                    : query.OrderBy(l => l.LastActivityAt).ThenBy(l => l.Id),
                "nextaction" => filter.SortDescending
                    ? query.OrderByDescending(l => l.NextActionAt).ThenByDescending(l => l.Id)
                    : query.OrderBy(l => l.NextActionAt).ThenBy(l => l.Id),
                "stage" => filter.SortDescending
                    ? query.OrderByDescending(l => l.Stage).ThenByDescending(l => l.Id)
                    : query.OrderBy(l => l.Stage).ThenBy(l => l.Id),
                _ => filter.SortDescending
                    ? query.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
                    : query.OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            };

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(LeadMapping.ToResponse(_context))
                .ToListAsync(cancellationToken);

            return new LeadListDto { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
        }

        public async Task<List<LeadActivityDto>> GetTimelineAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            await EnsureVisibleAsync(leadId, ctx, cancellationToken);

            return await _context.LeadActivities
                .AsNoTracking()
                .Where(a => a.LeadId == leadId)
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .Select(LeadMapping.ToActivityDto)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<LeadAssignmentHistoryDto>> GetAssignmentHistoryAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            await EnsureVisibleAsync(leadId, ctx, cancellationToken);

            return await _context.LeadAssignmentHistories
                .AsNoTracking()
                .Where(h => h.LeadId == leadId)
                .OrderByDescending(h => h.AssignedAt)
                .ThenByDescending(h => h.Id)
                .Select(h => new LeadAssignmentHistoryDto
                {
                    Id = h.Id,
                    PreviousEmployeeId = h.PreviousEmployeeId,
                    PreviousEmployeeName = _context.Employees
                        .Where(e => e.Id == h.PreviousEmployeeId).Select(e => e.FullName).FirstOrDefault(),
                    PreviousTeamId = h.PreviousTeamId,
                    AssignedEmployeeId = h.AssignedEmployeeId,
                    AssignedEmployeeName = _context.Employees
                        .Where(e => e.Id == h.AssignedEmployeeId).Select(e => e.FullName).FirstOrDefault(),
                    AssignedTeamId = h.AssignedTeamId,
                    Reason = h.Reason,
                    AssignedByUserId = h.AssignedByUserId,
                    AssignedByName = h.AssignedByName,
                    AssignedAt = h.AssignedAt
                })
                .ToListAsync(cancellationToken);
        }

        // ── Mutations ───────────────────────────────────────────────────────────────

        public async Task<LeadResponseDto> UpdateAsync(int id, UpdateLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var lead = await LoadForWriteAsync(id, ctx, cancellationToken);

            if (LeadStageRules.IsClosed(lead.Stage))
                throw new InvalidOperationException(
                    lead.Stage == LeadStage.Won
                        ? "A converted lead is kept as history and can no longer be edited."
                        : $"This lead is {lead.Stage}. Reopen it before editing.");

            var normalizedPhone = LeadContactNormalizer.NormalizePhone(dto.Phone);
            if (normalizedPhone.Length < 7)
                throw new InvalidOperationException("A usable phone number is required.");

            if (dto.BudgetMin.HasValue && dto.BudgetMax.HasValue && dto.BudgetMin > dto.BudgetMax)
                throw new InvalidOperationException("The minimum budget cannot be greater than the maximum budget.");

            // Editing the number must not achieve what creation refuses: two open leads for
            // the same person.
            if (normalizedPhone != lead.NormalizedPhone)
            {
                var clash = await _context.Leads.AnyAsync(
                    l => l.Id != lead.Id
                         && l.NormalizedPhone == normalizedPhone
                         && !LeadStageRules.ClosedStages.Contains(l.Stage), cancellationToken);

                if (clash)
                    throw new InvalidOperationException("Another open lead already uses that phone number.");
            }

            var changes = new List<string>();
            if (lead.Phone != dto.Phone.Trim()) changes.Add("phone");
            if (lead.Email != LeadContactNormalizer.NormalizeEmail(dto.Email)) changes.Add("email");

            lead.FirstName = dto.FirstName.Trim();
            lead.LastName = LeadContactNormalizer.Clean(dto.LastName);
            lead.Phone = dto.Phone.Trim();
            lead.NormalizedPhone = normalizedPhone;
            lead.WhatsappNumber = LeadContactNormalizer.Clean(dto.WhatsappNumber);
            lead.NormalizedWhatsapp = LeadContactNormalizer.NormalizePhoneOrNull(dto.WhatsappNumber);
            lead.Email = LeadContactNormalizer.NormalizeEmail(dto.Email);
            lead.NormalizedEmail = lead.Email;
            lead.Address = LeadContactNormalizer.Clean(dto.Address);
            lead.City = LeadContactNormalizer.Clean(dto.City);
            lead.PreferredContactMethod = dto.PreferredContactMethod;
            lead.PreferredContactTime = LeadContactNormalizer.Clean(dto.PreferredContactTime);
            lead.SourceDetails = LeadContactNormalizer.Clean(dto.SourceDetails);
            lead.CampaignName = LeadContactNormalizer.Clean(dto.CampaignName);
            lead.CampaignReference = LeadContactNormalizer.Clean(dto.CampaignReference);
            lead.AdReference = LeadContactNormalizer.Clean(dto.AdReference);
            lead.InterestedProjectId = dto.InterestedProjectId;
            lead.InterestedUnitId = dto.InterestedUnitId;
            lead.PropertyType = LeadContactNormalizer.Clean(dto.PropertyType);
            lead.PreferredLocation = LeadContactNormalizer.Clean(dto.PreferredLocation);
            lead.BudgetMin = dto.BudgetMin;
            lead.BudgetMax = dto.BudgetMax;
            lead.PurchaseIntent = dto.PurchaseIntent;
            lead.Notes = LeadContactNormalizer.Clean(dto.Notes);
            lead.UpdatedAt = DateTime.UtcNow;

            LeadTimeline.Record(_context, lead, LeadActivityType.DetailsUpdated,
                "Lead details updated.", ctx,
                a => a.NewValue = changes.Count == 0 ? null : string.Join(", ", changes) + " changed");

            await SaveWithConcurrencyGuardAsync(cancellationToken);

            return await LoadResponseRequiredAsync(lead.Id, cancellationToken);
        }

        public async Task<LeadResponseDto> AssignAsync(int id, AssignLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanAssign(ctx);

            var lead = await LoadForWriteAsync(id, ctx, cancellationToken);

            if (lead.Stage == LeadStage.Won)
                throw new InvalidOperationException("A converted lead cannot be reassigned.");

            var previousEmployeeId = lead.AssignedEmployeeId;
            var previousTeamId = lead.AssignedTeamId;
            var isReassignment = previousEmployeeId != null || previousTeamId != null;

            if (isReassignment && string.IsNullOrWhiteSpace(dto.Reason))
                throw new InvalidOperationException("A reason is required when changing lead ownership.");

            if (dto.EmployeeId == null && dto.TeamId == null)
            {
                if (!isReassignment)
                    throw new InvalidOperationException("This lead is already unassigned.");

                lead.AssignedEmployeeId = null;
                lead.AssignedTeamId = null;
                lead.AssignmentState = LeadAssignmentState.Unassigned;
                lead.AssignedAt = null;
                lead.AssignedByUserId = ctx.UserId;
            }
            else
            {
                Employee? employee = null;
                if (dto.EmployeeId.HasValue)
                    employee = await LoadAssignableEmployeeAsync(dto.EmployeeId.Value, ctx, cancellationToken);

                var teamId = dto.TeamId ?? employee?.TeamId;
                if (teamId.HasValue)
                    await EnsureTeamAssignableAsync(teamId.Value, ctx, cancellationToken);

                if (employee != null && previousEmployeeId == employee.Id && previousTeamId == teamId)
                    throw new InvalidOperationException("This lead is already assigned to that owner.");

                lead.AssignedEmployeeId = employee?.Id;
                lead.AssignedTeamId = teamId;
                lead.AssignmentState = isReassignment ? LeadAssignmentState.Reassigned : LeadAssignmentState.Assigned;
                lead.AssignedAt = DateTime.UtcNow;
                lead.AssignedByUserId = ctx.UserId;

                // A brand-new lead that now has an owner is waiting on first contact.
                if (lead.Stage == LeadStage.New)
                {
                    lead.Stage = LeadStage.FirstContactPending;
                    LeadTimeline.Record(_context, lead, LeadActivityType.StageChanged,
                        "Stage moved to First Contact Pending after assignment.", ctx,
                        a =>
                        {
                            a.PreviousValue = LeadStage.New.ToString();
                            a.NewValue = LeadStage.FirstContactPending.ToString();
                        });
                }
            }

            lead.UpdatedAt = DateTime.UtcNow;

            _context.LeadAssignmentHistories.Add(new LeadAssignmentHistory
            {
                LeadId = lead.Id,
                PreviousEmployeeId = previousEmployeeId,
                PreviousTeamId = previousTeamId,
                AssignedEmployeeId = lead.AssignedEmployeeId,
                AssignedTeamId = lead.AssignedTeamId,
                Reason = LeadContactNormalizer.Clean(dto.Reason),
                AssignedByUserId = ctx.UserId,
                AssignedByName = ctx.DisplayName,
                AssignedAt = DateTime.UtcNow
            });

            var activityType = lead.AssignmentState switch
            {
                LeadAssignmentState.Unassigned => LeadActivityType.LeadUnassigned,
                LeadAssignmentState.Reassigned => LeadActivityType.LeadReassigned,
                _ => LeadActivityType.LeadAssigned
            };

            LeadTimeline.Record(_context, lead, activityType,
                activityType switch
                {
                    LeadActivityType.LeadUnassigned => "Lead returned to the unassigned queue.",
                    LeadActivityType.LeadReassigned => "Lead reassigned.",
                    _ => "Lead assigned."
                },
                ctx,
                a =>
                {
                    a.Notes = LeadContactNormalizer.Clean(dto.Reason);
                    a.PreviousValue = previousEmployeeId?.ToString();
                    a.NewValue = lead.AssignedEmployeeId?.ToString();
                });

            var name = FullName(lead);
            if (lead.AssignedEmployeeId.HasValue)
            {
                await NotifyOwnerAsync(lead,
                    isReassignment ? LeadNotificationType.LeadReassigned : LeadNotificationType.LeadAssigned,
                    $"Lead assigned: {name}",
                    LeadContactNormalizer.Clean(dto.Reason) ?? "This lead is now yours to work.",
                    $"assign:{DateTime.UtcNow:yyyyMMddHHmmss}", cancellationToken);
            }

            if (isReassignment)
            {
                await _notifications.QueueForSupervisorsAsync(lead,
                    LeadNotificationType.LeadReassigned,
                    $"Lead ownership changed: {name}",
                    LeadContactNormalizer.Clean(dto.Reason),
                    $"reassign:{DateTime.UtcNow:yyyyMMddHHmmss}",
                    cancellationToken: cancellationToken);
            }

            await SaveWithConcurrencyGuardAsync(cancellationToken);

            return await LoadResponseRequiredAsync(lead.Id, cancellationToken);
        }

        public async Task<LeadResponseDto> ChangeStageAsync(int id, ChangeLeadStageDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var lead = await LoadForWriteAsync(id, ctx, cancellationToken);

            LeadStageRules.EnsureCanTransition(lead.Stage, dto.Stage);

            if (dto.Stage == LeadStage.Contacted && lead.LastContactAt == null)
                throw new InvalidOperationException(
                    "Record the call, WhatsApp message or meeting first — a lead is only Contacted once the contact is on file.");

            if (dto.Stage == LeadStage.SiteVisitScheduled)
            {
                var hasVisit = await _context.LeadSiteVisits.AnyAsync(
                    v => v.LeadId == lead.Id
                         && (v.Status == LeadSiteVisitStatus.Scheduled || v.Status == LeadSiteVisitStatus.Rescheduled),
                    cancellationToken);

                if (!hasVisit)
                    throw new InvalidOperationException("Schedule the site visit first; it carries the date, place and owner.");
            }

            if (dto.Stage == LeadStage.SiteVisitCompleted)
            {
                var hasCompletedVisit = await _context.LeadSiteVisits.AnyAsync(
                    v => v.LeadId == lead.Id && v.Status == LeadSiteVisitStatus.Completed, cancellationToken);

                if (!hasCompletedVisit)
                    throw new InvalidOperationException("Complete the site visit with its outcome first.");
            }

            var previous = lead.Stage;
            lead.Stage = dto.Stage;
            lead.UpdatedAt = DateTime.UtcNow;

            LeadTimeline.Record(_context, lead, LeadActivityType.StageChanged,
                $"Stage changed from {previous} to {dto.Stage}.", ctx,
                a =>
                {
                    a.Notes = LeadContactNormalizer.Clean(dto.Notes);
                    a.PreviousValue = previous.ToString();
                    a.NewValue = dto.Stage.ToString();
                });

            if (dto.Stage == LeadStage.Negotiation)
                LeadTimeline.Record(_context, lead, LeadActivityType.NegotiationUpdate,
                    "Negotiation started.", ctx, a => a.Notes = LeadContactNormalizer.Clean(dto.Notes));

            // Managers only need telling about the stages where money is close.
            if (dto.Stage is LeadStage.Negotiation or LeadStage.BookingPending or LeadStage.DocumentsInProgress)
            {
                await _notifications.QueueForSupervisorsAsync(lead, LeadNotificationType.StageChanged,
                    $"{FullName(lead)} moved to {dto.Stage}",
                    LeadContactNormalizer.Clean(dto.Notes),
                    $"stage:{dto.Stage}", cancellationToken: cancellationToken);
            }

            await SaveWithConcurrencyGuardAsync(cancellationToken);

            return await LoadResponseRequiredAsync(lead.Id, cancellationToken);
        }

        public async Task<LeadResponseDto> UpdateQualificationAsync(int id, UpdateLeadQualificationDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var lead = await LoadForWriteAsync(id, ctx, cancellationToken);

            if (LeadStageRules.IsClosed(lead.Stage))
                throw new InvalidOperationException($"This lead is {lead.Stage}; its qualification can no longer change.");

            if (lead.Qualification == dto.Qualification)
                throw new InvalidOperationException($"The lead is already marked {dto.Qualification}.");

            var previous = lead.Qualification;
            lead.Qualification = dto.Qualification;
            lead.UpdatedAt = DateTime.UtcNow;

            LeadTimeline.Record(_context, lead, LeadActivityType.QualificationChanged,
                $"Qualification changed from {previous} to {dto.Qualification}.", ctx,
                a =>
                {
                    a.Notes = LeadContactNormalizer.Clean(dto.Notes);
                    a.PreviousValue = previous.ToString();
                    a.NewValue = dto.Qualification.ToString();
                });

            await SaveWithConcurrencyGuardAsync(cancellationToken);

            return await LoadResponseRequiredAsync(lead.Id, cancellationToken);
        }

        public async Task<LeadResponseDto> CloseAsync(int id, bool dormant, CloseLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var lead = await LoadForWriteAsync(id, ctx, cancellationToken);

            if (lead.Stage == LeadStage.Won)
                throw new InvalidOperationException("A converted lead cannot be closed as lost or dormant.");

            if (LeadStageRules.IsClosed(lead.Stage))
                throw new InvalidOperationException($"This lead is already {lead.Stage}.");

            var wantedKind = dormant ? LeadClosureReasonKind.Dormant : LeadClosureReasonKind.Lost;
            var reason = await _context.LeadClosureReasons
                .FirstOrDefaultAsync(r => r.Id == dto.ClosureReasonId, cancellationToken)
                ?? throw new InvalidOperationException("Select a valid reason.");

            if (!reason.IsActive)
                throw new InvalidOperationException($"The reason '{reason.Name}' is no longer available.");

            if (reason.Kind != LeadClosureReasonKind.Both && reason.Kind != wantedKind)
                throw new InvalidOperationException($"The reason '{reason.Name}' cannot be used to mark a lead {wantedKind}.");

            if (!dormant && dto.ReactivateOn.HasValue)
                throw new InvalidOperationException("A follow-up date applies to dormant leads only.");

            if (dormant && dto.ReactivateOn.HasValue && dto.ReactivateOn.Value.Date < DateTime.UtcNow.Date)
                throw new InvalidOperationException("The follow-up date must be in the future.");

            var previous = lead.Stage;
            lead.Stage = dormant ? LeadStage.Dormant : LeadStage.Lost;
            lead.ClosureReasonId = reason.Id;
            lead.ClosureNotes = LeadContactNormalizer.Clean(dto.Notes);
            lead.ClosedAt = DateTime.UtcNow;
            lead.ReactivateOn = dormant ? dto.ReactivateOn : null;
            lead.NextActionAt = dormant ? dto.ReactivateOn : null;
            lead.NextActionSummary = dormant && dto.ReactivateOn.HasValue ? "Revisit dormant lead" : null;
            lead.UpdatedAt = DateTime.UtcNow;

            var cancelled = await CancelOpenWorkAsync(lead, $"Lead marked {lead.Stage}.", cancellationToken);

            LeadTimeline.Record(_context, lead,
                dormant ? LeadActivityType.LeadDormant : LeadActivityType.LeadLost,
                $"Lead marked {lead.Stage}: {reason.Name}.", ctx,
                a =>
                {
                    a.Notes = LeadContactNormalizer.Clean(dto.Notes);
                    a.PreviousValue = previous.ToString();
                    a.NewValue = reason.Name;
                });

            if (cancelled > 0)
                LeadTimeline.Record(_context, lead, LeadActivityType.SystemAlert,
                    $"{cancelled} open follow-up(s) and visit(s) cancelled with the lead.", ctx);

            await _notifications.QueueForSupervisorsAsync(lead, LeadNotificationType.LeadClosed,
                $"{FullName(lead)} marked {lead.Stage}",
                $"Reason: {reason.Name}.",
                $"closed:{lead.Stage}", cancellationToken: cancellationToken);

            await SaveWithConcurrencyGuardAsync(cancellationToken);

            return await LoadResponseRequiredAsync(lead.Id, cancellationToken);
        }

        public async Task<LeadResponseDto> ReopenAsync(int id, ReopenLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanReopen(ctx);

            var lead = await LoadForWriteAsync(id, ctx, cancellationToken);

            if (!LeadStageRules.ReopenableStages.Contains(lead.Stage))
                throw new InvalidOperationException("Only a lost or dormant lead can be reopened.");

            if (!LeadStageRules.ReopenTargets.Contains(dto.Stage))
                throw new InvalidOperationException($"A lead cannot be reopened directly into {dto.Stage}.");

            if (dto.Stage == LeadStage.Contacted && lead.LastContactAt == null)
                throw new InvalidOperationException("Reopen into New or First Contact Pending — no contact has been recorded yet.");

            var previousStage = lead.Stage;
            var previousReason = await _context.LeadClosureReasons
                .Where(r => r.Id == lead.ClosureReasonId)
                .Select(r => r.Name)
                .FirstOrDefaultAsync(cancellationToken);

            lead.Stage = dto.Stage;
            lead.ClosureReasonId = null;
            lead.ClosureNotes = null;
            lead.ClosedAt = null;
            lead.ReactivateOn = null;
            lead.UpdatedAt = DateTime.UtcNow;

            LeadTimeline.Record(_context, lead, LeadActivityType.LeadReopened,
                $"Lead reopened from {previousStage} into {dto.Stage}.", ctx,
                a =>
                {
                    a.Notes = dto.Reason.Trim();
                    a.PreviousValue = previousReason == null ? previousStage.ToString() : $"{previousStage} ({previousReason})";
                    a.NewValue = dto.Stage.ToString();
                });

            await NotifyOwnerAsync(lead, LeadNotificationType.LeadAssigned,
                $"Lead reopened: {FullName(lead)}", dto.Reason.Trim(),
                $"reopen:{DateTime.UtcNow:yyyyMMddHHmmss}", cancellationToken);

            await SaveWithConcurrencyGuardAsync(cancellationToken);

            return await LoadResponseRequiredAsync(lead.Id, cancellationToken);
        }

        // ── Conversion ──────────────────────────────────────────────────────────────

        public async Task<LeadConversionResultDto> ConvertAsync(
            int id, ConvertLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanConvert(ctx);

            var lead = await LoadForWriteAsync(id, ctx, cancellationToken);

            // Idempotent: a repeated request returns the first conversion rather than
            // creating a second customer or booking.
            if (lead.ConvertedBookingId.HasValue)
                return await BuildExistingConversionAsync(lead, cancellationToken);

            LeadStageRules.EnsureCanConvert(lead.Stage);

            var source = await _context.LeadSources.FirstAsync(s => s.Id == lead.LeadSourceId, cancellationToken);

            LeadConversionResultDto? result = null;

            await RunInTransactionAsync(async () =>
            {
                int customerId;
                bool customerWasCreated;

                if (dto.CustomerId.HasValue)
                {
                    var customer = await _context.Customers
                        .FirstOrDefaultAsync(c => c.Id == dto.CustomerId.Value, cancellationToken)
                        ?? throw new InvalidOperationException("Customer not found.");

                    if (customer.Status == CustomerStatus.Blocked)
                        throw new InvalidOperationException("This customer is blocked and cannot be booked.");

                    customerId = customer.Id;
                    customerWasCreated = false;
                }
                else
                {
                    var resolution = await _customerService.FindOrCreateCustomerAsync(
                        FullName(lead),
                        lead.Phone,
                        LeadContactNormalizer.Clean(dto.CNIC),
                        lead.Email,
                        lead.Address,
                        // Attribution survives the jump from lead to customer.
                        source.CustomerSource,
                        $"Converted from lead {lead.LeadReference} ({source.Name}).",
                        ctx.UserId,
                        LeadContactNormalizer.Clean(dto.FatherName),
                        whatsapp: lead.WhatsappNumber);

                    customerId = resolution.CustomerId;
                    customerWasCreated = resolution.WasCreated;
                }

                var assignedSalesUserId = lead.AssignedEmployeeId == null
                    ? null
                    : await _context.Employees
                        .Where(e => e.Id == lead.AssignedEmployeeId)
                        .Select(e => e.UserId)
                        .FirstOrDefaultAsync(cancellationToken);

                var booking = await _bookingService.CreateBookingAsync(new CreateBookingDto
                {
                    UnitId = dto.UnitId,
                    CustomerId = customerId,
                    Source = source.CustomerSource,
                    AgreedSalePrice = dto.AgreedSalePrice,
                    DiscountPercent = dto.DiscountPercent,
                    DiscountReason = dto.DiscountReason,
                    BookingAmountRequired = dto.BookingAmountRequired,
                    BookingAmountDueDate = dto.BookingAmountDueDate,
                    AssignedSalesUserId = assignedSalesUserId,
                    ReferenceId = lead.LeadReference,
                    InternalNotes = LeadContactNormalizer.Clean(dto.Notes)
                }, ctx.UserId);

                lead.Stage = LeadStage.Won;
                lead.ConvertedAt = DateTime.UtcNow;
                lead.ConvertedByUserId = ctx.UserId;
                lead.ConvertedCustomerId = customerId;
                lead.ConvertedBookingId = booking.Id;
                lead.ClosureReasonId = null;
                lead.ClosureNotes = null;
                lead.ClosedAt = null;
                lead.NextActionAt = null;
                lead.NextActionSummary = null;
                lead.UpdatedAt = DateTime.UtcNow;

                await CancelOpenWorkAsync(lead, "Lead converted into a booking.", cancellationToken);

                LeadTimeline.Record(_context, lead, LeadActivityType.CustomerLinked,
                    customerWasCreated ? "Customer record created from the lead." : "Linked to an existing customer.",
                    ctx, a => a.CustomerId = customerId);

                LeadTimeline.Record(_context, lead, LeadActivityType.BookingCreated,
                    $"Booking {booking.BookingReference} created.", ctx,
                    a =>
                    {
                        a.BookingId = booking.Id;
                        a.CustomerId = customerId;
                    });

                LeadTimeline.Record(_context, lead, LeadActivityType.LeadConverted,
                    $"Lead converted and marked Won ({booking.BookingReference}).", ctx,
                    a =>
                    {
                        a.PreviousValue = LeadStage.BookingPending.ToString();
                        a.NewValue = LeadStage.Won.ToString();
                        a.BookingId = booking.Id;
                        a.CustomerId = customerId;
                    });

                await _notifications.QueueForSupervisorsAsync(lead, LeadNotificationType.LeadConverted,
                    $"Lead won: {FullName(lead)}",
                    $"Booking {booking.BookingReference} created.",
                    $"converted:{booking.Id}", cancellationToken: cancellationToken);

                await NotifyOwnerAsync(lead, LeadNotificationType.LeadConverted,
                    $"Lead won: {FullName(lead)}",
                    $"Booking {booking.BookingReference} created.",
                    $"converted:{booking.Id}", cancellationToken);

                await SaveWithConcurrencyGuardAsync(cancellationToken);

                result = new LeadConversionResultDto
                {
                    Created = true,
                    LeadId = lead.Id,
                    LeadReference = lead.LeadReference,
                    CustomerId = customerId,
                    CustomerWasCreated = customerWasCreated,
                    BookingId = booking.Id,
                    BookingReference = booking.BookingReference,
                    Message = "Lead converted into a customer and booking."
                };
            });

            return result!;
        }

        private async Task<LeadConversionResultDto> BuildExistingConversionAsync(Lead lead, CancellationToken cancellationToken)
        {
            var reference = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Id == lead.ConvertedBookingId)
                .Select(b => b.BookingReference)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

            return new LeadConversionResultDto
            {
                Created = false,
                LeadId = lead.Id,
                LeadReference = lead.LeadReference,
                CustomerId = lead.ConvertedCustomerId ?? 0,
                CustomerWasCreated = false,
                BookingId = lead.ConvertedBookingId ?? 0,
                BookingReference = reference,
                Message = "This lead had already been converted; the existing booking was returned."
            };
        }

        // ── Booking-request backfill ────────────────────────────────────────────────

        public async Task<LeadBackfillResultDto> BackfillFromBookingRequestsAsync(
            LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanConfigure(ctx);

            var websiteSource = await _context.LeadSources.FirstAsync(s => s.Code == WebsiteSourceCode, cancellationToken);
            var otherReasonId = await _context.LeadClosureReasons
                .Where(r => r.Code == "other")
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var pending = await _context.BookingRequests
                .Where(br => br.LeadId == null)
                .OrderBy(br => br.Id)
                .ToListAsync(cancellationToken);

            var alreadyLinked = await _context.BookingRequests.CountAsync(br => br.LeadId != null, cancellationToken);
            var created = 0;

            // Approved requests already produced a booking; resolve them all in one query so
            // the loop below stays free of per-row round trips.
            var requestIds = pending.Select(br => br.Id).ToList();
            var bookingIdByRequest = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.BookingRequestId != null && requestIds.Contains(b.BookingRequestId.Value))
                .Select(b => new { RequestId = b.BookingRequestId!.Value, b.Id })
                .ToDictionaryAsync(x => x.RequestId, x => x.Id, cancellationToken);

            foreach (var request in pending)
            {
                // Re-runnable: an earlier partial run may already have produced the lead.
                var externalId = request.Id.ToString();
                var existing = await _context.Leads
                    .FirstOrDefaultAsync(l => l.ExternalProvider == BookingRequestProvider
                                              && l.ExternalLeadId == externalId, cancellationToken);

                if (existing != null)
                {
                    request.LeadId = existing.Id;
                    continue;
                }

                bookingIdByRequest.TryGetValue(request.Id, out var bookingId);
                var lead = BuildLeadFromBookingRequest(request, websiteSource, otherReasonId,
                    bookingId == 0 ? null : bookingId);
                _context.Leads.Add(lead);

                LeadTimeline.Record(_context, lead, LeadActivityType.LeadCreated,
                    $"Lead created from historical website booking request #{request.Id}.", null,
                    a => a.OccurredAt = request.RequestedAt);
                LeadTimeline.Record(_context, lead, LeadActivityType.SourceRecorded,
                    "Source recorded: Website Inquiry.", null,
                    a => a.OccurredAt = request.RequestedAt);

                await SaveNewLeadAsync(lead, cancellationToken);

                request.LeadId = lead.Id;
                created++;
            }

            if (created > 0 || pending.Count > 0)
                await _context.SaveChangesAsync(cancellationToken);

            return new LeadBackfillResultDto
            {
                BookingRequestsScanned = pending.Count,
                LeadsCreated = created,
                AlreadyLinked = alreadyLinked,
                Message = $"{created} lead(s) created from {pending.Count} unlinked booking request(s)."
            };
        }

        private static Lead BuildLeadFromBookingRequest(
            BookingRequest request, LeadSource websiteSource, int? otherReasonId, int? bookingId)
        {
            var (first, last) = SplitName(request.FullName);

            var lead = new Lead
            {
                FirstName = first,
                LastName = last,
                Phone = request.Phone,
                NormalizedPhone = LeadContactNormalizer.NormalizePhone(request.Phone),
                Email = LeadContactNormalizer.NormalizeEmail(request.Email),
                NormalizedEmail = LeadContactNormalizer.NormalizeEmail(request.Email),
                Address = LeadContactNormalizer.Clean(request.Address),
                LeadSourceId = websiteSource.Id,
                Source = websiteSource,
                SourceDetails = $"Website booking request #{request.Id}.",
                ExternalProvider = BookingRequestProvider,
                ExternalLeadId = request.Id.ToString(),
                ExternalSubmittedAt = request.RequestedAt,
                IntegrationStatus = LeadIntegrationStatus.Processed,
                InterestedUnitId = request.UnitId,
                Notes = LeadContactNormalizer.Clean(request.Notes),
                CreatedByUserId = request.UserId,
                CreatedAt = request.RequestedAt,
                AssignmentState = LeadAssignmentState.Unassigned,
                Stage = LeadStage.New
            };

            switch (request.Status)
            {
                case BookingRequestStatus.Approved:
                    lead.Stage = LeadStage.Won;
                    lead.ConvertedAt = request.ReviewedAt;
                    lead.ConvertedByUserId = request.ReviewedByUserId;
                    lead.ConvertedCustomerId = request.CustomerId;
                    lead.ConvertedBookingId = bookingId;
                    break;

                case BookingRequestStatus.Rejected:
                    lead.Stage = LeadStage.Lost;
                    lead.ClosureReasonId = otherReasonId;
                    lead.ClosureNotes = LeadContactNormalizer.Clean(request.RejectionReason) ?? "Request rejected.";
                    lead.ClosedAt = request.ReviewedAt;
                    break;

                case BookingRequestStatus.Cancelled:
                    lead.Stage = LeadStage.Dormant;
                    lead.ClosureReasonId = otherReasonId;
                    lead.ClosureNotes = "Request cancelled by the customer.";
                    lead.ClosedAt = request.ReviewedAt;
                    break;
            }

            return lead;
        }

        public async Task<int> EnsureLeadForBookingRequestAsync(
            BookingRequest request, LeadUserContext? actor, CancellationToken cancellationToken = default)
        {
            if (request.LeadId.HasValue)
                return request.LeadId.Value;

            var (first, last) = SplitName(request.FullName);
            var projectId = await _context.Units
                .AsNoTracking()
                .Where(u => u.Id == request.UnitId)
                .Select(u => (int?)u.ProjectId)
                .FirstOrDefaultAsync(cancellationToken);

            var result = await IngestAsync(new LeadIntakeDto
            {
                FirstName = first,
                LastName = last,
                Phone = request.Phone,
                Email = request.Email,
                Address = request.Address,
                SourceCode = WebsiteSourceCode,
                SourceDetails = $"Website booking request #{request.Id}.",
                InterestedProjectId = projectId,
                InterestedUnitId = request.UnitId,
                Notes = request.Notes,
                ExternalProvider = BookingRequestProvider,
                ExternalLeadId = request.Id.ToString(),
                ExternalSubmittedAt = request.RequestedAt,
                // A website enquiry is never dropped: if we already know this person, the
                // enquiry is added to their existing lead instead of being refused.
                AllowDuplicate = true
            }, actor, cancellationToken);

            var leadId = result.Lead?.Id
                ?? throw new InvalidOperationException("The lead for this website request could not be created.");

            request.LeadId = leadId;
            return leadId;
        }

        public async Task CloseFromSystemAsync(
            int leadId, bool dormant, string reasonCode, string summary, string? notes, int? actingUserId,
            CancellationToken cancellationToken = default)
        {
            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
            if (lead == null || LeadStageRules.IsClosed(lead.Stage))
                return;

            var reason = await _context.LeadClosureReasons
                .FirstOrDefaultAsync(r => r.Code == reasonCode, cancellationToken);

            lead.Stage = dormant ? LeadStage.Dormant : LeadStage.Lost;
            lead.ClosureReasonId = reason?.Id;
            lead.ClosureNotes = LeadContactNormalizer.LimitOrNull(notes, 1000);
            lead.ClosedAt = DateTime.UtcNow;
            lead.NextActionAt = null;
            lead.NextActionSummary = null;
            lead.UpdatedAt = DateTime.UtcNow;

            await CancelOpenWorkAsync(lead, summary, cancellationToken);

            // Attribution only — this context never reaches an access check. It exists so the
            // timeline records who triggered the closure rather than showing it as a system
            // event when a person actually decided it.
            LeadUserContext? actor = null;
            if (actingUserId != null)
            {
                var name = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.UserId == actingUserId.Value)
                    .Select(u => u.FullName)
                    .FirstOrDefaultAsync(cancellationToken);

                actor = new LeadUserContext { UserId = actingUserId.Value, Role = LeadRoles.Admin, DisplayName = name };
            }

            LeadTimeline.Record(_context, lead,
                dormant ? LeadActivityType.LeadDormant : LeadActivityType.LeadLost,
                summary, actor, a =>
                {
                    a.Notes = notes;
                    a.NewValue = reason?.Name;
                });

            await _notifications.QueueForSupervisorsAsync(lead, LeadNotificationType.LeadClosed,
                $"{FullName(lead)} marked {lead.Stage}", summary,
                $"closed:{lead.Stage}", cancellationToken: cancellationToken);
        }

        // ── Shared helpers ──────────────────────────────────────────────────────────

        internal static string FullName(Lead lead) =>
            string.IsNullOrWhiteSpace(lead.LastName) ? lead.FirstName : $"{lead.FirstName} {lead.LastName}";

        internal static (string First, string? Last) SplitName(string fullName)
        {
            var trimmed = (fullName ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return ("Unknown", null);

            var separator = trimmed.IndexOf(' ');
            return separator <= 0
                ? (Truncate(trimmed, 100), null)
                : (Truncate(trimmed[..separator], 100), Truncate(trimmed[(separator + 1)..].Trim(), 100));
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

        private static string Append(string? existing, string addition, int max)
        {
            var combined = string.IsNullOrWhiteSpace(existing) ? addition : $"{existing}\n{addition}";
            return combined.Length <= max ? combined : combined[^max..];
        }

        private static string BuildSourceTrail(LeadIntakeDto dto, LeadSource source)
        {
            var parts = new List<string> { source.Name };
            if (!string.IsNullOrWhiteSpace(dto.CampaignName)) parts.Add($"campaign: {dto.CampaignName.Trim()}");
            if (!string.IsNullOrWhiteSpace(dto.AdReference)) parts.Add($"ad: {dto.AdReference.Trim()}");
            if (!string.IsNullOrWhiteSpace(dto.ExternalProvider)) parts.Add($"provider: {dto.ExternalProvider.Trim()}");
            if (!string.IsNullOrWhiteSpace(dto.SourceDetails)) parts.Add(dto.SourceDetails.Trim());
            return string.Join(" | ", parts);
        }

        private async Task<Employee> LoadAssignableEmployeeAsync(int employeeId, LeadUserContext ctx, CancellationToken cancellationToken)
        {
            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
                ?? throw new InvalidOperationException("Employee not found.");

            if (employee.Status != EmployeeStatus.Active)
                throw new InvalidOperationException($"{employee.FullName} is not an active employee.");

            // A manager may only hand work to their own people.
            if (ctx.IsManager && employee.Id != ctx.EmployeeId
                && (employee.TeamId == null || !ctx.ManagedTeamIds.Contains(employee.TeamId.Value)))
                throw new LeadAuthorizationException("You can only assign leads within your own team.");

            return employee;
        }

        private async Task EnsureTeamAssignableAsync(int teamId, LeadUserContext ctx, CancellationToken cancellationToken)
        {
            var team = await _context.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken)
                ?? throw new InvalidOperationException("Team not found.");

            if (!team.IsActive)
                throw new InvalidOperationException($"Team '{team.Name}' is not active.");

            if (ctx.IsManager && !ctx.ManagedTeamIds.Contains(teamId))
                throw new LeadAuthorizationException("You can only assign leads to a team you manage.");
        }

        /// <summary>Loads a tracked lead the caller is allowed to act on, or throws.</summary>
        private async Task<Lead> LoadForWriteAsync(int id, LeadUserContext ctx, CancellationToken cancellationToken)
        {
            LeadAccess.EnsureStaff(ctx);

            // Out-of-scope leads are reported exactly like missing ones, so the endpoint
            // never confirms that a lead someone cannot see exists.
            var lead = await LeadAccess.Scope(_context.Leads, ctx)
                .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

            return lead ?? throw new LeadNotFoundException();
        }

        private async Task EnsureVisibleAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken)
        {
            LeadAccess.EnsureStaff(ctx);

            var visible = await LeadAccess.Scope(_context.Leads.AsNoTracking(), ctx)
                .AnyAsync(l => l.Id == leadId, cancellationToken);

            if (!visible)
                throw new LeadNotFoundException();
        }

        /// <summary>Cancels pending follow-ups and open visits so a closed lead stops alerting.</summary>
        private async Task<int> CancelOpenWorkAsync(Lead lead, string reason, CancellationToken cancellationToken)
        {
            var followUps = await _context.LeadFollowUps
                .Where(f => f.LeadId == lead.Id && f.Status == LeadFollowUpStatus.Pending)
                .ToListAsync(cancellationToken);

            var visits = await _context.LeadSiteVisits
                .Where(v => v.LeadId == lead.Id
                            && (v.Status == LeadSiteVisitStatus.Scheduled || v.Status == LeadSiteVisitStatus.Rescheduled))
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var followUp in followUps)
            {
                followUp.Status = LeadFollowUpStatus.Cancelled;
                followUp.Outcome = reason;
                followUp.UpdatedAt = now;
            }

            foreach (var visit in visits)
            {
                visit.Status = LeadSiteVisitStatus.Cancelled;
                visit.CancellationReason = reason;
                visit.UpdatedAt = now;
            }

            return followUps.Count + visits.Count;
        }

        private async Task RunInTransactionAsync(Func<Task> action)
        {
            if (_context.Database.CurrentTransaction != null)
            {
                await action();
                return;
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                await action();
                await transaction.CommitAsync();
            });
        }

        private async Task SaveWithConcurrencyGuardAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "Someone else updated this lead while you were working on it. Reload and try again.");
            }
        }

        private async Task<LeadResponseDto> LoadResponseRequiredAsync(int id, CancellationToken cancellationToken) =>
            await LoadResponseAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("The lead could not be reloaded after the change.");

        private async Task<LeadResponseDto?> LoadResponseAsync(int id, CancellationToken cancellationToken)
        {
            // Read back through the same untracked projection a GET would use, so the
            // response can never disagree with it. The change tracker is deliberately left
            // alone: callers such as the website booking-request flow still hold entities of
            // their own that have not been saved yet.
            return await _context.Leads
                .AsNoTracking()
                .Where(l => l.Id == id)
                .Select(LeadMapping.ToResponse(_context))
                .FirstOrDefaultAsync(cancellationToken);
        }

        private void DetachPendingInserts()
        {
            foreach (var entry in _context.ChangeTracker.Entries()
                         .Where(e => e.State == EntityState.Added)
                         .ToList())
                entry.State = EntityState.Detached;
        }

        private static bool IsExternalDuplicate(DbUpdateException ex) =>
            ex.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("ExternalLeadId", StringComparison.OrdinalIgnoreCase);
    }
}
