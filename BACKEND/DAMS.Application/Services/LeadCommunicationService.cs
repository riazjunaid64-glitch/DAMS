using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class LeadCommunicationService : ILeadCommunicationService
    {
        private readonly AppDbContext _context;
        private readonly ILeadNotificationService _notifications;

        public LeadCommunicationService(AppDbContext context, ILeadNotificationService notifications)
        {
            _context = context;
            _notifications = notifications;
        }

        public async Task<LeadCommunicationDto> RecordAsync(
            int leadId, RecordLeadCommunicationDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var lead = await LeadGate.LoadActiveAsync(_context, leadId, ctx, cancellationToken);

            var occurredAt = dto.OccurredAt ?? DateTime.UtcNow;
            if (occurredAt > DateTime.UtcNow.AddMinutes(5))
                throw new InvalidOperationException("A communication cannot be recorded in the future.");

            var provider = LeadContactNormalizer.Clean(dto.ExternalProvider);
            var externalMessageId = LeadContactNormalizer.Clean(dto.ExternalMessageId);

            // Channel integrations replay messages; the same provider message must produce
            // one record however many times it arrives.
            if (provider != null && externalMessageId != null)
            {
                var existingId = await _context.LeadCommunications
                    .AsNoTracking()
                    .Where(c => c.ExternalProvider == provider
                                && c.ExternalMessageId == externalMessageId
                                && c.LeadId == leadId)
                    .Select(c => c.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingId != 0)
                    return await LoadAsync(existingId, cancellationToken);

                var usedOnAnotherLead = await _context.LeadCommunications
                    .AsNoTracking()
                    .AnyAsync(c => c.ExternalProvider == provider
                                   && c.ExternalMessageId == externalMessageId, cancellationToken);
                if (usedOnAnotherLead)
                    throw new InvalidOperationException("That external message is already linked to another lead.");
            }

            var communication = new LeadCommunication
            {
                Lead = lead,
                LeadId = lead.Id,
                Channel = dto.Channel,
                Direction = dto.Direction,
                OccurredAt = occurredAt,
                EmployeeId = ctx.EmployeeId,
                RecordedByUserId = ctx.UserId,
                Summary = dto.Summary.Trim(),
                CustomerResponse = LeadContactNormalizer.Clean(dto.CustomerResponse),
                NextAction = LeadContactNormalizer.Clean(dto.NextAction),
                NextActionAt = dto.NextActionAt,
                ExternalProvider = provider,
                ExternalMessageId = externalMessageId,
                CreatedAt = DateTime.UtcNow
            };

            _context.LeadCommunications.Add(communication);

            if (dto.Connected)
            {
                // Only a conversation that actually happened counts as contact — a missed
                // call must not stop the first-response clock.
                lead.FirstContactAt ??= occurredAt;
                lead.LastContactAt = occurredAt;

                if (lead.Stage is LeadStage.New or LeadStage.FirstContactPending)
                {
                    var previous = lead.Stage;
                    lead.Stage = LeadStage.Contacted;
                    LeadTimeline.Record(_context, lead, LeadActivityType.StageChanged,
                        "Stage moved to Contacted after the first recorded conversation.", ctx,
                        a =>
                        {
                            a.PreviousValue = previous.ToString();
                            a.NewValue = LeadStage.Contacted.ToString();
                        });
                }
            }

            if (dto.NextActionAt.HasValue)
            {
                if (dto.NextActionAt <= DateTime.UtcNow.AddMinutes(-1))
                    throw new InvalidOperationException("The next action must be scheduled for a future time.");
            }

            lead.UpdatedAt = DateTime.UtcNow;

            var activity = LeadTimeline.Record(_context, lead, ActivityTypeFor(dto.Channel, dto.Connected),
                BuildSummary(dto), ctx, a =>
                {
                    a.Channel = dto.Channel;
                    a.Notes = dto.Summary.Trim();
                    a.OccurredAt = occurredAt;
                });

            await _context.SaveChangesAsync(cancellationToken);

            // The activity's link to the communication needs the generated id.
            activity.CommunicationId = communication.Id;
            await LeadGate.RefreshNextActionAsync(_context, lead.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return await LoadAsync(communication.Id, cancellationToken);
        }

        public async Task<List<LeadCommunicationDto>> GetForLeadAsync(
            int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            await LeadGate.EnsureVisibleAsync(_context, leadId, ctx, cancellationToken);

            return await _context.LeadCommunications
                .AsNoTracking()
                .Where(c => c.LeadId == leadId)
                .OrderByDescending(c => c.OccurredAt)
                .ThenByDescending(c => c.Id)
                .Select(LeadMapping.ToCommunicationDto)
                .ToListAsync(cancellationToken);
        }

        public async Task<LeadCommentDto> AddCommentAsync(
            int leadId, CreateLeadCommentDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            // Comments stay available on closed leads: a decision record about why a lead was
            // lost is exactly the kind of note people add afterwards.
            var lead = await LeadGate.LoadAsync(_context, leadId, ctx, cancellationToken);

            if (dto.ParentCommentId.HasValue)
            {
                var parentExists = await _context.LeadComments
                    .AnyAsync(c => c.Id == dto.ParentCommentId.Value && c.LeadId == leadId, cancellationToken);

                if (!parentExists)
                    throw new InvalidOperationException("The comment being replied to does not belong to this lead.");
            }

            var mentionedUserIds = dto.MentionedUserIds.Where(id => id > 0).Distinct().ToList();
            if (mentionedUserIds.Count > 0)
            {
                // Only staff can be mentioned — a mention grants access to the lead.
                var validStaff = await _context.Users
                    .AsNoTracking()
                    .Where(u => mentionedUserIds.Contains(u.UserId)
                                && (u.Role.Role_name == LeadRoles.Admin
                                    || u.Role.Role_name == LeadRoles.Manager
                                    || u.Role.Role_name == LeadRoles.Employee))
                    .Select(u => u.UserId)
                    .ToListAsync(cancellationToken);

                var invalid = mentionedUserIds.Except(validStaff).ToList();
                if (invalid.Count > 0)
                    throw new InvalidOperationException("You can only mention colleagues who work on leads.");

                foreach (var mentionedUserId in mentionedUserIds)
                    await LeadGate.EnsureEmployeeCanBeMentionedAsync(
                        _context, mentionedUserId, lead, ctx, cancellationToken);
            }

            var comment = new LeadComment
            {
                Lead = lead,
                LeadId = lead.Id,
                ParentCommentId = dto.ParentCommentId,
                Body = dto.Body.Trim(),
                IsManagerReviewRequest = dto.IsManagerReviewRequest,
                IsDecisionRecord = dto.IsDecisionRecord,
                AuthorUserId = ctx.UserId,
                AuthorName = ctx.DisplayName,
                CreatedAt = DateTime.UtcNow
            };

            foreach (var userId in mentionedUserIds)
                comment.Mentions.Add(new LeadCommentMention { MentionedUserId = userId });

            _context.LeadComments.Add(comment);

            var activity = LeadTimeline.Record(_context, lead,
                dto.ParentCommentId.HasValue ? LeadActivityType.InternalComment : LeadActivityType.InternalNote,
                dto.IsManagerReviewRequest ? "Manager review requested." : "Internal note added.",
                ctx, a => a.Notes = comment.Body);

            if (mentionedUserIds.Count > 0)
                LeadTimeline.Record(_context, lead, LeadActivityType.TeamMemberMentioned,
                    $"{mentionedUserIds.Count} team member(s) mentioned.", ctx);

            lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            activity.CommentId = comment.Id;

            var leadName = LeadService.FullName(lead);
            foreach (var userId in mentionedUserIds.Where(id => id != ctx.UserId))
            {
                await _notifications.QueueAsync(lead.Id, userId, LeadNotificationType.MentionedInComment,
                    $"You were mentioned on {leadName}", comment.Body,
                    $"mention:{comment.Id}:{userId}", cancellationToken: cancellationToken);
            }

            if (dto.IsManagerReviewRequest)
            {
                await _notifications.QueueForSupervisorsAsync(lead, LeadNotificationType.ManagerAttentionRequired,
                    $"Review requested on {leadName}", comment.Body,
                    $"review:{comment.Id}", cancellationToken: cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            return await LoadCommentAsync(comment.Id, cancellationToken);
        }

        public async Task<List<LeadCommentDto>> GetCommentsAsync(
            int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            await LeadGate.EnsureVisibleAsync(_context, leadId, ctx, cancellationToken);

            return await _context.LeadComments
                .AsNoTracking()
                .Where(c => c.LeadId == leadId)
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .Select(LeadMapping.ToCommentDto)
                .ToListAsync(cancellationToken);
        }

        private static LeadActivityType ActivityTypeFor(LeadCommunicationChannel channel, bool connected)
        {
            if (!connected)
                return LeadActivityType.ContactAttempt;

            return channel switch
            {
                LeadCommunicationChannel.Phone => LeadActivityType.CallCompleted,
                LeadCommunicationChannel.Whatsapp => LeadActivityType.WhatsappActivity,
                LeadCommunicationChannel.Email => LeadActivityType.EmailActivity,
                LeadCommunicationChannel.Sms => LeadActivityType.SmsActivity,
                LeadCommunicationChannel.Meeting or LeadCommunicationChannel.OfficeVisit
                    or LeadCommunicationChannel.SiteVisit => LeadActivityType.MeetingRecorded,
                _ => LeadActivityType.OtherCommunication
            };
        }

        private static string BuildSummary(RecordLeadCommunicationDto dto)
        {
            var direction = dto.Direction == LeadCommunicationDirection.Inbound ? "Inbound" : "Outbound";
            return dto.Connected
                ? $"{direction} {dto.Channel} recorded."
                : $"{direction} {dto.Channel} attempt — no answer.";
        }

        private async Task<LeadCommunicationDto> LoadAsync(int id, CancellationToken cancellationToken) =>
            await _context.LeadCommunications
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(LeadMapping.ToCommunicationDto)
                .FirstAsync(cancellationToken);

        private async Task<LeadCommentDto> LoadCommentAsync(int id, CancellationToken cancellationToken) =>
            await _context.LeadComments
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(LeadMapping.ToCommentDto)
                .FirstAsync(cancellationToken);
    }
}
