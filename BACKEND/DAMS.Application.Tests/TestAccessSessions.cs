using DAMS.Application.Interfaces;
using DAMS.Application.Security;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Domain.Identity;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DAMS.Application.Tests;

/// <summary>
/// The JWT pipeline now checks the live login, so a bearer minted from a user that was never
/// saved is rejected. These helpers persist that login and mint the token from the saved row.
/// </summary>
internal static class TestAccessSessions
{
    private static readonly object Gate = new();

    public static string Issue(
        IServiceProvider services,
        int userId,
        string roleName,
        string email,
        string fullName,
        DateTime? emailVerifiedAt = null)
    {
        lock (Gate)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();

            var role = db.Roles.Single(candidate => candidate.Role_name == roleName);
            var user = db.Users.SingleOrDefault(candidate => candidate.UserId == userId);
            if (user == null)
            {
                user = new User { UserId = userId };
                db.Users.Add(user);
            }

            user.Email = email;
            user.NormalizedEmail = EmailIdentity.Normalize(email);
            user.FullName = fullName;
            user.RoleId = role.RoleId;
            user.AccountStatus = UserAccountStatus.Active;
            user.EmailVerifiedAt = emailVerifiedAt;

            if (roleName is "Manager" or "Employee" or "Accountant")
            {
                var employee = db.Employees.FirstOrDefault(candidate => candidate.UserId == userId);
                if (employee == null)
                {
                    db.Employees.Add(new Employee
                    {
                        UserId = userId,
                        FullName = fullName,
                        JobTitle = "Staff",
                        Department = "Sales",
                        Phone = "03" + userId.ToString("D9"),
                        JoinDate = new DateTime(2026, 1, 1),
                        Status = EmployeeStatus.Active
                    });
                }
                else
                {
                    employee.Status = EmployeeStatus.Active;
                }
            }

            db.SaveChanges();
            db.Entry(user).Reload();
            scope.ServiceProvider.GetRequiredService<IAccessSessionCache>().Invalidate(userId);

            return scope.ServiceProvider.GetRequiredService<ITokenService>()
                .GenerateAccessToken(user, roleName);
        }
    }
}
