using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services;

/// <summary>
/// Repairs advisory default-document assignments missed during transient failures or rolling
/// deployments. It deliberately discovers work from current state rather than a document-table
/// outbox, because an unavailable document migration must not make the core customer write depend
/// on another document table.
/// </summary>
public sealed class CustomerDocumentReconciliationService
{
    private const int BatchSize = 100;

    private readonly AppDbContext _context;
    private readonly ILogger<CustomerDocumentReconciliationService> _logger;

    public CustomerDocumentReconciliationService(
        AppDbContext context,
        ILogger<CustomerDocumentReconciliationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<int> ReconcileBatchAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _context.Customers.AsNoTracking()
            .Where(customer => customer.Status == CustomerStatus.Active
                && _context.CustomerDocumentCategories.Any(category =>
                    category.IsActive && category.AssignToNewCustomers
                    && !_context.CustomerDocumentRequirements.Any(requirement =>
                        requirement.CustomerId == customer.Id && requirement.CategoryId == category.Id)))
            .OrderBy(customer => customer.Id)
            .Select(customer => customer.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var reconciled = 0;
        foreach (var id in ids)
        {
            var customer = await _context.Customers.SingleAsync(c => c.Id == id, cancellationToken);
            await CustomerDocumentAssignment.ReconcileCustomerAsync(_context, customer, cancellationToken: cancellationToken);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                reconciled++;
            }
            catch (DbUpdateException ex)
            {
                // Another instance may have won the unique (customer, category) race. Clear only the
                // failed advisory rows and continue; the next sweep re-evaluates the actual state.
                _logger.LogInformation(ex,
                    "Document defaults for customer {CustomerId} raced with another reconciler; current state will be rechecked.", id);
                foreach (var entry in _context.ChangeTracker.Entries()
                             .Where(e => e.Entity is DAMS.Domain.Entities.CustomerDocumentRequirement
                                 or DAMS.Domain.Entities.CustomerDocumentAuditEntry)
                             .Where(e => e.State != EntityState.Unchanged)
                             .ToList())
                    entry.State = EntityState.Detached;
            }
        }

        return reconciled;
    }
}
