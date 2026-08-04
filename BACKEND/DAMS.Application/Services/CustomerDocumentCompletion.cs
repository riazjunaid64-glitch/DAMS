using DAMS.Application.DTOs.CustomerDocumentDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;

namespace DAMS.Application.Services
{
    /// <summary>The single completion rule used by list, detail, and checklist responses.</summary>
    public static class CustomerDocumentCompletion
    {
        public static CustomerDocumentSummaryDto Calculate(IEnumerable<CustomerDocumentRequirement> requirements)
        {
            var required = requirements.Where(r => r.IsRequired).ToList();
            var completed = required.Count(r => IsComplete(r.Status));
            var missing = required.Count(r => r.Status is CustomerDocumentStatus.Missing or CustomerDocumentStatus.Requested);
            var review = required.Count(r => r.Status is CustomerDocumentStatus.Received or CustomerDocumentStatus.UnderReview);
            var replacement = required.Count(r => r.Status is CustomerDocumentStatus.Rejected or CustomerDocumentStatus.ReplacementRequired or CustomerDocumentStatus.Expired);
            var postponed = required.Count(r => r.Status == CustomerDocumentStatus.Postponed);
            var now = DateTime.UtcNow;
            var postponedDue = required.Count(r => r.Status == CustomerDocumentStatus.Postponed
                                                    && r.PostponedUntil.HasValue
                                                    && r.PostponedUntil.Value <= now);
            var isComplete = completed == required.Count;

            return new CustomerDocumentSummaryDto
            {
                RequiredTotal = required.Count,
                CompletedRequired = completed,
                Missing = missing,
                AwaitingReview = review,
                ReplacementRequired = replacement,
                Postponed = postponed,
                PostponedDue = postponedDue,
                IsComplete = isComplete,
                CompletionPercent = required.Count == 0 ? 100 : (int)Math.Round(completed * 100d / required.Count),
                Label = required.Count == 0 ? "No required documents"
                    : replacement > 0 ? "Replacement required"
                    : postponedDue > 0 ? $"{postponedDue} postponed due"
                    : review > 0 ? $"{review} awaiting review"
                    : missing > 0 ? $"{missing} missing"
                    : postponed > 0 ? $"{postponed} postponed"
                    : isComplete ? "Documents complete"
                    : $"{completed} of {required.Count} complete"
            };
        }

        public static bool IsComplete(CustomerDocumentStatus status) =>
            status is CustomerDocumentStatus.Approved
                or CustomerDocumentStatus.Waived
                or CustomerDocumentStatus.NotApplicable;
    }
}
