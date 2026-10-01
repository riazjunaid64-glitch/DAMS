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
            if (customer.Status == CustomerStatus.Blocked)
                return;

            var existing = customer.Id == 0
                ? []
                : (await context.CustomerDocumentRequirements
                    .Where(r => r.CustomerId == customer.Id && r.CategoryId != null)
                    .Select(r => r.CategoryId!.Value)
                    .ToListAsync(cancellationToken)).ToHashSet();
            var categories = await context.CustomerDocumentCategories
                .Where(c => c.AsksEveryCustomer && !c.IsHidden && !c.IsOther && !existing.Contains(c.Id))
                .OrderBy(c => c.Id)
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
                    NewStatus = CustomerDocumentStatus.Needed,
                    Notes = "Assigned automatically to a new customer.",
                    PerformedByUserId = actor?.UserId,
                    PerformedByName = actor?.DisplayName ?? "System",
                    OccurredAt = now
                });
            }
        }

        /// <summary>
        /// Reconciles defaults idempotently. Callers still rely on the database unique index as the
        /// final guard when two application instances discover the same missing assignment.
        /// </summary>
        public static Task ReconcileCustomerAsync(
            AppDbContext context,
            Customer customer,
            CustomerDocumentActor? actor = null,
            CancellationToken cancellationToken = default) =>
            AddDefaultsForNewCustomerAsync(context, customer, actor, cancellationToken);

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
            IsRequired = true,
            DisplayOrder = category.Id,
            IsSuppressed = false,
            Status = CustomerDocumentStatus.Needed,
            LastActionByUserId = actor?.UserId,
            LastActionByName = actor?.DisplayName ?? "System",
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
