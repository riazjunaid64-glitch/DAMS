namespace DAMS.Application.Common
{
    /// <summary>
    /// Who is asking, resolved once per request from the token. Every inbox query is scoped
    /// to <see cref="UserId"/> and nothing accepts a recipient id from the wire, so changing
    /// an identifier in a request can never reach another person's notifications.
    /// </summary>
    public sealed class NotificationUserContext
    {
        public required int UserId { get; init; }

        public required string Role { get; init; }

        public string? DisplayName { get; init; }

        public string? Email { get; init; }

        public bool IsAdmin => string.Equals(Role, LeadRoles.Admin, StringComparison.OrdinalIgnoreCase);

        public bool IsManager => string.Equals(Role, LeadRoles.Manager, StringComparison.OrdinalIgnoreCase);

        public bool IsEmployee => string.Equals(Role, LeadRoles.Employee, StringComparison.OrdinalIgnoreCase);

        public bool IsStaff => IsAdmin || IsManager || IsEmployee;
    }

    /// <summary>Authorisation for the notification platform's own surfaces.</summary>
    public static class NotificationAccess
    {
        public static void EnsureAdmin(NotificationUserContext ctx)
        {
            if (!ctx.IsAdmin)
                throw new LeadAuthorizationException("Only an admin can manage notification settings.");
        }
    }
}
