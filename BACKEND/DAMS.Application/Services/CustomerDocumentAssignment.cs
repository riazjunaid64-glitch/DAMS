using DAMS.Application.Common;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public static class CustomerDocumentAssignment
    {
        /// <summary>Adds only categories explicitly configured for future customers.</summary>
        public static async Task AddDefaultsForNewCustomerAsync(
            AppDbContext context,
            Customer customer,
            CustomerDocumentActor? actor,
            CancellationToken cancellationToken = default)
        {
            var categories = await context.CustomerDocumentCategories
                .Where(c => c.IsActive && c.AssignToNewCustomers)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var category in categories)
            {
                var requirement = FromCategory(customer, category, actor, now);
                context.CustomerDocumentRequirements.Add(requirement);
                context.CustomerDocumentAuditEntries.Add(new CustomerDocumentAuditEntry
                {
                    Customer = customer,
                    Requirement = requirement,
                    Category = category,
                    Action = CustomerDocumentAction.CategoryAssigned,
                    NewStatus = CustomerDocumentStatus.Missing,
                    Notes = "Assigned automatically to a new customer.",
                    PerformedByUserId = actor?.UserId,
                    PerformedByName = actor?.DisplayName ?? "System",
                    OccurredAt = now
                });
            }
        }

        public static CustomerDocumentRequirement FromCategory(
            Customer customer,
            CustomerDocumentCategory category,
            CustomerDocumentActor? actor,
            DateTime now) => new()
        {
            Customer = customer,
            CustomerId = customer.Id,
            Category = category,
            CategoryId = category.Id,
            Name = category.Name,
            Description = category.Description,
            IsRequired = category.IsRequiredByDefault,
            DisplayOrder = category.DisplayOrder,
            AllowedFileTypes = category.AllowedFileTypes,
            MaxFileSizeBytes = category.MaxFileSizeBytes,
            Status = CustomerDocumentStatus.Missing,
            DueDate = category.DefaultDueDays.HasValue ? now.Date.AddDays(category.DefaultDueDays.Value) : null,
            LastActionByUserId = actor?.UserId,
            LastActionByName = actor?.DisplayName ?? "System",
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
