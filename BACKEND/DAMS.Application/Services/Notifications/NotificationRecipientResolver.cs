using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Turns an audience description into concrete user ids. Deactivated staff and closed
    /// customer records drop out here, so no broadcast path has to remember to exclude them.
    /// </summary>
    public sealed class NotificationRecipientResolver : INotificationRecipientResolver
    {
        private readonly AppDbContext _context;

        public NotificationRecipientResolver(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<int>> ResolveAsync(
            NotificationAudienceType type, NotificationAudienceSelection selection, CancellationToken cancellationToken = default)
        {
            var ids = type switch
            {
                NotificationAudienceType.SelectedUsers => await SelectedAsync(selection, cancellationToken),
                NotificationAudienceType.AllCustomers => await CustomerUsersAsync(_context.Customers, cancellationToken),
                NotificationAudienceType.AllSalesEmployees => await StaffAsync(LeadRoles.Employee, cancellationToken),
                NotificationAudienceType.AllManagers => await StaffAsync(LeadRoles.Manager, cancellationToken),
                NotificationAudienceType.AllInternalStaff => await InternalStaffAsync(cancellationToken),
                NotificationAudienceType.Team => await TeamAsync(selection.TeamId, cancellationToken),
                NotificationAudienceType.CustomersInProject => await CustomersInProjectAsync(selection.ProjectId, cancellationToken),
                NotificationAudienceType.CustomersOfBookings => await CustomersOfBookingsAsync(selection.BookingIds, cancellationToken),
                NotificationAudienceType.CustomersWithOverdueInstallments => await OverdueCustomersAsync(cancellationToken),
                NotificationAudienceType.EmployeesAssignedToLeads => await LeadOwnersAsync(selection.LeadIds, cancellationToken),
                _ => new List<int>()
            };

            return ids.Where(id => id > 0).Distinct().OrderBy(id => id).ToList();
        }

        public async Task<List<NotificationRecipientTarget>> ResolveTargetsAsync(
            NotificationAudienceType type,
            NotificationAudienceSelection selection,
            CancellationToken cancellationToken = default)
        {
            if (type != NotificationAudienceType.AllCustomers)
            {
                var userIds = await ResolveAsync(type, selection, cancellationToken);
                return userIds.Select(id => new NotificationRecipientTarget(id, null, null)).ToList();
            }

            var customers = await _context.Customers
                .AsNoTracking()
                .Where(c => c.Status == CustomerStatus.Active
                            && (c.UserId != null || (c.Email != null && c.Email != "")))
                .Select(c => new { c.UserId, c.Email, c.FullName })
                .ToListAsync(cancellationToken);

            var users = customers
                .Where(c => c.UserId is > 0)
                .Select(c => new NotificationRecipientTarget(c.UserId, null, c.FullName));

            var contacts = customers
                .Where(c => c.UserId == null && SmtpEmailSender.IsValidAddress(c.Email))
                .GroupBy(c => c.Email!.Trim().ToLowerInvariant())
                .Select(g => new NotificationRecipientTarget(null, g.Key, g.First().FullName));

            return users.Concat(contacts)
                .DistinctBy(r => r.UserId.HasValue ? $"u:{r.UserId}" : $"e:{r.Email}")
                .ToList();
        }

        public string Describe(NotificationAudienceType type, NotificationAudienceSelection selection) => type switch
        {
            NotificationAudienceType.SelectedUsers => selection.UserIds.Count == 1
                ? "One selected user"
                : $"{selection.UserIds.Count} selected users",
            NotificationAudienceType.AllCustomers => "All customers",
            NotificationAudienceType.AllSalesEmployees => "All sales employees",
            NotificationAudienceType.AllManagers => "All managers",
            NotificationAudienceType.AllInternalStaff => "All internal staff",
            NotificationAudienceType.Team => "One team",
            NotificationAudienceType.CustomersInProject => "Customers connected to a project",
            NotificationAudienceType.CustomersOfBookings => $"Customers of {selection.BookingIds.Count} booking(s)",
            NotificationAudienceType.CustomersWithOverdueInstallments => "Customers with overdue installments",
            NotificationAudienceType.EmployeesAssignedToLeads => $"Employees assigned to {selection.LeadIds.Count} lead(s)",
            _ => "Unknown audience"
        };

        private async Task<List<int>> SelectedAsync(NotificationAudienceSelection selection, CancellationToken cancellationToken)
        {
            if (selection.UserIds.Count == 0)
                return new List<int>();

            var requested = selection.UserIds.Distinct().Take(5000).ToArray();

            // Only ids that are real logins survive, so a crafted list cannot inflate the
            // audience count an admin is asked to confirm.
            return await _context.Users
                .AsNoTracking()
                .Where(u => requested.Contains(u.UserId))
                .Select(u => u.UserId)
                .ToListAsync(cancellationToken);
        }

        private async Task<List<int>> CustomerUsersAsync(IQueryable<Domain.Entities.Customer> customers, CancellationToken cancellationToken) =>
            await customers
                .AsNoTracking()
                .Where(c => c.UserId != null && c.Status == CustomerStatus.Active)
                .Select(c => c.UserId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

        private async Task<List<int>> StaffAsync(string role, CancellationToken cancellationToken) =>
            await _context.Employees
                .AsNoTracking()
                .Where(e => e.UserId != null
                            && e.Status == EmployeeStatus.Active
                            && e.User!.Role.Role_name == role)
                .Select(e => e.UserId!.Value)
                .ToListAsync(cancellationToken);

        private async Task<List<int>> InternalStaffAsync(CancellationToken cancellationToken)
        {
            var employees = await _context.Employees
                .AsNoTracking()
                .Where(e => e.UserId != null && e.Status == EmployeeStatus.Active)
                .Select(e => e.UserId!.Value)
                .ToListAsync(cancellationToken);

            // Admins usually have no employee record; they are staff all the same.
            var admins = await _context.Users
                .AsNoTracking()
                .Where(u => u.Role.Role_name == LeadRoles.Admin)
                .Select(u => u.UserId)
                .ToListAsync(cancellationToken);

            return employees.Concat(admins).ToList();
        }

        private async Task<List<int>> TeamAsync(int? teamId, CancellationToken cancellationToken)
        {
            if (teamId is null or <= 0)
                return new List<int>();

            var members = await _context.Employees
                .AsNoTracking()
                .Where(e => e.TeamId == teamId && e.UserId != null && e.Status == EmployeeStatus.Active)
                .Select(e => e.UserId!.Value)
                .ToListAsync(cancellationToken);

            var manager = await _context.Teams
                .AsNoTracking()
                .Where(t => t.Id == teamId && t.ManagerEmployee != null && t.ManagerEmployee.UserId != null)
                .Select(t => t.ManagerEmployee!.UserId!.Value)
                .ToListAsync(cancellationToken);

            return members.Concat(manager).ToList();
        }

        private async Task<List<int>> CustomersInProjectAsync(int? projectId, CancellationToken cancellationToken)
        {
            if (projectId is null or <= 0)
                return new List<int>();

            return await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Unit.ProjectId == projectId
                            && b.Status != BookingStatus.Cancelled
                            && b.Customer.UserId != null
                            && b.Customer.Status == CustomerStatus.Active)
                .Select(b => b.Customer.UserId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        private async Task<List<int>> CustomersOfBookingsAsync(IReadOnlyList<int> bookingIds, CancellationToken cancellationToken)
        {
            if (bookingIds.Count == 0)
                return new List<int>();

            var ids = bookingIds.Distinct().Take(5000).ToArray();

            return await _context.Bookings
                .AsNoTracking()
                .Where(b => ids.Contains(b.Id) && b.Customer.UserId != null && b.Customer.Status == CustomerStatus.Active)
                .Select(b => b.Customer.UserId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        private async Task<List<int>> OverdueCustomersAsync(CancellationToken cancellationToken)
        {
            // "Overdue" follows the finance rule: due date has passed in local business time
            // and the installment is not settled.
            var today = PakistanTime.Today;

            return await _context.Installments
                .AsNoTracking()
                .Where(i => i.DueDate < today
                            && i.Status != InstallmentStatus.Paid
                            && i.Booking.Status != BookingStatus.Cancelled
                            && i.Booking.Customer.UserId != null
                            && i.Booking.Customer.Status == CustomerStatus.Active)
                .Select(i => i.Booking.Customer.UserId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        private async Task<List<int>> LeadOwnersAsync(IReadOnlyList<int> leadIds, CancellationToken cancellationToken)
        {
            if (leadIds.Count == 0)
                return new List<int>();

            var ids = leadIds.Distinct().Take(5000).ToArray();

            return await _context.Leads
                .AsNoTracking()
                .Where(l => ids.Contains(l.Id)
                            && l.AssignedEmployee != null
                            && l.AssignedEmployee.UserId != null
                            && l.AssignedEmployee.Status == EmployeeStatus.Active)
                .Select(l => l.AssignedEmployee!.UserId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        }
    }
}
