using System.Runtime.CompilerServices;
using DAMS.Domain.Entities;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DAMS.Infrastructure.Data
{
    /// <summary>
    /// Unit rows already bump the project list from AppDbContext. A cover photo lives on
    /// ProjectMedia, which that check does not see, so the projects page would keep the old
    /// cover for the cache window. This interceptor bumps for those rows too.
    /// </summary>
    public sealed class ProjectListCacheMediaInterceptor : SaveChangesInterceptor
    {
        public static readonly ProjectListCacheMediaInterceptor Instance = new();

        // The interceptor is a singleton shared by every pooled context, so the "this save
        // touched a photo" flag has to live with the context, not on the interceptor.
        private static readonly ConditionalWeakTable<AppDbContext, StrongBox<bool>> Pending = new();

        private ProjectListCacheMediaInterceptor()
        {
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Note(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Note(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            Apply(eventData.Context);
            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Apply(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            if (eventData.Context is AppDbContext context)
                Pending.Remove(context);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            SaveChangesFailed(eventData);
            return Task.CompletedTask;
        }

        public static bool MediaRowsChanged(AppDbContext context) =>
            context.ChangeTracker.Entries<ProjectMedia>().Any(entry =>
                entry.State is EntityState.Added or EntityState.Deleted or EntityState.Modified);

        private static void Note(DbContext? context)
        {
            if (context is not AppDbContext app)
                return;
            Pending.Remove(app);
            if (MediaRowsChanged(app))
                Pending.Add(app, new StrongBox<bool>(true));
        }

        private static void Apply(DbContext? context)
        {
            if (context is not AppDbContext app || !Pending.TryGetValue(app, out _))
                return;
            Pending.Remove(app);
            if (app.Database.CurrentTransaction != null)
                app.ProjectListCachePending = true;
            else
                ProjectListCache.Bump();
        }
    }
}
