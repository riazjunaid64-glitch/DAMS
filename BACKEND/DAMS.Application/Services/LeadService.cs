using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DAMS.Application.Services
{
    public class LeadService : ILeadService
    {
        private const string DefaultSourceCode = "manual";
        private const int IntakeHoldPageSize = 200;
        /// <summary>How long a write waits for another request's contact lock. Well inside the
        /// 30-second command timeout; only reached under heavy contention for one contact.
        /// Settable only so a test can reach the timeout without waiting for it.</summary>
        internal int IntakeLockTimeoutMilliseconds { get; init; } = 15000;
        private const string WebsiteSourceCode = "website";
        private const string BookingRequestProvider = "dams_booking_request";

        private readonly AppDbContext _context;
        private readonly ICustomerService _customerService;
        private readonly IBookingService _bookingService;
        private readonly ILeadNotificationService _notifications;
        private readonly ICustomerAccountLinkService? _accountLinks;
        private readonly LeadAlertOptions _alertOptions;

        /// <param name="accountLinks">
        /// Optional so the many lead tests that never convert a website request do not have to
        /// construct it. When it is absent no <c>Customer.UserId</c> is ever written — conversion
        /// still succeeds and the customer is simply left unowned, which is the safe direction to
        /// fail in.
        /// </param>
        public LeadService(
            AppDbContext context,
            ICustomerService customerService,
            IBookingService bookingService,
            ILeadNotificationService notifications,
            IOptions<LeadAlertOptions> alertOptions,
            ICustomerAccountLinkService? accountLinks = null)
        {
            _context = context;
            _customerService = customerService;
            _bookingService = bookingService;
            _notifications = notifications;
            _accountLinks = accountLinks;
            _alertOptions = alertOptions.Value;
        }

        // ── Ingestion ───────────────────────────────────────────────────────────────

        public async Task<LeadIntakeResultDto> IngestAsync(
            LeadIntakeDto dto,
            LeadUserContext? actor,
            bool trustedExternal = false,
            CancellationToken cancellationToken = default)
        {
            var provider = LeadContactNormalizer.Clean(dto.ExternalProvider);
            var externalId = LeadContactNormalizer.Clean(dto.ExternalLeadId);
            var isExternal = trustedExternal && provider != null && externalId != null;

            // A number too short to dial is not a contact method. Discard it rather than store
            // something that would never match anything — the original answer is still kept on
            // the external submission for whoever wants to look. Phone and WhatsApp are held to
            // the same bar: a manual lead created with only an unusable WhatsApp number is just
            // as unreachable as one with only an unusable phone number.
            var normalizedPhone = LeadContactNormalizer.NormalizeUsablePhoneOrNull(dto.Phone);
            var normalizedWhatsapp = LeadContactNormalizer.NormalizeUsablePhoneOrNull(dto.WhatsappNumber);
            var normalizedEmail = LeadContactNormalizer.NormalizeEmail(dto.Email);

            EnsureContactable(isExternal, normalizedPhone, normalizedWhatsapp, normalizedEmail, dto.Phone);

            var source = await ResolveSourceAsync(dto.SourceCode, cancellationToken);

            // Assignment writes into the dto, so a retried attempt must start from what was asked.
            var requestedEmployeeId = dto.AssignedEmployeeId;
            var requestedTeamId = dto.AssignedTeamId;

            try
            {
                // Checking for an existing lead and creating or enriching one happen in a single
                // transaction that first locks these contact details, so two enquiries for the
                // same person cannot both see "no lead yet" and both create one.
                return await RunContactWriteAtomicallyAsync(async ct =>
                {
                    dto.AssignedEmployeeId = requestedEmployeeId;
                    dto.AssignedTeamId = requestedTeamId;
                    // A retry clears the tracker, and an untracked source would be inserted anew.
                    if (_context.Entry(source).State == EntityState.Detached)
                        _context.LeadSources.Attach(source);

                    await LockContactsAsync(isExternal ? provider : null, isExternal ? externalId : null,
                        normalizedPhone, normalizedWhatsapp, normalizedEmail, ct);

                    return await DecideAndWriteAsync(dto, actor, source, isExternal, provider, externalId,
                        normalizedPhone, normalizedWhatsapp, normalizedEmail, ct);
                }, cancellationToken);
            }
            catch (DbUpdateException ex) when (isExternal && IsExternalDuplicate(ex))
            {
                // A backstop: the submission lock already makes a second copy of the same
                // webhook wait and then find the first, so this is reached only if the unique
                // index catches what the lock did not. Discard only what this call tried to
                // insert — a caller such as the website request flow may still have unsaved
                // changes of its own — then return the winner instead of a 500.
                DetachPendingInserts();
                var winnerId = await _context.LeadExternalSubmissions
                    .AsNoTracking()
                    .Where(s => s.Provider == provider && s.ExternalLeadId == externalId)
                    .Select(s => s.LeadId)
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
        }

        /// <summary>
        /// Everything intake decides once the details are known: a replay, a conflict, a match to
        /// enrich, or a new lead. Runs inside <see cref="RunContactWriteAtomicallyAsync{T}"/>, under the
        /// contact locks, so its reads and its write cannot be split by a concurrent enquiry.
        /// </summary>
        private async Task<LeadIntakeResultDto> DecideAndWriteAsync(
            LeadIntakeDto dto,
            LeadUserContext? actor,
            LeadSource source,
            bool isExternal,
            string? provider,
            string? externalId,
            string? normalizedPhone,
            string? normalizedWhatsapp,
            string? normalizedEmail,
            CancellationToken cancellationToken)
        {
            // 1. Same external submission replayed — return what we already stored.
            if (isExternal)
            {
                var existingExternal = await _context.LeadExternalSubmissions
                    .AsNoTracking()
                    .Where(s => s.Provider == provider && s.ExternalLeadId == externalId)
                    .Select(s => s.LeadId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingExternal == 0)
                {
                    existingExternal = await _context.Leads
                        .AsNoTracking()
                        .Where(l => l.ExternalProvider == provider && l.ExternalLeadId == externalId)
                        .Select(l => l.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                if (existingExternal != 0)
                {
                    return new LeadIntakeResultDto
                    {
                        AlreadyIngested = true,
                        Message = "This submission had already been received; the existing lead was returned.",
                        Lead = await LoadResponseAsync(existingExternal, cancellationToken)
                    };
                }

                // Held earlier for an identity conflict: the same hold stands, whatever has changed.
                var existingHold = await _context.LeadIntakeHolds
                    .AsNoTracking()
                    .Where(h => h.Provider == provider && h.ExternalLeadId == externalId)
                    .Select(h => new { h.Id, h.Status })
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingHold != null)
                {
                    return new LeadIntakeResultDto
                    {
                        AlreadyIngested = true,
                        HeldForReview = existingHold.Status == LeadIntakeHoldStatus.Open,
                        HoldId = existingHold.Id,
                        Message = existingHold.Status == LeadIntakeHoldStatus.Open
                            ? "This submission is already waiting for an administrator to choose its lead."
                            : "This submission was already reviewed by an administrator."
                    };
                }
            }

            // 2. Someone we already know?
            var (match, openLeads) = await FindDuplicateAsync(normalizedPhone, normalizedWhatsapp, normalizedEmail, cancellationToken);

            // 2a. The details point at more than one person — the phone matching one open lead and
            // the email another. Choosing either would join two people's records on a guess, so
            // neither is touched until someone who can see both decides.
            if (openLeads.Count > 1)
            {
                if (actor != null)
                {
                    foreach (var candidate in openLeads)
                    {
                        if (!await IsVisibleAsync(candidate.LeadId!.Value, actor, cancellationToken))
                            throw new InvalidOperationException(
                                "The lead could not be created. Ask a manager or administrator for assistance.");
                    }

                    // A person saw every candidate and picked one: that is the explicit decision.
                    var chosen = openLeads.FirstOrDefault(m => m.LeadId == dto.ExpectedExistingLeadId);
                    if (dto.AllowDuplicate && chosen != null)
                    {
                        await LockLeadAsync(chosen.LeadId!.Value, cancellationToken);
                        var enrichedChoice = await EnrichExistingLeadAsync(
                            chosen.LeadId!.Value, dto, source, actor, isExternal, cancellationToken);
                        return new LeadIntakeResultDto
                        {
                            IsDuplicate = true,
                            EnrichedExisting = true,
                            Match = chosen,
                            Message = "The enquiry was added to the lead you chose.",
                            Lead = enrichedChoice
                        };
                    }
                }
                else if (dto.AllowDuplicate)
                {
                    // An external channel would have enriched automatically, and nobody is here
                    // to choose. Keep the enquiry, exactly as it arrived, for an administrator.
                    return await HoldForReviewAsync(dto, provider, externalId, isExternal, openLeads, cancellationToken);
                }

                return new LeadIntakeResultDto
                {
                    IsDuplicate = true,
                    IdentityConflict = true,
                    Match = openLeads[0],
                    ConflictingMatches = openLeads,
                    Message = $"These details match more than one open lead ({DescribeLeads(openLeads)}). " +
                              "Nothing was changed. Choose which lead this enquiry belongs to."
                };
            }

            if (match is { LeadId: not null })
            {
                // Duplicate detection protects the one-open-lead invariant across the whole
                // CRM, but the candidate itself remains subject to normal visibility. Do not
                // turn an inaccessible record into either a disclosure or a second open lead.
                if (actor != null && !await IsVisibleAsync(match.LeadId.Value, actor, cancellationToken))
                    throw new InvalidOperationException(
                        "The lead could not be created. Ask a manager or administrator for assistance.");

                // The caller chose to add this enquiry to one specific lead, but the details now
                // match a different one. Enriching it would add the enquiry somewhere the person
                // never agreed to, so nothing is written and the current match is shown instead.
                if (dto.ExpectedExistingLeadId is { } expectedLeadId && expectedLeadId != match.LeadId)
                {
                    return new LeadIntakeResultDto
                    {
                        IsDuplicate = true,
                        Match = match,
                        Message = $"These details now match {match.LeadReference}, not the lead you chose. " +
                                  "Nothing was added. Review the match before adding this enquiry."
                    };
                }

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

                await LockLeadAsync(match.LeadId.Value, cancellationToken);
                var enriched = await EnrichExistingLeadAsync(
                    match.LeadId.Value, dto, source, actor, isExternal, cancellationToken);
                return new LeadIntakeResultDto
                {
                    IsDuplicate = true,
                    EnrichedExisting = true,
                    Match = match,
                    Message = "An existing lead matched this contact and was enriched with the new enquiry.",
                    Lead = enriched
                };
            }

            // Asked to add to a lead that no longer matches at all — typically closed in the
            // meantime. Creating a new lead instead would be exactly what the caller did not ask for.
            if (dto.ExpectedExistingLeadId.HasValue)
            {
                return new LeadIntakeResultDto
                {
                    Message = "These details no longer match an open lead, so nothing was added. " +
                              "Submit again to create a new lead."
                };
            }

            // The lead, final reference and queued notifications commit together, in the
            // surrounding intake transaction. A replay must never find a lead whose initial
            // notification was rolled back or skipped.
            var lead = BuildLead(dto, source, normalizedPhone, normalizedWhatsapp, normalizedEmail, actor, isExternal);

            await ApplyInitialAssignmentAsync(lead, dto, actor, cancellationToken);

            _context.Leads.Add(lead);
            if (isExternal)
                AddExternalReceipt(lead, dto);

            LeadTimeline.Record(_context, lead, LeadActivityType.LeadCreated,
                $"Lead created from {source.Name}.", actor, a => a.Notes = dto.Notes);
            LeadTimeline.Record(_context, lead, LeadActivityType.SourceRecorded,
                $"Source recorded: {source.Name}.", actor,
                a => a.NewValue = BuildSourceTrail(dto, source));

            await SaveNewLeadAsync(lead, cancellationToken, c => NotifyNewLeadAsync(lead, c));

            return new LeadIntakeResultDto
            {
                Match = match,
                Message = match?.CustomerId != null
                    ? $"Lead created. Note: an existing customer ({match.CustomerName}) matches these details."
                    : "Lead created.",
                Lead = await LoadResponseAsync(lead.Id, cancellationToken)
            };
        }

        /// <summary>
        /// A lead must be reachable, but what counts as reachable depends on where it came from.
        ///
        /// Someone typing a lead into DAMS has the person in front of them, so requiring a phone,
        /// WhatsApp or email costs nothing and prevents unusable records. An ad platform, by
        /// contrast, hands over whatever the person chose to fill in; rejecting those would throw
        /// away a real enquiry we were paid to receive, and the provider's own id keeps the record
        /// identifiable regardless.
        /// </summary>
        private static void EnsureContactable(
            bool isExternal, string? normalizedPhone, string? normalizedWhatsapp, string? normalizedEmail, string? suppliedPhone)
        {
            if (isExternal)
                return;

            if (normalizedPhone != null || normalizedWhatsapp != null || normalizedEmail != null)
                return;

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(suppliedPhone)
                    ? "A lead needs at least one way to reach the person: a phone number, a WhatsApp number, or an email address."
                    : "That phone number is too short to be usable. Correct it, or record a WhatsApp number or email address instead.");
        }

        private Lead BuildLead(
            LeadIntakeDto dto,
            LeadSource source,
            string? normalizedPhone,
            string? normalizedWhatsapp,
            string? normalizedEmail,
            LeadUserContext? actor,
            bool isExternal)
        {
            var externalProvider = isExternal ? LeadContactNormalizer.Clean(dto.ExternalProvider) : null;

            return new Lead
            {
                FirstName = dto.FirstName.Trim(),
                LastName = LeadContactNormalizer.Clean(dto.LastName),
                // Keep the display value only when it normalised to something usable, so a
                // rejected number never lingers as if it were dialable.
                Phone = normalizedPhone == null ? null : LeadContactNormalizer.Clean(dto.Phone),
                NormalizedPhone = normalizedPhone,
                WhatsappNumber = normalizedWhatsapp == null ? null : LeadContactNormalizer.Clean(dto.WhatsappNumber),
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
                ExternalLeadId = isExternal ? LeadContactNormalizer.Clean(dto.ExternalLeadId) : null,
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
                NotificationType.LeadCreated,
                $"New lead: {name}",
                $"{name} arrived through {lead.Source?.Name ?? "an enquiry channel"}.",
                "created",
                cancellationToken: cancellationToken);

            if (lead.AssignedEmployeeId.HasValue)
                await NotifyOwnerAsync(lead, NotificationType.LeadAssigned,
                    $"Lead assigned: {name}", "This lead is now yours to work.", "assigned",
                    cancellationToken);
        }

        private async Task NotifyOwnerAsync(
            Lead lead, NotificationType type, string title, string? body, string suffix,
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
            int leadId,
            LeadIntakeDto dto,
            LeadSource source,
            LeadUserContext? actor,
            bool isExternal,
            CancellationToken cancellationToken,
            SubmissionAttribution? attribution = null)
        {
            // Repeat enrichment is a write. Recheck scope here rather than trusting the
            // duplicate lookup above: ownership can change between that lookup and this load.
            var lead = actor == null
                ? await _context.Leads.FirstAsync(l => l.Id == leadId, cancellationToken)
                : await LoadForWriteAsync(leadId, actor, cancellationToken);

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

            // A later enquiry can complete a lead that arrived without a phone number — the
            // common case being an ad-platform lead the person then follows up on properly.
            if (lead.NormalizedPhone == null)
            {
                var incomingPhone = LeadContactNormalizer.NormalizeUsablePhoneOrNull(dto.Phone);
                if (incomingPhone != null)
                {
                    lead.Phone = LeadContactNormalizer.Clean(dto.Phone);
                    lead.NormalizedPhone = incomingPhone;
                }
            }

            if (lead.NormalizedWhatsapp == null)
            {
                var incomingWhatsapp = LeadContactNormalizer.NormalizeUsablePhoneOrNull(dto.WhatsappNumber);
                if (incomingWhatsapp != null)
                {
                    lead.WhatsappNumber = LeadContactNormalizer.Clean(dto.WhatsappNumber);
                    lead.NormalizedWhatsapp = incomingWhatsapp;
                }
            }

            if (lead.Email == null && !string.IsNullOrWhiteSpace(dto.Email))
            {
                lead.Email = LeadContactNormalizer.NormalizeEmail(dto.Email);
                lead.NormalizedEmail = lead.Email;
            }

            if (lead.PurchaseIntent == LeadPurchaseIntent.Unknown)
                lead.PurchaseIntent = dto.PurchaseIntent;

            if (isExternal)
                AddExternalReceipt(lead, dto, attribution);

            // Adopt the provider reference when the lead has none, so replaying this exact
            // submission later is recognised as already ingested rather than re-enriching.
            var provider = LeadContactNormalizer.Clean(dto.ExternalProvider);
            var externalId = LeadContactNormalizer.Clean(dto.ExternalLeadId);
            if (isExternal && provider != null && externalId != null && lead.ExternalLeadId == null)
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

            await NotifyOwnerAsync(lead, NotificationType.LeadCreated,
                $"Repeat enquiry: {FullName(lead)}",
                $"A new enquiry arrived through {source.Name} for a lead you own.",
                $"repeat:{DateTime.UtcNow:yyyyMMddHHmm}", cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            return await LoadResponseRequiredAsync(lead.Id, cancellationToken);
        }

        private void AddExternalReceipt(Lead lead, LeadIntakeDto dto, SubmissionAttribution? attribution = null)
        {
            var provider = LeadContactNormalizer.Clean(dto.ExternalProvider);
            var externalId = LeadContactNormalizer.Clean(dto.ExternalLeadId);
            if (provider == null || externalId == null)
                return;

            var submission = new LeadExternalSubmission
            {
                Lead = lead,
                LeadId = lead.Id,
                Provider = provider,
                ExternalLeadId = externalId,
                ExternalFormReference = LeadContactNormalizer.Clean(dto.ExternalFormReference),
                ExternalSubmittedAt = dto.ExternalSubmittedAt,
                ReceivedAt = DateTime.UtcNow
            };
            attribution?.ApplyTo(submission);
            _context.LeadExternalSubmissions.Add(submission);
        }

        /// <summary>
        /// The best match for these details, plus every open lead they match. More than one open
        /// lead means the details disagree about who this is — the phone matching one person and
        /// the email another — and the caller must not pick one on the person's behalf.
        /// </summary>
        private async Task<(LeadDuplicateMatchDto? Match, List<LeadDuplicateMatchDto> OpenLeads)> FindDuplicateAsync(
            string? normalizedPhone,
            string? normalizedWhatsapp,
            string? normalizedEmail,
            CancellationToken cancellationToken)
        {
            // Nothing to compare on. Without this guard the null checks below would match this
            // lead against every other lead that also has no contact details — two anonymous
            // Meta enquiries are not the same person.
            if (normalizedPhone == null && normalizedWhatsapp == null && normalizedEmail == null)
                return (null, []);

            // Closed leads are history: a fresh enquiry from someone we lost last year is a
            // genuinely new opportunity, so only open leads count as duplicates. A number is the
            // same person whichever field it was entered in, so each incoming number is compared
            // with both the phone and the WhatsApp field.
            var openLeads = await _context.Leads
                .AsNoTracking()
                .Where(l => !LeadStageRules.ClosedStages.Contains(l.Stage))
                .Where(l =>
                    (normalizedPhone != null && (l.NormalizedPhone == normalizedPhone || l.NormalizedWhatsapp == normalizedPhone))
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
                // Bounded: a conflict needs only to be seen, not enumerated in full.
                .Take(10)
                .ToListAsync(cancellationToken);

            if (openLeads.Count > 0)
            {
                var matches = openLeads.Select(openLead => new LeadDuplicateMatchDto
                {
                    MatchedOn = MatchedOn(normalizedPhone, normalizedWhatsapp, openLead.NormalizedPhone, openLead.NormalizedWhatsapp),
                    LeadId = openLead.Id,
                    LeadReference = openLead.LeadReference,
                    LeadStage = openLead.Stage,
                    LeadOwnerName = openLead.OwnerName
                }).ToList();

                return (matches[0], matches);
            }

            // No open lead, but this may still be a customer we already sold to. Report it so
            // staff can link rather than duplicate; it does not block lead creation.
            // Customer phones keep their local form ("03001234567") while lead numbers are
            // stored without the trunk/country prefix, so match on the trailing digits.
            var customer = await _context.Customers
                .AsNoTracking()
                .Where(c => (normalizedPhone != null && c.Phone.EndsWith(normalizedPhone))
                            || (normalizedEmail != null && c.Email == normalizedEmail))
                .Select(c => new { c.Id, c.FullName, c.Phone, c.Email })
                .FirstOrDefaultAsync(cancellationToken);

            if (customer == null)
                return (null, []);

            return (new LeadDuplicateMatchDto
            {
                MatchedOn = normalizedPhone != null && customer.Phone.EndsWith(normalizedPhone, StringComparison.Ordinal) ? "phone" : "email",
                CustomerId = customer.Id,
                CustomerName = customer.FullName
            }, []);
        }

        /// <summary>Which of the incoming details a lead matched on. A number matches either of the
        /// lead's number fields; anything else that matched was the email.</summary>
        private static string MatchedOn(
            string? normalizedPhone, string? normalizedWhatsapp, string? leadPhone, string? leadWhatsapp) =>
            normalizedPhone != null && (leadPhone == normalizedPhone || leadWhatsapp == normalizedPhone) ? "phone"
            : normalizedWhatsapp != null && (leadWhatsapp == normalizedWhatsapp || leadPhone == normalizedWhatsapp) ? "whatsapp"
            : "email";

        private static string DescribeMatchedOn(string matchedOn) => matchedOn switch
        {
            "phone" => "phone number",
            "whatsapp" => "WhatsApp number",
            _ => "email address"
        };

        private static string DescribeLeads(IEnumerable<LeadDuplicateMatchDto> leads) =>
            string.Join(", ", leads.Select(l => $"{l.LeadReference} by {l.MatchedOn}"));

        // ── Held enquiries ──────────────────────────────────────────────────────────

        private async Task<LeadIntakeResultDto> HoldForReviewAsync(
            LeadIntakeDto dto,
            string? provider,
            string? externalId,
            bool isExternal,
            List<LeadDuplicateMatchDto> candidates,
            CancellationToken cancellationToken)
        {
            var hold = new LeadIntakeHold
            {
                Provider = isExternal ? provider : null,
                ExternalLeadId = isExternal ? externalId : null,
                PayloadJson = JsonSerializer.Serialize(dto),
                IsExternal = isExternal,
                CandidateLeadIds = string.Join(',', candidates.Select(c => c.LeadId!.Value)),
                ReceivedAt = DateTime.UtcNow
            };

            var holdId = 0;
            if (hold.Provider == null)
            {
                // Without a provider id nothing identifies a retry, except that it is the same
                // enquiry word for word. An identical one already waiting is that retry.
                var payload = hold.PayloadJson;
                holdId = await _context.LeadIntakeHolds
                    .AsNoTracking()
                    .Where(h => h.Status == LeadIntakeHoldStatus.Open && h.Provider == null && h.PayloadJson == payload)
                    .Select(h => h.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (holdId == 0)
            {
                _context.LeadIntakeHolds.Add(hold);
                await SaveNewHoldAsync(hold, cancellationToken);
                holdId = hold.Id;
            }

            // No lead details in the result: it goes back to an external caller, which has no
            // business learning which leads exist. Administrators see them in the review list.
            return new LeadIntakeResultDto
            {
                IdentityConflict = true,
                HeldForReview = true,
                HoldId = holdId,
                Message = "These details match more than one open lead. The enquiry was kept for an administrator " +
                          "to add to the right lead; no lead was changed."
            };
        }

        private async Task SaveNewHoldAsync(LeadIntakeHold hold, CancellationToken cancellationToken)
        {
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (hold.Provider != null && IsHoldDuplicate(ex))
            {
                // Two copies of the same webhook arrived at once and the other made the hold.
                _context.Entry(hold).State = EntityState.Detached;
                var (heldProvider, heldExternalId) = (hold.Provider, hold.ExternalLeadId);
                hold.Id = await _context.LeadIntakeHolds
                    .AsNoTracking()
                    .Where(h => h.Provider == heldProvider && h.ExternalLeadId == heldExternalId)
                    .Select(h => h.Id)
                    .FirstAsync(cancellationToken);
            }
        }

        public async Task<LeadIntakeHoldListDto> GetIntakeHoldsAsync(
            LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanResolveIntakeHolds(ctx);

            var waiting = _context.LeadIntakeHolds
                .AsNoTracking()
                .Where(h => h.Status == LeadIntakeHoldStatus.Open);
            var totalWaiting = await waiting.CountAsync(cancellationToken);
            // Oldest first and bounded; the total says when there is more behind these.
            var holds = await waiting
                .OrderBy(h => h.ReceivedAt)
                .ThenBy(h => h.Id)
                .Take(IntakeHoldPageSize)
                .ToListAsync(cancellationToken);

            var candidateIds = holds.SelectMany(CandidateIds).Distinct().ToList();
            var leads = await _context.Leads
                .AsNoTracking()
                .Where(l => candidateIds.Contains(l.Id))
                .Select(l => new
                {
                    l.Id,
                    l.LeadReference,
                    l.Stage,
                    l.NormalizedPhone,
                    l.NormalizedWhatsapp,
                    Name = (l.FirstName + " " + (l.LastName ?? "")).Trim(),
                    OwnerName = l.AssignedEmployee != null ? l.AssignedEmployee.FullName : null
                })
                .ToDictionaryAsync(l => l.Id, cancellationToken);
            var sources = await _context.LeadSources
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Code, s => s.Name, cancellationToken);

            var items = holds.Select(hold =>
            {
                var payload = ReadHoldPayload(hold);
                var phone = LeadContactNormalizer.NormalizeUsablePhoneOrNull(payload.Phone);
                var whatsapp = LeadContactNormalizer.NormalizeUsablePhoneOrNull(payload.WhatsappNumber);

                return new LeadIntakeHoldDto
                {
                    Id = hold.Id,
                    ReceivedAt = hold.ReceivedAt,
                    Provider = hold.Provider,
                    SourceName = payload.SourceCode != null && sources.TryGetValue(payload.SourceCode, out var name) ? name : payload.SourceCode,
                    FirstName = payload.FirstName,
                    LastName = payload.LastName,
                    Phone = payload.Phone,
                    WhatsappNumber = payload.WhatsappNumber,
                    Email = payload.Email,
                    CampaignName = payload.CampaignName,
                    Notes = payload.Notes,
                    BookingRequestId = hold.BookingRequestId,
                    Candidates = CandidateIds(hold)
                        .Where(leads.ContainsKey)
                        .Select(id => leads[id])
                        .Select(l => new LeadIntakeHoldCandidateDto
                        {
                            LeadId = l.Id,
                            LeadReference = l.LeadReference,
                            LeadName = l.Name,
                            LeadStage = l.Stage,
                            LeadOwnerName = l.OwnerName,
                            MatchedOn = MatchedOn(phone, whatsapp, l.NormalizedPhone, l.NormalizedWhatsapp),
                            IsOpen = !LeadStageRules.IsClosed(l.Stage)
                        })
                        .ToList()
                };
            }).ToList();

            return new LeadIntakeHoldListDto { TotalWaiting = totalWaiting, Items = items };
        }

        /// <summary>
        /// Adds a held enquiry to the lead an administrator chose, or dismisses it. Everything —
        /// the enrichment, the hold's outcome and any waiting booking request's link — is written
        /// by one save, so the decision either happens completely or not at all.
        /// </summary>
        public async Task<LeadResponseDto?> ResolveIntakeHoldAsync(
            int holdId, ResolveLeadIntakeHoldDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanResolveIntakeHolds(ctx);

            if (dto.Dismiss == dto.LeadId.HasValue)
                throw new InvalidOperationException("Choose the lead to add this enquiry to, or dismiss it.");

            var hold = await _context.LeadIntakeHolds.FirstOrDefaultAsync(h => h.Id == holdId, cancellationToken)
                ?? throw new InvalidOperationException("That held enquiry was not found.");

            if (hold.Status != LeadIntakeHoldStatus.Open)
                throw new InvalidOperationException("This enquiry has already been dealt with.");

            hold.ResolvedByUserId = ctx.UserId;
            hold.ResolvedAt = DateTime.UtcNow;
            hold.ResolutionNotes = LeadContactNormalizer.LimitOrNull(dto.Notes, 1000);

            if (dto.Dismiss)
            {
                if (hold.BookingRequestId is { } waitingRequestId
                    && await _context.BookingRequests.AnyAsync(
                        r => r.Id == waitingRequestId && r.Status == BookingRequestStatus.Pending, cancellationToken))
                    throw new InvalidOperationException(
                        "A website booking request is waiting on this enquiry, so it cannot be dismissed. Choose the lead it belongs to.");

                hold.Status = LeadIntakeHoldStatus.Dismissed;
                await SaveHoldDecisionAsync(() => _context.SaveChangesAsync(cancellationToken));
                return null;
            }

            var leadId = dto.LeadId!.Value;
            if (!CandidateIds(hold).Contains(leadId))
                throw new InvalidOperationException("That lead is not one this enquiry matched.");

            var stage = await _context.Leads
                .Where(l => l.Id == leadId)
                .Select(l => (LeadStage?)l.Stage)
                .FirstOrDefaultAsync(cancellationToken);
            if (stage == null || LeadStageRules.IsClosed(stage.Value))
                throw new InvalidOperationException("That lead has been closed since. Reopen it first, or choose another.");

            var payload = ReadHoldPayload(hold);
            // Not ResolveSourceAsync: a source switched off since the enquiry arrived must not
            // strand an enquiry that was valid when it came in.
            var sourceCode = string.IsNullOrWhiteSpace(payload.SourceCode) ? DefaultSourceCode : payload.SourceCode.Trim().ToLowerInvariant();
            var source = await _context.LeadSources.FirstOrDefaultAsync(s => s.Code == sourceCode, cancellationToken)
                ?? throw new InvalidOperationException($"Lead source '{sourceCode}' no longer exists.");

            hold.Status = LeadIntakeHoldStatus.Resolved;
            hold.ResolvedLeadId = leadId;

            if (hold.BookingRequestId is { } bookingRequestId)
            {
                var request = await _context.BookingRequests.FirstOrDefaultAsync(r => r.Id == bookingRequestId, cancellationToken);
                if (request != null && request.LeadId == null)
                    request.LeadId = leadId;
            }

            // The enrichment's own save carries the hold's outcome and the request link with it.
            var attribution = hold.AttributionJson == null
                ? null
                : JsonSerializer.Deserialize<SubmissionAttribution>(hold.AttributionJson);

            return await SaveHoldDecisionAsync(() =>
                EnrichExistingLeadAsync(leadId, payload, source, ctx, hold.IsExternal, cancellationToken, attribution));
        }

        /// <summary>
        /// A rejected or cancelled booking request no longer needs a lead, so its waiting hold is
        /// closed with it. Staged only: it is saved by the caller's own save of the request.
        /// </summary>
        public async Task CloseIntakeHoldsForBookingRequestAsync(
            int bookingRequestId, int? userId, string reason, CancellationToken cancellationToken = default)
        {
            var holds = await _context.LeadIntakeHolds
                .Where(h => h.BookingRequestId == bookingRequestId && h.Status == LeadIntakeHoldStatus.Open)
                .ToListAsync(cancellationToken);

            foreach (var hold in holds)
            {
                hold.Status = LeadIntakeHoldStatus.Dismissed;
                hold.ResolvedAt = DateTime.UtcNow;
                hold.ResolvedByUserId = userId;
                hold.ResolutionNotes = reason;
            }
        }

        private static async Task<T> SaveHoldDecisionAsync<T>(Func<Task<T>> save)
        {
            try
            {
                return await save();
            }
            // Either way round, another decision landed first: SQL Server reports whichever it
            // meets first — the hold's row version, or the receipt that decision already wrote.
            catch (Exception ex) when (ex is DbUpdateConcurrencyException
                                       || (ex is DbUpdateException update && IsExternalDuplicate(update)))
            {
                throw new InvalidOperationException(
                    "Someone else dealt with this enquiry or its lead at the same time. Reload and try again.");
            }
        }

        private static IEnumerable<int> CandidateIds(LeadIntakeHold hold) =>
            hold.CandidateLeadIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse);

        private static LeadIntakeDto ReadHoldPayload(LeadIntakeHold hold) =>
            JsonSerializer.Deserialize<LeadIntakeDto>(hold.PayloadJson)
            ?? throw new InvalidOperationException("The held enquiry could not be read.");

        private static bool IsHoldDuplicate(DbUpdateException ex) =>
            ex.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("LeadIntakeHolds", StringComparison.OrdinalIgnoreCase);

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
        // placeholder, as Booking does. Anything queued by beforeReferenceSaveAsync rides along
        // in the second save. The two saves are only atomic inside a transaction, which the
        // caller supplies — see RunContactWriteAtomicallyAsync.
        private async Task SaveNewLeadAsync(
            Lead lead, CancellationToken cancellationToken, Func<CancellationToken, Task>? beforeReferenceSaveAsync = null)
        {
            lead.LeadReference = $"LD-PENDING-{Guid.NewGuid():N}";
            await _context.SaveChangesAsync(cancellationToken);

            lead.LeadReference = $"LD-{lead.Id:D6}";

            if (beforeReferenceSaveAsync is not null)
                await beforeReferenceSaveAsync(cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Runs a write that decides whether a contact already has an open lead — intake, an edit
        /// of a lead's contact details, a reopen — in one transaction, joining the caller's when
        /// there is one. The API retries transient SQL errors, and a retry must rebuild from nothing: the
        /// rolled-back attempt's first save has already marked its rows as stored, so replaying it
        /// would update rows that no longer exist. The tracker is only cleared when this owns the
        /// transaction: callers that stage work of their own (the website booking-request flow,
        /// the Meta processor) always open one first.
        /// </summary>
        private async Task<T> RunContactWriteAtomicallyAsync<T>(
            Func<CancellationToken, Task<T>> intake, CancellationToken cancellationToken)
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null)
                return await intake(cancellationToken);

            var attempt = 0;
            return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (attempt++ > 0)
                    _context.ChangeTracker.Clear();

                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                var result = await intake(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }

        /// <summary>
        /// Takes, for the rest of the surrounding transaction, a SQL Server application lock for
        /// each identity a write is about to rely on: each number (phone and WhatsApp share one, since either
        /// field can match either), the email, and the provider submission. A second enquiry for
        /// the same person waits here until the first commits, then finds its lead instead of
        /// creating another. The locks live in the database, so they hold across API instances.
        ///
        /// Taken in one sorted order, so two enquiries that share some details never wait on each
        /// other in a circle. The names are hashes: contact details never appear in lock views.
        /// </summary>
        private async Task LockContactsAsync(
            string? provider, string? externalId, string? normalizedPhone, string? normalizedWhatsapp,
            string? normalizedEmail, CancellationToken cancellationToken)
        {
            if (!_context.Database.IsRelational())
                return;

            var resources = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var number in new[] { normalizedPhone, normalizedWhatsapp })
            {
                if (number != null)
                    resources.Add(ContactLockName("number", number));
            }
            if (normalizedEmail != null)
                resources.Add(ContactLockName("email", normalizedEmail));
            if (provider != null && externalId != null)
                resources.Add(ContactLockName("submission", provider + "\n" + externalId));

            await AcquireLocksAsync(resources, cancellationToken);
        }

        /// <summary>
        /// Locks one lead before enriching it. Enquiries matching the same lead through different
        /// details — one by its phone, another by its email — hold different contact locks and
        /// would otherwise update the lead together, and the second would fail on its row
        /// version. Always taken after the contact locks, and only ever one, so it cannot close a
        /// circle of waits.
        /// </summary>
        private async Task LockLeadAsync(int leadId, CancellationToken cancellationToken)
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction == null)
                return;

            await AcquireLocksAsync([ContactLockName("lead", leadId.ToString())], cancellationToken);
        }

        private async Task AcquireLocksAsync(IEnumerable<string> resources, CancellationToken cancellationToken)
        {
            var transaction = _context.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("Contact locks are only taken inside a transaction.");

            foreach (var resource in resources)
            {
                await using var command = _context.Database.GetDbConnection().CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    DECLARE @result int;
                    EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive',
                        @LockOwner = 'Transaction', @LockTimeout = @timeout;
                    SELECT @result;
                    """;
                AddParameter(command, "@resource", resource);
                AddParameter(command, "@timeout", IntakeLockTimeoutMilliseconds);

                // 0 or 1: granted (1 after waiting). Negative: timed out, cancelled or chosen as
                // a deadlock victim — nothing has been written yet, so the caller can simply retry.
                var granted = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
                if (granted < 0)
                    throw new LeadIntakeBusyException();
            }
        }

        internal static string ContactLockName(string kind, string value) =>
            "dams:lead-intake:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}:{value}")));

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
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
                    || (digits != "" && l.NormalizedPhone != null && l.NormalizedPhone.Contains(digits))
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

        public async Task<List<LeadExternalSubmissionDto>> GetExternalSubmissionsAsync(
            int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            await EnsureVisibleAsync(leadId, ctx, cancellationToken);

            var rows = await _context.LeadExternalSubmissions
                .AsNoTracking()
                .Where(s => s.LeadId == leadId)
                .OrderByDescending(s => s.ExternalSubmittedAt ?? s.ReceivedAt)
                .ThenByDescending(s => s.Id)
                .Select(s => new
                {
                    s.Id,
                    s.Provider,
                    s.Platform,
                    s.ExternalLeadId,
                    s.ExternalFormReference,
                    s.ExternalFormName,
                    s.PageName,
                    s.AdAccountExternalId,
                    s.CampaignName,
                    s.AdSetName,
                    s.AdName,
                    s.ExternalSubmittedAt,
                    s.ReceivedAt,
                    s.FieldDataJson,
                    ConnectionDisplayName = _context.ExternalIntegrationConnections
                        .Where(c => c.Id == s.ExternalIntegrationConnectionId)
                        .Select(c => c.DisplayName)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            return rows.Select(s => new LeadExternalSubmissionDto
            {
                Id = s.Id,
                Provider = s.Provider,
                Platform = s.Platform,
                ConnectionDisplayName = s.ConnectionDisplayName,
                ExternalLeadId = s.ExternalLeadId,
                ExternalFormReference = s.ExternalFormReference,
                ExternalFormName = s.ExternalFormName,
                PageName = s.PageName,
                AdAccountExternalId = s.AdAccountExternalId,
                CampaignName = s.CampaignName,
                AdSetName = s.AdSetName,
                AdName = s.AdName,
                ExternalSubmittedAt = s.ExternalSubmittedAt,
                ReceivedAt = s.ReceivedAt,
                FieldData = ReadFieldData(s.FieldDataJson)
            }).ToList();
        }

        /// <summary>
        /// The stored answers, or nothing at all. A receipt written before this feature existed
        /// has no field data, and malformed JSON is not worth failing a page load over.
        /// </summary>
        private static List<ExternalFieldAnswerDto> ReadFieldData(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<ExternalFieldAnswerDto>>(json) ?? [];
            }
            catch (System.Text.Json.JsonException)
            {
                return [];
            }
        }

        // ── Mutations ───────────────────────────────────────────────────────────────

        public async Task<LeadResponseDto> UpdateAsync(int id, UpdateLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var normalizedPhone = LeadContactNormalizer.NormalizePhoneOrNull(dto.Phone);
            if (normalizedPhone is { Length: < LeadContactNormalizer.MinUsablePhoneDigits })
                throw new InvalidOperationException("That phone number is too short to be usable.");

            var normalizedWhatsappEdit = LeadContactNormalizer.NormalizePhoneOrNull(dto.WhatsappNumber);
            if (normalizedWhatsappEdit is { Length: < LeadContactNormalizer.MinUsablePhoneDigits })
                throw new InvalidOperationException("That WhatsApp number is too short to be usable.");

            var normalizedEmailEdit = LeadContactNormalizer.NormalizeEmail(dto.Email);

            // The clash check and the write share one transaction under the same contact locks
            // intake takes, so an enquiry for the new number cannot create a lead in between.
            return await RunContactWriteAtomicallyAsync(async ct =>
            {
                await LockContactsAsync(null, null, normalizedPhone, normalizedWhatsappEdit, normalizedEmailEdit, ct);
                return await UpdateUnderLockAsync(id, dto, ctx, normalizedPhone, normalizedWhatsappEdit, normalizedEmailEdit, ct);
            }, cancellationToken);
        }

        private async Task<LeadResponseDto> UpdateUnderLockAsync(
            int id, UpdateLeadDto dto, LeadUserContext ctx, string? normalizedPhone, string? normalizedWhatsappEdit,
            string? normalizedEmailEdit, CancellationToken cancellationToken)
        {
            var lead = await LoadForWriteAsync(id, ctx, cancellationToken);

            if (LeadStageRules.IsClosed(lead.Stage))
                throw new InvalidOperationException(
                    lead.Stage == LeadStage.Won
                        ? "A converted lead is kept as history and can no longer be edited."
                        : $"This lead is {lead.Stage}. Reopen it before editing.");

            // Editing must not strand a lead with no way to reach the person, even though a
            // lead may legitimately have arrived without a phone number.
            if (normalizedPhone == null && normalizedWhatsappEdit == null && normalizedEmailEdit == null)
                throw new InvalidOperationException(
                    "A lead must keep at least one way to reach the person: a phone number, a WhatsApp number, or an email address.");

            if (dto.BudgetMin.HasValue && dto.BudgetMax.HasValue && dto.BudgetMin > dto.BudgetMax)
                throw new InvalidOperationException("The minimum budget cannot be greater than the maximum budget.");

            // Editing a contact field must not achieve what creation refuses: two open leads
            // for the same person. Checked against every channel that changed, not phone alone
            // — matching FindDuplicateAsync's rule that a number collides with either field,
            // whichever field it is entered in.
            if (normalizedPhone != null && normalizedPhone != lead.NormalizedPhone)
            {
                var clash = await _context.Leads.AnyAsync(
                    l => l.Id != lead.Id
                         && (l.NormalizedPhone == normalizedPhone || l.NormalizedWhatsapp == normalizedPhone)
                         && !LeadStageRules.ClosedStages.Contains(l.Stage), cancellationToken);

                if (clash)
                    throw new InvalidOperationException("Another open lead already uses that phone number.");
            }

            if (normalizedWhatsappEdit != null && normalizedWhatsappEdit != lead.NormalizedWhatsapp)
            {
                var clash = await _context.Leads.AnyAsync(
                    l => l.Id != lead.Id
                         && (l.NormalizedWhatsapp == normalizedWhatsappEdit || l.NormalizedPhone == normalizedWhatsappEdit)
                         && !LeadStageRules.ClosedStages.Contains(l.Stage), cancellationToken);

                if (clash)
                    throw new InvalidOperationException("Another open lead already uses that WhatsApp number.");
            }

            if (normalizedEmailEdit != null && normalizedEmailEdit != lead.NormalizedEmail)
            {
                var clash = await _context.Leads.AnyAsync(
                    l => l.Id != lead.Id
                         && l.NormalizedEmail == normalizedEmailEdit
                         && !LeadStageRules.ClosedStages.Contains(l.Stage), cancellationToken);

                if (clash)
                    throw new InvalidOperationException("Another open lead already uses that email address.");
            }

            var changes = new List<string>();
            if (lead.NormalizedPhone != normalizedPhone) changes.Add("phone");
            if (lead.Email != normalizedEmailEdit) changes.Add("email");

            lead.FirstName = dto.FirstName.Trim();
            lead.LastName = LeadContactNormalizer.Clean(dto.LastName);
            lead.Phone = normalizedPhone == null ? null : LeadContactNormalizer.Clean(dto.Phone);
            lead.NormalizedPhone = normalizedPhone;
            lead.WhatsappNumber = normalizedWhatsappEdit == null ? null : LeadContactNormalizer.Clean(dto.WhatsappNumber);
            lead.NormalizedWhatsapp = normalizedWhatsappEdit;
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
                    isReassignment ? NotificationType.LeadReassigned : NotificationType.LeadAssigned,
                    $"Lead assigned: {name}",
                    LeadContactNormalizer.Clean(dto.Reason) ?? "This lead is now yours to work.",
                    $"assign:{DateTime.UtcNow:yyyyMMddHHmmss}", cancellationToken);
            }

            if (isReassignment)
            {
                await _notifications.QueueForSupervisorsAsync(lead,
                    NotificationType.LeadReassigned,
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
                await _notifications.QueueForSupervisorsAsync(lead, NotificationType.LeadStageChanged,
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

            await _notifications.QueueForSupervisorsAsync(lead, NotificationType.LeadClosed,
                $"{FullName(lead)} marked {lead.Stage}",
                $"Reason: {reason.Name}.",
                $"closed:{lead.Stage}", cancellationToken: cancellationToken);

            await SaveWithConcurrencyGuardAsync(cancellationToken);

            return await LoadResponseRequiredAsync(lead.Id, cancellationToken);
        }

        public async Task<LeadResponseDto> ReopenAsync(int id, ReopenLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanReopen(ctx);

            return await RunContactWriteAtomicallyAsync(ct => ReopenUnderLockAsync(id, dto, ctx, ct), cancellationToken);
        }

        private async Task<LeadResponseDto> ReopenUnderLockAsync(
            int id, ReopenLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken)
        {
            var lead = await LoadForWriteAsync(id, ctx, cancellationToken);

            if (!LeadStageRules.ReopenableStages.Contains(lead.Stage))
                throw new InvalidOperationException("Only a lost or dormant lead can be reopened.");

            if (!LeadStageRules.ReopenTargets.Contains(dto.Stage))
                throw new InvalidOperationException($"A lead cannot be reopened directly into {dto.Stage}.");

            if (dto.Stage == LeadStage.Contacted && lead.LastContactAt == null)
                throw new InvalidOperationException("Reopen into New or First Contact Pending — no contact has been recorded yet.");

            // While this lead was closed, the same person may have come back and been given a new
            // open lead. Reopening this one too would leave two open leads for one person, which
            // intake and editing both refuse. Checked under the contact locks, with intake's rule.
            await LockContactsAsync(null, null, lead.NormalizedPhone, lead.NormalizedWhatsapp, lead.NormalizedEmail, cancellationToken);
            var (_, openLeads) = await FindDuplicateAsync(lead.NormalizedPhone, lead.NormalizedWhatsapp, lead.NormalizedEmail, cancellationToken);
            var other = openLeads.FirstOrDefault(m => m.LeadId != lead.Id);
            if (other != null)
                throw new InvalidOperationException(
                    $"Another open lead ({other.LeadReference}) already uses this lead's {DescribeMatchedOn(other.MatchedOn)}. " +
                    "Reopening this one would make two open leads for the same person; continue with that lead instead.");

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

            await NotifyOwnerAsync(lead, NotificationType.LeadAssigned,
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
            {
                if (!await _context.Bookings.AnyAsync(b => b.Id == lead.ConvertedBookingId.Value
                                                       && b.UnitId == dto.UnitId, cancellationToken))
                    throw new InvalidOperationException("This lead was already converted for a different unit.");
                return await BuildExistingConversionAsync(lead, cancellationToken);
            }

            LeadStageRules.EnsureCanConvert(lead.Stage);

            // A *new* customer record requires a phone number, so a lead that arrived from an ad
            // platform without one must have it filled in before it can become one. But when the
            // admin is instead attaching this lead to a Customer that already exists (dto.CustomerId
            // below), no new customer is created and the lead's own phone is never read — the
            // existing customer's own contact details stand. Blocking that path on the lead's phone
            // would refuse a conversion that has nothing to do with the missing number.
            if (!dto.CustomerId.HasValue && string.IsNullOrWhiteSpace(lead.Phone))
                throw new InvalidOperationException(
                    "Add a phone number to this lead before converting it — a customer record cannot be created without one.");

            var source = await _context.LeadSources.FirstAsync(s => s.Id == lead.LeadSourceId, cancellationToken);

            LeadConversionResultDto? result = null;

            await RunInTransactionAsync(async () =>
            {
                int customerId;
                bool customerWasCreated;

                // Resolved FIRST, before anything decides who the customer is or who owns them.
                //
                // This request is the only record of an authenticated identity in the whole
                // conversion: BookingRequest.UserId was captured from the submitter's own token
                // while they were signed in, and nothing later in this method — not the lead's
                // email, not the DTO, not the reviewing administrator — carries comparable proof.
                // Reading it after the customer had already been resolved (which is how this used
                // to run) meant the linking decision was made without the one fact that could
                // justify it, and email matching filled the gap.
                var linkedRequest = await _context.BookingRequests
                    .Where(br => br.LeadId == lead.Id
                                 && br.UnitId == dto.UnitId
                                 && br.Status == BookingRequestStatus.Pending
                                 && (dto.BookingRequestId == null || br.Id == dto.BookingRequestId))
                    .OrderBy(br => br.RequestedAt)
                    .ThenBy(br => br.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (dto.BookingRequestId.HasValue && linkedRequest == null)
                    throw new InvalidOperationException("The pending booking request does not match this lead and unit.");

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
                        // Guaranteed non-blank here: this branch only runs when dto.CustomerId
                        // was not supplied, and the check above already refused to reach the
                        // transaction at all in that case unless lead.Phone was usable.
                        lead.Phone ?? throw new InvalidOperationException(
                            "Add a phone number to this lead before converting it — a customer record cannot be created without one."),
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

                    // The single automatic path by which a login ever comes to own a customer.
                    //
                    // Both conditions are load-bearing. WasCreated, because a record that already
                    // existed belongs to whoever it always belonged to — matching this person's
                    // email or CNIC is not evidence that it is theirs, and claiming it here is
                    // exactly the hole this work closes. And a stored request with an
                    // authenticated submitter, because that is the only identity in the room that
                    // anybody ever proved.
                    //
                    // An existing unowned customer is therefore left unowned. That is deliberate:
                    // the booking proceeds, the CRM record is correct, and portal access waits for
                    // an administrator to verify the person and link it explicitly.
                    if (customerWasCreated && linkedRequest?.UserId != null && _accountLinks != null)
                    {
                        await _accountLinks.LinkNewCustomerFromBookingRequestAsync(
                            customerId, linkedRequest.Id, cancellationToken);
                    }
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
                }, ctx.UserId, cancellationToken);

                if (linkedRequest != null)
                {
                    linkedRequest.Status = BookingRequestStatus.Approved;
                    linkedRequest.ReviewedAt = DateTime.UtcNow;
                    linkedRequest.ReviewedByUserId = ctx.UserId;
                    linkedRequest.CustomerId = customerId;
                    linkedRequest.UpdatedAt = DateTime.UtcNow;

                    // On the Booking row, not on the response DTO that CreateBookingAsync just
                    // handed back — that object is thrown away at the end of this method, and with
                    // it the only trace of which website request this booking came from. Everything
                    // downstream reads the stored column: the request's "view booking" link, the
                    // portal ownership audit, and the approval notification.
                    var bookingRow = await _context.Bookings
                        .FirstAsync(b => b.Id == booking.Id, cancellationToken);
                    bookingRow.BookingRequestId = linkedRequest.Id;
                }

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

                await _notifications.QueueForSupervisorsAsync(lead, NotificationType.LeadConverted,
                    $"Lead won: {FullName(lead)}",
                    $"Booking {booking.BookingReference} created.",
                    $"converted:{booking.Id}", cancellationToken: cancellationToken);

                await NotifyOwnerAsync(lead, NotificationType.LeadConverted,
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

            // A request waiting in the held-enquiry review has no lead on purpose: its details match
            // more than one open lead. Creating one here would add a third lead for the same person.
            var pendingIds = pending.Select(br => br.Id).ToList();
            var heldRequestIds = (await _context.LeadIntakeHolds
                .AsNoTracking()
                .Where(h => h.Status == LeadIntakeHoldStatus.Open && h.BookingRequestId != null
                            && pendingIds.Contains(h.BookingRequestId.Value))
                .Select(h => h.BookingRequestId!.Value)
                .ToListAsync(cancellationToken)).ToHashSet();
            pending = pending.Where(br => !heldRequestIds.Contains(br.Id)).ToList();

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
                var existingLeadId = await _context.LeadExternalSubmissions
                    .AsNoTracking()
                    .Where(s => s.Provider == BookingRequestProvider && s.ExternalLeadId == externalId)
                    .Select(s => s.LeadId)
                    .FirstOrDefaultAsync(cancellationToken);

                var existing = existingLeadId == 0
                    ? await _context.Leads.FirstOrDefaultAsync(l => l.ExternalProvider == BookingRequestProvider
                                                                    && l.ExternalLeadId == externalId, cancellationToken)
                    : await _context.Leads.FirstOrDefaultAsync(l => l.Id == existingLeadId, cancellationToken);

                if (existing != null)
                {
                    request.LeadId = existing.Id;
                    continue;
                }

                bookingIdByRequest.TryGetValue(request.Id, out var bookingId);
                var lead = BuildLeadFromBookingRequest(request, websiteSource, otherReasonId,
                    bookingId == 0 ? null : bookingId);
                _context.Leads.Add(lead);
                _context.LeadExternalSubmissions.Add(new LeadExternalSubmission
                {
                    Lead = lead,
                    Provider = BookingRequestProvider,
                    ExternalLeadId = request.Id.ToString(),
                    ExternalSubmittedAt = request.RequestedAt,
                    ReceivedAt = DateTime.UtcNow
                });

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
                HeldForReview = heldRequestIds.Count,
                Message = $"{created} lead(s) created from {pending.Count} unlinked booking request(s)." +
                          (heldRequestIds.Count > 0
                              ? $" {heldRequestIds.Count} request(s) waiting in Held enquiries were left for an administrator."
                              : "")
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

        public async Task<int?> EnsureLeadForBookingRequestAsync(
            BookingRequest request, LeadUserContext? actor, CancellationToken cancellationToken = default)
        {
            if (request.LeadId.HasValue)
            {
                var convertedBookingId = await _context.Leads
                    .Where(l => l.Id == request.LeadId.Value)
                    .Select(l => l.ConvertedBookingId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (!convertedBookingId.HasValue || await _context.Bookings.AnyAsync(
                        b => b.Id == convertedBookingId.Value && b.BookingRequestId == request.Id,
                        cancellationToken))
                    return request.LeadId.Value;

                // Several open enquiries can share a lead, but a won lead represents one
                // conversion. Give this still-pending request its own conversion context.
                var websiteSource = await _context.LeadSources.FirstAsync(
                    s => s.Code == WebsiteSourceCode, cancellationToken);
                var lead = BuildLeadFromBookingRequest(request, websiteSource, null, null);
                var externalId = request.Id.ToString();
                if (await _context.LeadExternalSubmissions.AnyAsync(
                        s => s.Provider == BookingRequestProvider && s.ExternalLeadId == externalId,
                        cancellationToken)
                    || await _context.Leads.AnyAsync(l => l.ExternalProvider == BookingRequestProvider
                                                       && l.ExternalLeadId == externalId, cancellationToken))
                {
                    // Keep the original submission attribution and receipt as history.
                    lead.ExternalProvider = null;
                    lead.ExternalLeadId = null;
                }

                _context.Leads.Add(lead);
                LeadTimeline.Record(_context, lead, LeadActivityType.LeadCreated,
                    $"Lead created for website booking request #{request.Id} after an earlier enquiry converted.", actor);
                await SaveNewLeadAsync(lead, cancellationToken);

                request.LeadId = lead.Id;
                return lead.Id;
            }

            var (first, last) = SplitName(request.FullName);
            var projectId = await _context.Units
                .AsNoTracking()
                .Where(u => u.Id == request.UnitId)
                .Select(u => (int?)u.ProjectId)
                .FirstOrDefaultAsync(cancellationToken);

            var intake = new LeadIntakeDto
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
            };
            var result = await IngestAsync(intake, actor, trustedExternal: true, cancellationToken: cancellationToken);

            // An administrator approving an older request reaches intake as a signed-in person,
            // so a conflict is reported rather than held — but the website visitor, whose enquiry
            // this is, is not here to choose. Hold it, so it can be resolved like any other.
            if (result.IdentityConflict && result.HoldId == null)
            {
                result = await HoldForReviewAsync(intake, LeadContactNormalizer.Clean(intake.ExternalProvider),
                    LeadContactNormalizer.Clean(intake.ExternalLeadId), isExternal: true, result.ConflictingMatches,
                    cancellationToken);
            }

            // Its details match more than one open lead. The request itself is kept; it gets its
            // lead when an administrator decides which one the enquiry belongs to.
            if (result.HoldId is { } holdId)
            {
                var hold = await _context.LeadIntakeHolds.FirstAsync(h => h.Id == holdId, cancellationToken);
                hold.BookingRequestId ??= request.Id;
                return null;
            }

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

            await _notifications.QueueForSupervisorsAsync(lead, NotificationType.LeadClosed,
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

            // Deliberately narrow. Assignment has never required a login — an employee tracked in
            // HR with no DAMS account is still a valid owner — so only the state this feature
            // introduced is refused: a login that exists but has not been activated or has been
            // switched off, whose owner cannot sign in to see the work.
            if (employee.UserId.HasValue)
            {
                var loginUsable = await _context.Users.AnyAsync(
                    u => u.UserId == employee.UserId.Value
                        && u.AccountStatus == UserAccountStatus.Active,
                    cancellationToken);

                if (!loginUsable)
                    throw new InvalidOperationException(
                        $"{employee.FullName} has not activated their DAMS login yet, so they cannot be given leads.");
            }

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
            if (!await IsVisibleAsync(leadId, ctx, cancellationToken))
                throw new LeadNotFoundException();
        }

        private async Task<bool> IsVisibleAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken)
        {
            LeadAccess.EnsureStaff(ctx);

            return await LeadAccess.Scope(_context.Leads.AsNoTracking(), ctx)
                .AnyAsync(l => l.Id == leadId, cancellationToken);
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
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
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
