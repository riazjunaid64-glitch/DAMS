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
        public static Expression<Func<Lead, LeadResponseDto>> ToResponse(AppDbContext context) => l =>
            new LeadResponseDto
            {
                Id = l.Id,
                LeadReference = l.LeadReference,
                FirstName = l.FirstName,
                LastName = l.LastName,
                FullName = l.LastName == null || l.LastName == "" ? l.FirstName : l.FirstName + " " + l.LastName,
                Phone = l.Phone,
                WhatsappNumber = l.WhatsappNumber,
                Email = l.Email,
                Address = l.Address,
                City = l.City,
                PreferredContactMethod = l.PreferredContactMethod,
                PreferredContactTime = l.PreferredContactTime,
                LeadSourceId = l.LeadSourceId,
                SourceCode = l.Source.Code,
                SourceName = l.Source.Name,
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
                PropertyType = l.PropertyType,
                PreferredLocation = l.PreferredLocation,
                BudgetMin = l.BudgetMin,
                BudgetMax = l.BudgetMax,
                PurchaseIntent = l.PurchaseIntent,
                PaymentPreference = l.PaymentPreference,
                Notes = l.Notes,
                AssignedEmployeeId = l.AssignedEmployeeId,
                AssignedEmployeeName = l.AssignedEmployee != null ? l.AssignedEmployee.FullName : null,
                AssignedTeamId = l.AssignedTeamId,
                AssignedTeamName = l.AssignedTeam != null ? l.AssignedTeam.Name : null,
                AssignmentState = l.AssignmentState,
                AssignedAt = l.AssignedAt,
                Stage = l.Stage,
                Qualification = l.Qualification,
                LastActivityAt = l.LastActivityAt,
                LastActivitySummary = l.LastActivitySummary,
                NextActionAt = l.NextActionAt,
                NextActionSummary = l.NextActionSummary,
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
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt,
                ConcurrencyToken = Convert.ToBase64String(l.RowVersion),
                OpenFollowUpCount = l.FollowUps.Count(f => f.Status == LeadFollowUpStatus.Pending),
                DocumentCount = l.Documents.Count
            };

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
                ExternalProvider = c.ExternalProvider,
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
