using System.Data.Common;
using System.Runtime.CompilerServices;
using DAMS.Application.Security;
using DAMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DAMS.Api.Security
{
    /// <summary>
    /// A login's current access token has to die in the same save that takes the access away.
    /// Clearing the refresh token only stops the next renewal; this moves
    /// <see cref="User.TokenVersion"/> so the token already in the browser no longer matches.
    /// Rotating a refresh token does not count: that is how an active session stays signed in.
    /// </summary>
    public sealed class AccessSessionSaveInterceptor : SaveChangesInterceptor
    {
        private readonly IAccessSessionCache _cache;
        private readonly ConditionalWeakTable<DbContext, HashSet<int>> _pending = new();

        public AccessSessionSaveInterceptor(IAccessSessionCache cache) => _cache = cache;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context != null)
                Remember(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context != null)
                Remember(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            Drop(eventData.Context);
            return base.SavedChanges(eventData, result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Drop(eventData.Context);
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            if (eventData.Context != null)
                _pending.Remove(eventData.Context);
            base.SaveChangesFailed(eventData);
        }

        private void Remember(DbContext context)
        {
            var affected = new HashSet<int>();
            if (context.Database.CurrentTransaction != null
                && _pending.TryGetValue(context, out var earlier))
            {
                foreach (var userId in earlier)
                    affected.Add(userId);
            }

            foreach (var entry in context.ChangeTracker.Entries<User>().ToList())
            {
                if (!SessionMustEnd(entry))
                    continue;

                if (!entry.Property(user => user.TokenVersion).IsModified)
                    entry.Entity.TokenVersion = entry.Property(user => user.TokenVersion).OriginalValue + 1;

                if (entry.Entity.UserId != 0)
                    affected.Add(entry.Entity.UserId);
            }

            foreach (var entry in context.ChangeTracker.Entries<Employee>().ToList())
                NoteEmployment(context, entry, affected);

            _pending.Remove(context);
            _pending.Add(context, affected);
        }

        private static void NoteEmployment(DbContext context, EntityEntry<Employee> entry, HashSet<int> affected)
        {
            if (entry.State == EntityState.Added)
                return;

            var statusChanged = entry.State == EntityState.Modified
                && entry.Property(employee => employee.Status).IsModified;
            var removed = entry.State == EntityState.Deleted;
            var linkChanged = entry.State == EntityState.Modified
                && entry.Property(employee => employee.UserId).IsModified;

            if (statusChanged || removed)
                Bump(context, entry.Property(employee => employee.UserId).CurrentValue, affected);

            if (!linkChanged)
                return;

            Bump(context, entry.Property(employee => employee.UserId).OriginalValue, affected);
            Bump(context, entry.Property(employee => employee.UserId).CurrentValue, affected);
        }

        private static void Bump(DbContext context, int? userId, HashSet<int> affected)
        {
            if (userId is not int id || id == 0 || !affected.Add(id))
                return;

            var user = context.Set<User>().Local.FirstOrDefault(candidate => candidate.UserId == id)
                ?? context.Set<User>().Find(id);
            if (user == null || context.Entry(user).State == EntityState.Deleted)
            {
                affected.Remove(id);
                return;
            }

            if (!context.Entry(user).Property(candidate => candidate.TokenVersion).IsModified)
                user.TokenVersion++;
        }

        private void Drop(DbContext? context)
        {
            if (context == null || context.Database.CurrentTransaction != null)
                return;

            Invalidate(context);
        }

        /// <summary>
        /// A save inside a transaction is not visible yet. Dropping the cache before commit lets
        /// the next request read the old row and cache it again for the whole window.
        /// </summary>
        internal void OnCommitted(DbContext context) => Invalidate(context);

        internal void OnRolledBack(DbContext context) => _pending.Remove(context);

        private void Invalidate(DbContext context)
        {
            if (!_pending.TryGetValue(context, out var affected))
                return;

            _pending.Remove(context);
            foreach (var userId in affected)
                _cache.Invalidate(userId);
        }

        /// <summary>
        /// Disable, role change, a new password, or the refresh token being cleared (logout).
        /// A refresh token being replaced with another one is a renewal, not an end.
        /// </summary>
        private static bool SessionMustEnd(EntityEntry<User> entry)
        {
            if (entry.State != EntityState.Modified)
                return false;

            if (entry.Property(user => user.AccountStatus).IsModified
                || entry.Property(user => user.RoleId).IsModified
                || entry.Property(user => user.Password).IsModified)
                return true;

            var refresh = entry.Property(user => user.RefreshToken);
            return refresh.IsModified && refresh.CurrentValue == null && refresh.OriginalValue != null;
        }
    }

    /// <summary>
    /// The save interceptor can see that a transaction is open, but not that it committed.
    /// This finishes the cache drop at commit, and throws the ids away on rollback.
    /// </summary>
    public sealed class AccessSessionTransactionInterceptor : DbTransactionInterceptor
    {
        private readonly AccessSessionSaveInterceptor _sessions;

        public AccessSessionTransactionInterceptor(AccessSessionSaveInterceptor sessions) =>
            _sessions = sessions;

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (eventData.Context != null)
                _sessions.OnCommitted(eventData.Context);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            TransactionCommitted(transaction, eventData);
            return Task.CompletedTask;
        }

        public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (eventData.Context != null)
                _sessions.OnRolledBack(eventData.Context);
        }

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            TransactionRolledBack(transaction, eventData);
            return Task.CompletedTask;
        }
    }
}
