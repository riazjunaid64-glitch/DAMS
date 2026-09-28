using System.Security.Claims;
using DAMS.Application.Common;
using DAMS.Application.Security;
using DAMS.Application.Services;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Api.Security
{
    /// <summary>
    /// The access token is signed, so its claims stay true until it expires. This re-reads the
    /// login they were copied from and rejects the token when that login has since been disabled,
    /// demoted, or signed out. A missing version claim fails closed, the same way an old token
    /// with no proof of email verification does.
    /// </summary>
    public static class AccessTokenSessionValidator
    {
        public static async Task Validate(TokenValidatedContext context)
        {
            var principal = context.Principal;
            var userIdText = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = principal?.FindFirst(ClaimTypes.Role)?.Value;
            var versionText = principal?.FindFirst(TokenService.TokenVersionClaimType)?.Value;

            if (!int.TryParse(userIdText, out var userId) || userId <= 0
                || !int.TryParse(versionText, out var tokenVersion)
                || string.IsNullOrEmpty(role))
            {
                context.Fail("Access token is no longer valid.");
                return;
            }

            var cache = context.HttpContext.RequestServices.GetRequiredService<IAccessSessionCache>();
            if (!cache.TryGet(userId, out var stamp))
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var loaded = await LoadAsync(db, userId, context.HttpContext.RequestAborted);
                if (loaded == null)
                {
                    context.Fail("Access token is no longer valid.");
                    return;
                }

                stamp = loaded.Value;
                cache.Set(userId, stamp);
            }

            if (!Accepts(stamp, tokenVersion, role))
                context.Fail("Access token is no longer valid.");
        }

        private static async Task<AccessSessionStamp?> LoadAsync(AppDbContext db, int userId, CancellationToken cancellationToken)
        {
            var user = await db.Users.AsNoTracking()
                .Where(candidate => candidate.UserId == userId)
                .Select(candidate => new
                {
                    candidate.TokenVersion,
                    candidate.AccountStatus,
                    RoleName = candidate.Role.Role_name
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null || string.IsNullOrEmpty(user.RoleName))
                return null;

            var hasActiveEmployee = await db.Employees.AsNoTracking()
                .AnyAsync(
                    employee => employee.UserId == userId && employee.Status == EmployeeStatus.Active,
                    cancellationToken);

            return new AccessSessionStamp(user.TokenVersion, user.AccountStatus, user.RoleName, hasActiveEmployee);
        }

        private static bool Accepts(AccessSessionStamp stamp, int tokenVersion, string role) =>
            stamp.AccountStatus == UserAccountStatus.Active
            && stamp.TokenVersion == tokenVersion
            && string.Equals(stamp.RoleName, role, StringComparison.OrdinalIgnoreCase)
            && (!IsStaffRole(role) || stamp.HasActiveEmployee);

        /// <summary>
        /// Same three roles login itself refuses without an active employment record.
        /// Admin and Client are not employment.
        /// </summary>
        private static bool IsStaffRole(string role) =>
            role.Equals(AppRoles.Manager, StringComparison.OrdinalIgnoreCase)
            || role.Equals(AppRoles.Employee, StringComparison.OrdinalIgnoreCase)
            || role.Equals(AppRoles.Accountant, StringComparison.OrdinalIgnoreCase);
    }
}
