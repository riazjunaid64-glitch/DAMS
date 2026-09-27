using System.Linq.Expressions;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;

namespace DAMS.Application.Services
{
    /// <summary>
    /// Shared read projections. Kept in one place so a list row and a detail response can
    /// never disagree about what a lead looks like.
    /// </summary>
    internal static class LeadMapping
    {
        /// <summary>
        /// A row of the leads list: what the list shows and nothing else, so no sub-selects and
        /// none of the long text. <see cref="ToResponse"/> starts from these same fields.
        /// </summary>
        public static readonly Expression<Func<Lead, LeadListItemDto>> ToListItem = l =>
            new LeadListItemDto
            {
                Id = l.Id,
                LeadReference = l.LeadReference,
                FirstName = l.FirstName,
                LastName = l.LastName,
                FullName = l.LastName == null || l.LastName == "" ? l.FirstName : l.FirstName + " " + l.LastName,
                Phone = l.Phone,
                City = l.City,
                PropertyType = l.PropertyType,
                PaymentPreference = l.PaymentPreference,
                PurchaseIntent = l.PurchaseIntent,
                SourceName = l.Source.Name,
                Stage = l.Stage,
                AssignedEmployeeId = l.AssignedEmployeeId,
                AssignedEmployeeName = l.AssignedEmployee != null ? l.AssignedEmployee.FullName : null,
                LastActivityAt = l.LastActivityAt,
                LastActivitySummary = l.LastActivitySummary,
                NextActionAt = l.NextActionAt,
                NextActionSummary = l.NextActionSummary,
                CreatedAt = l.CreatedAt
            };

        /// <summary>The whole lead: the list row's fields and the rest of the record.</summary>
        public static Expression<Func<Lead, LeadResponseDto>> ToResponse(AppDbContext context) =>
            Extend(ToListItem, l => new LeadResponseDto
            {
                WhatsappNumber = l.WhatsappNumber,
                Email = l.Email,
                Address = l.Address,
                PreferredContactMethod = l.PreferredContactMethod,
                PreferredContactTime = l.PreferredContactTime,
                LeadSourceId = l.LeadSourceId,
                SourceCode = l.Source.Code,
                SourceDetails = l.SourceDetails,
                CampaignName = l.CampaignName,
                CampaignReference = l.CampaignReference,
                AdReference = l.AdReference,
                ExternalProvider = l.ExternalProvider,
                ExternalLeadId = l.ExternalLeadId,
                ExternalFormReference = l.ExternalFormReference,
                ExternalSubmittedAt = l.ExternalSubmittedAt,
                IntegrationStatus = l.IntegrationStatus,
                IntegrationError = l.IntegrationError,
                InterestedProjectId = l.InterestedProjectId,
                InterestedProjectName = l.InterestedProject != null ? l.InterestedProject.ProjectName : null,
                InterestedUnitId = l.InterestedUnitId,
                InterestedUnitNumber = l.InterestedUnit != null ? l.InterestedUnit.UnitNumber : null,
                PreferredLocation = l.PreferredLocation,
                BudgetMin = l.BudgetMin,
                BudgetMax = l.BudgetMax,
                Notes = l.Notes,
                AssignmentState = l.AssignmentState,
                AssignedAt = l.AssignedAt,
                Qualification = l.Qualification,
                FirstContactAt = l.FirstContactAt,
                LastContactAt = l.LastContactAt,
                ConvertedAt = l.ConvertedAt,
                ConvertedCustomerId = l.ConvertedCustomerId,
                ConvertedBookingId = l.ConvertedBookingId,
                ConvertedBookingReference = l.ConvertedBooking != null ? l.ConvertedBooking.BookingReference : null,
                ClosureReasonId = l.ClosureReasonId,
                ClosureReasonName = l.ClosureReason != null ? l.ClosureReason.Name : null,
                ClosureNotes = l.ClosureNotes,
                ClosedAt = l.ClosedAt,
                ReactivateOn = l.ReactivateOn,
                BookingRequestId = context.BookingRequests
                    .Where(br => br.LeadId == l.Id)
                    .Select(br => (int?)br.Id)
                    .FirstOrDefault(),
                UpdatedAt = l.UpdatedAt,
                ConcurrencyToken = Convert.ToBase64String(l.RowVersion),
                OpenFollowUpCount = l.FollowUps.Count(f => f.Status == LeadFollowUpStatus.Pending),
                DocumentCount = l.Documents.Count
            });

        /// <summary>
        /// The single-lead read: <see cref="ToResponse"/> plus the page header. The list keeps to
        /// <see cref="ToListItem"/>, so it runs none of these sub-selects.
        /// </summary>
        public static Expression<Func<Lead, LeadDetailResponseDto>> ToDetail(AppDbContext context) =>
            Extend(ToResponse(context), PageHeader(context));

        /// <summary>
        /// One projection setting <paramref name="shared"/>'s fields and then
        /// <paramref name="extra"/>'s. Both have to stay object initializers.
        /// </summary>
        private static Expression<Func<Lead, TResult>> Extend<TShared, TResult>(
            Expression<Func<Lead, TShared>> shared, Expression<Func<Lead, TResult>> extra)
            where TResult : TShared
        {
            if (shared.Body is not MemberInitExpression sharedInit)
                throw new InvalidOperationException($"The {typeof(TShared).Name} projection must stay an object initializer.");

            var lead = shared.Parameters[0];
            if (new ReplaceParameterVisitor(extra.Parameters[0], lead).Visit(extra.Body) is not MemberInitExpression extraInit)
                throw new InvalidOperationException($"The {typeof(TResult).Name} projection must stay an object initializer.");

            return Expression.Lambda<Func<Lead, TResult>>(
                Expression.MemberInit(
                    Expression.New(typeof(TResult)),
                    sharedInit.Bindings.Concat(extraInit.Bindings)),
                lead);
        }

        /// <summary>
        /// Fields the lead page shows before any tab is opened. Not part of the list row.
        /// </summary>
        private static Expression<Func<Lead, LeadDetailResponseDto>> PageHeader(AppDbContext context) => l =>
            new LeadDetailResponseDto
            {
                Counts = new LeadPageCountsDto
                {
                    Timeline = l.Activities.Count(a => !LeadTimeline.NotShownOnTimeline.Contains(a.Type)),
                    Communications = l.Communications.Count(),
                    FollowUps = l.FollowUps.Count(f => f.Status == LeadFollowUpStatus.Pending
                                                       || f.Status == LeadFollowUpStatus.Missed
                                                       || f.Status == LeadFollowUpStatus.Completed),
                    SiteVisits = l.SiteVisits.Count(v => v.Status != LeadSiteVisitStatus.Cancelled)
                },
                LastCommunication = l.Communications
                    .OrderByDescending(c => c.OccurredAt)
                    .ThenByDescending(c => c.Id)
                    .Select(c => new LeadLastCommunicationDto
                    {
                        Channel = c.Channel,
                        Direction = c.Direction,
                        Connected = c.Connected,
                        Summary = c.Summary,
                        OccurredAt = c.OccurredAt,
                        EmployeeName = c.Employee != null ? c.Employee.FullName : null
                    })
                    .FirstOrDefault(),
                ConvertedByName = context.Users
                    .Where(u => u.UserId == l.ConvertedByUserId)
                    .Select(u => u.FullName)
                    .FirstOrDefault(),
                ConvertedUnitNumber = l.ConvertedBooking != null ? l.ConvertedBooking.Unit.UnitNumber : null,
                ClosedByName = l.Activities
                    .Where(a => a.Type == LeadActivityType.LeadLost || a.Type == LeadActivityType.LeadDormant)
                    .OrderByDescending(a => a.OccurredAt)
                    .ThenByDescending(a => a.Id)
                    .Select(a => a.PerformedByName)
                    .FirstOrDefault()
            };

        private sealed class ReplaceParameterVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _from;
            private readonly ParameterExpression _to;

            public ReplaceParameterVisitor(ParameterExpression from, ParameterExpression to)
            {
                _from = from;
                _to = to;
            }

            protected override Expression VisitParameter(ParameterExpression node) =>
                node == _from ? _to : base.VisitParameter(node);
        }

        public static readonly Expression<Func<LeadActivity, LeadActivityDto>> ToActivityDto = a =>
            new LeadActivityDto
            {
                Id = a.Id,
                LeadId = a.LeadId,
                Type = a.Type,
                Summary = a.Summary,
                Notes = a.Notes,
                Channel = a.Channel,
                PreviousValue = a.PreviousValue,
                NewValue = a.NewValue,
                CommunicationId = a.CommunicationId,
                FollowUpId = a.FollowUpId,
                SiteVisitId = a.SiteVisitId,
                DocumentId = a.DocumentId,
                CommentId = a.CommentId,
                BookingId = a.BookingId,
                CustomerId = a.CustomerId,
                PerformedByUserId = a.PerformedByUserId,
                PerformedByName = a.PerformedByName,
                IsSystemGenerated = a.IsSystemGenerated,
                OccurredAt = a.OccurredAt
            };

        public static readonly Expression<Func<LeadFollowUp, LeadFollowUpDto>> ToFollowUpDto = f =>
            new LeadFollowUpDto
            {
                Id = f.Id,
                LeadId = f.LeadId,
                LeadReference = f.Lead.LeadReference,
                LeadName = f.Lead.LastName == null || f.Lead.LastName == ""
                    ? f.Lead.FirstName
                    : f.Lead.FirstName + " " + f.Lead.LastName,
                Type = f.Type,
                AssignedEmployeeId = f.AssignedEmployeeId,
                AssignedEmployeeName = f.AssignedEmployee != null ? f.AssignedEmployee.FullName : null,
                Title = f.Title,
                Notes = f.Notes,
                DueAt = f.DueAt,
                RemindAt = f.RemindAt,
                Priority = f.Priority,
                Status = f.Status,
                CompletedAt = f.CompletedAt,
                Outcome = f.Outcome,
                CreatedAt = f.CreatedAt
            };

        public static readonly Expression<Func<LeadSiteVisit, LeadSiteVisitDto>> ToSiteVisitDto = v =>
            new LeadSiteVisitDto
            {
                Id = v.Id,
                LeadId = v.LeadId,
                LeadReference = v.Lead.LeadReference,
                LeadName = v.Lead.LastName == null || v.Lead.LastName == ""
                    ? v.Lead.FirstName
                    : v.Lead.FirstName + " " + v.Lead.LastName,
                ProjectId = v.ProjectId,
                ProjectName = v.Project != null ? v.Project.ProjectName : null,
                UnitId = v.UnitId,
                UnitNumber = v.Unit != null ? v.Unit.UnitNumber : null,
                AssignedEmployeeId = v.AssignedEmployeeId,
                AssignedEmployeeName = v.AssignedEmployee != null ? v.AssignedEmployee.FullName : null,
                ScheduledAt = v.ScheduledAt,
                MeetingLocation = v.MeetingLocation,
                CustomerAttendees = v.CustomerAttendees,
                InternalAttendees = v.InternalAttendees,
                Status = v.Status,
                RemindAt = v.RemindAt,
                Notes = v.Notes,
                Outcome = v.Outcome,
                OutcomeNotes = v.OutcomeNotes,
                CustomerFeedback = v.CustomerFeedback,
                NextAction = v.NextAction,
                CompletedAt = v.CompletedAt,
                OriginalScheduledAt = v.OriginalScheduledAt,
                RescheduleCount = v.RescheduleCount,
                CancellationReason = v.CancellationReason,
                CreatedAt = v.CreatedAt
            };

        public static readonly Expression<Func<LeadDocument, LeadDocumentDto>> ToDocumentDto = d =>
            new LeadDocumentDto
            {
                Id = d.Id,
                LeadId = d.LeadId,
                CommunicationId = d.CommunicationId,
                Category = d.Category,
                FileName = d.OriginalFileName,
                ContentType = d.ContentType,
                FileSize = d.FileSize,
                Description = d.Description,
                UploadedByUserId = d.UploadedByUserId,
                UploadedByName = d.UploadedByName,
                UploadedAt = d.UploadedAt
            };

        public static readonly Expression<Func<LeadCommunication, LeadCommunicationDto>> ToCommunicationDto = c =>
            new LeadCommunicationDto
            {
                Id = c.Id,
                LeadId = c.LeadId,
                Channel = c.Channel,
                Direction = c.Direction,
                OccurredAt = c.OccurredAt,
                EmployeeId = c.EmployeeId,
                EmployeeName = c.Employee != null ? c.Employee.FullName : null,
                Summary = c.Summary,
                CustomerResponse = c.CustomerResponse,
                NextAction = c.NextAction,
                NextActionAt = c.NextActionAt,
                FollowUpId = c.FollowUpId,
                ExternalProvider = c.ExternalProvider,
                Connected = c.Connected,
                CreatedAt = c.CreatedAt,
                Attachments = c.Attachments.Select(d => new LeadDocumentDto
                {
                    Id = d.Id,
                    LeadId = d.LeadId,
                    CommunicationId = d.CommunicationId,
                    Category = d.Category,
                    FileName = d.OriginalFileName,
                    ContentType = d.ContentType,
                    FileSize = d.FileSize,
                    Description = d.Description,
                    UploadedByUserId = d.UploadedByUserId,
                    UploadedByName = d.UploadedByName,
                    UploadedAt = d.UploadedAt
                }).ToList()
            };

        public static readonly Expression<Func<LeadComment, LeadCommentDto>> ToCommentDto = c =>
            new LeadCommentDto
            {
                Id = c.Id,
                LeadId = c.LeadId,
                ParentCommentId = c.ParentCommentId,
                Body = c.Body,
                IsManagerReviewRequest = c.IsManagerReviewRequest,
                IsDecisionRecord = c.IsDecisionRecord,
                AuthorUserId = c.AuthorUserId,
                AuthorName = c.AuthorName,
                CreatedAt = c.CreatedAt,
                Mentions = c.Mentions.Select(m => new LeadMentionDto
                {
                    UserId = m.MentionedUserId,
                    Name = m.MentionedUser != null ? m.MentionedUser.FullName : null
                }).ToList()
            };
    }
}
