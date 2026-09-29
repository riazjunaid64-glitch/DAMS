using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Services;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// KAN-59. A follow-up, visit or call used to commit its own row, then update the lead's next
/// action in a later save. A concurrent edit failed that later save and left the row behind, so
/// the user's retry created a second one. These tests move the lead's rowversion from another
/// connection after any insert that has already committed, and require the action to finish as
/// one row — or to fail with nothing written.
/// </summary>
public sealed class LeadActionAtomicitySqlTests
{
    [SqlServerFact]
    public async Task LeadActions_DoNotKeepARowWhenALaterSaveLosesTheRace_OnRealSqlServer()
    {
        await using var database = await SqlServerProductionInvariantTests.CreateDatabaseAsync();
        var options = Options(database.ConnectionString);
        int salesEmployeeId;
        LeadUserContext admin;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var adminUser = new User
            {
                RoleId = 1, FullName = "SQL admin", Email = "admin-kan59@dams.test",
                Password = "test-hash", AccountStatus = UserAccountStatus.Active
            };
            var sales = new User
            {
                RoleId = 4, FullName = "Sana Sales", Email = "sana-kan59@dams.test",
                Password = "test-hash", AccountStatus = UserAccountStatus.Active
            };
            db.Users.AddRange(adminUser, sales);
            await db.SaveChangesAsync();
            var employee = new Employee
            {
                FullName = "Sana Sales", UserId = sales.UserId, JobTitle = "Sales Executive",
                JoinDate = new DateTime(2026, 1, 1), Status = EmployeeStatus.Active
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            salesEmployeeId = employee.Id;
            admin = new LeadUserContext
            {
                UserId = adminUser.UserId, Role = LeadRoles.Admin, DisplayName = "SQL admin"
            };
        }

        var followUpLead = await NewLeadAsync(options, admin, "Follow", "follow-kan59@example.com", salesEmployeeId);
        var completeLead = await NewLeadAsync(options, admin, "Complete", "complete-kan59@example.com", salesEmployeeId);
        var visitLead = await NewLeadAsync(options, admin, "Visit", "visit-kan59@example.com", salesEmployeeId);
        var callLead = await NewLeadAsync(options, admin, "Call", "call-kan59@example.com", salesEmployeeId);
        var retryLead = await NewLeadAsync(options, admin, "Retry", "retry-kan59@example.com", salesEmployeeId);

        int startingFollowUpId;
        await using (var db = new AppDbContext(options))
        {
            using var dispatcher = Dispatcher(db);
            var created = await new LeadFollowUpService(db, new LeadNotificationService(db, dispatcher)).CreateAsync(
                completeLead, FollowUp("Starting call"), admin);
            startingFollowUpId = created.Id;
        }

        await AssertAtomicAsync(database.ConnectionString, options, followUpLead,
            entity => entity is LeadFollowUp followUp && followUp.LeadId == followUpLead,
            async (followUps, _, _) =>
            {
                await followUps.CreateAsync(followUpLead, FollowUp("Confirm payment plan"), admin);
            },
            async verify =>
            {
                var followUp = Assert.Single(await verify.LeadFollowUps.Where(f => f.LeadId == followUpLead).ToListAsync());
                Assert.Equal("Confirm payment plan", followUp.Title);
                var activity = Assert.Single(await verify.LeadActivities
                    .Where(a => a.LeadId == followUpLead && a.Type == LeadActivityType.FollowUpScheduled).ToListAsync());
                Assert.Equal(followUp.Id, activity.FollowUpId);
                var lead = await verify.Leads.SingleAsync(l => l.Id == followUpLead);
                Assert.Equal(followUp.DueAt, lead.NextActionAt);
                Assert.Equal("Confirm payment plan", lead.NextActionSummary);
                Assert.Equal(1, await verify.Notifications.CountAsync(n =>
                    n.EntityType == NotificationEntityType.Lead && n.EntityId == followUpLead
                    && n.Type == NotificationType.FollowUpAssigned));
            });

        await AssertAtomicAsync(database.ConnectionString, options, completeLead,
            entity => entity is LeadFollowUp followUp && followUp.LeadId == completeLead && followUp.Id != startingFollowUpId,
            async (followUps, _, _) =>
            {
                await followUps.CompleteAsync(startingFollowUpId, new CompleteLeadFollowUpDto
                {
                    Outcome = "Spoke.",
                    NextFollowUpAt = DateTime.UtcNow.AddDays(5),
                    NextFollowUpTitle = "Collect documents"
                }, admin);
            },
            async verify =>
            {
                var rows = await verify.LeadFollowUps.Where(f => f.LeadId == completeLead).ToListAsync();
                Assert.Equal(2, rows.Count);
                Assert.Equal(LeadFollowUpStatus.Completed, rows.Single(f => f.Id == startingFollowUpId).Status);
                var next = Assert.Single(rows, f => f.Status == LeadFollowUpStatus.Pending);
                Assert.Equal("Collect documents", next.Title);
                var lead = await verify.Leads.SingleAsync(l => l.Id == completeLead);
                Assert.Equal(next.DueAt, lead.NextActionAt);
                Assert.Equal("Collect documents", lead.NextActionSummary);
                Assert.Single(await verify.LeadActivities
                    .Where(a => a.LeadId == completeLead && a.Type == LeadActivityType.FollowUpCompleted
                                && a.FollowUpId == startingFollowUpId).ToListAsync());
            });

        await AssertAtomicAsync(database.ConnectionString, options, visitLead,
            entity => entity is LeadSiteVisit visit && visit.LeadId == visitLead,
            async (_, visits, _) =>
            {
                await visits.ScheduleAsync(visitLead, new ScheduleSiteVisitDto
                {
                    ScheduledAt = DateTime.UtcNow.AddDays(3),
                    MeetingLocation = "Site office"
                }, admin);
            },
            async verify =>
            {
                var visit = Assert.Single(await verify.LeadSiteVisits.Where(v => v.LeadId == visitLead).ToListAsync());
                var activity = Assert.Single(await verify.LeadActivities
                    .Where(a => a.LeadId == visitLead && a.Type == LeadActivityType.SiteVisitScheduled).ToListAsync());
                Assert.Equal(visit.Id, activity.SiteVisitId);
                var lead = await verify.Leads.SingleAsync(l => l.Id == visitLead);
                Assert.Equal(LeadStage.SiteVisitScheduled, lead.Stage);
                Assert.Equal(visit.ScheduledAt, lead.NextActionAt);
                Assert.Equal(1, await verify.Notifications.CountAsync(n =>
                    n.EntityType == NotificationEntityType.Lead && n.EntityId == visitLead
                    && n.Type == NotificationType.SiteVisitScheduled));
            });

        await AssertAtomicAsync(database.ConnectionString, options, callLead,
            entity => entity is LeadCommunication call && call.LeadId == callLead,
            async (_, _, calls) =>
            {
                await calls.RecordAsync(callLead, new RecordLeadCommunicationDto
                {
                    Channel = LeadCommunicationChannel.Phone,
                    Direction = LeadCommunicationDirection.Outbound,
                    Summary = "No answer, will try again.",
                    Connected = false
                }, admin);
            },
            async verify =>
            {
                var call = Assert.Single(await verify.LeadCommunications.Where(c => c.LeadId == callLead).ToListAsync());
                Assert.False(call.Connected);
                Assert.Equal(0, await verify.LeadFollowUps.CountAsync(f => f.LeadId == callLead));
                var activity = Assert.Single(await verify.LeadActivities
                    .Where(a => a.LeadId == callLead && a.Type == LeadActivityType.ContactAttempt).ToListAsync());
                Assert.Equal(call.Id, activity.CommunicationId);
            });

        // The lead moves between the read and the write. The failed attempt must leave nothing,
        // and doing the same action again must create exactly one follow-up.
        var beforeRetry = await ReadAsync(options, retryLead);
        var stale = new BumpLeadBeforeWriteInterceptor(database.ConnectionString, retryLead);
        await using (var db = new AppDbContext(Options(database.ConnectionString, stale)))
        {
            using var dispatcher = Dispatcher(db);
            var followUps = new LeadFollowUpService(db, new LeadNotificationService(db, dispatcher));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                followUps.CreateAsync(retryLead, FollowUp("Retry me"), admin));
        }

        Assert.Equal(beforeRetry, await ReadAsync(options, retryLead));

        await using (var db = new AppDbContext(options))
        {
            using var dispatcher = Dispatcher(db);
            await new LeadFollowUpService(db, new LeadNotificationService(db, dispatcher))
                .CreateAsync(retryLead, FollowUp("Retry me"), admin);
        }

        await using (var verify = new AppDbContext(options))
        {
            var followUp = Assert.Single(await verify.LeadFollowUps.Where(f => f.LeadId == retryLead).ToListAsync());
            Assert.Equal("Retry me", followUp.Title);
            Assert.Single(await verify.LeadActivities
                .Where(a => a.LeadId == retryLead && a.Type == LeadActivityType.FollowUpScheduled
                            && a.FollowUpId == followUp.Id).ToListAsync());
            Assert.Equal(1, await verify.Notifications.CountAsync(n =>
                n.EntityType == NotificationEntityType.Lead && n.EntityId == retryLead
                && n.Type == NotificationType.FollowUpAssigned));
        }
    }

    private static CreateLeadFollowUpDto FollowUp(string title) => new()
    {
        Type = LeadFollowUpType.Call,
        Title = title,
        DueAt = DateTime.UtcNow.AddDays(2)
    };

    private static async Task AssertAtomicAsync(
        string connectionString,
        DbContextOptions<AppDbContext> options,
        int leadId,
        Func<object, bool> inserted,
        Func<LeadFollowUpService, LeadSiteVisitService, LeadCommunicationService, Task> action,
        Func<AppDbContext, Task> assertSuccess)
    {
        var before = await ReadAsync(options, leadId);
        var race = new BumpAfterCommittedInsertInterceptor(connectionString, leadId, inserted);
        Exception? conflict = null;
        await using (var db = new AppDbContext(Options(connectionString, race)))
        {
            using var dispatcher = Dispatcher(db);
            var notifications = new LeadNotificationService(db, dispatcher);
            var followUps = new LeadFollowUpService(db, notifications);
            var visits = new LeadSiteVisitService(db, notifications);
            var calls = new LeadCommunicationService(db, notifications, followUps);
            try
            {
                await action(followUps, visits, calls);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                conflict = ex;
            }
        }

        if (conflict != null)
        {
            Assert.Equal(before, await ReadAsync(options, leadId));
            return;
        }

        Assert.False(race.SawASaveAfterACommittedInsert);
        await using var verify = new AppDbContext(options);
        await assertSuccess(verify);
    }

    private static async Task<int> NewLeadAsync(
        DbContextOptions<AppDbContext> options, LeadUserContext admin, string firstName, string email, int employeeId)
    {
        int leadId;
        await using (var db = new AppDbContext(options))
        {
            using var dispatcher = Dispatcher(db);
            var leads = OpenLeadService(db, dispatcher);
            leadId = (await leads.IngestAsync(new LeadIntakeDto
            {
                FirstName = firstName, Email = email, SourceCode = "walk_in"
            }, admin)).Lead!.Id;
            await leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = employeeId }, admin);
        }

        return leadId;
    }

    private static async Task<Counts> ReadAsync(DbContextOptions<AppDbContext> options, int leadId)
    {
        await using var db = new AppDbContext(options);
        return new Counts(
            await db.LeadFollowUps.CountAsync(f => f.LeadId == leadId),
            await db.LeadSiteVisits.CountAsync(v => v.LeadId == leadId),
            await db.LeadCommunications.CountAsync(c => c.LeadId == leadId),
            await db.LeadActivities.CountAsync(a => a.LeadId == leadId),
            await db.Notifications.CountAsync(n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId));
    }

    private sealed record Counts(int FollowUps, int Visits, int Communications, int Activities, int Notifications);

    private static DbContextOptions<AppDbContext> Options(string connectionString, SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString,
            sql => sql.EnableRetryOnFailure());
        if (interceptor != null)
            builder.AddInterceptors(interceptor);
        return builder.Options;
    }

    private static NotificationDispatcher Dispatcher(AppDbContext db) => new(
        db, new NotificationSettingsStore(db), new NotificationRealtimeBroker(),
        TimeProvider.System, new NotificationEligibilityPolicy(db),
        NullLogger<NotificationDispatcher>.Instance);

    private static LeadService OpenLeadService(AppDbContext db, NotificationDispatcher dispatcher)
    {
        var customers = new CustomerService(db);
        return new LeadService(db, customers,
            new BookingService(db, customers, new FinanceAccountService(db)),
            new LeadNotificationService(db, dispatcher),
            Microsoft.Extensions.Options.Options.Create(new LeadAlertOptions()));
    }

    /// <summary>
    /// After a save that has already committed a new follow-up, visit or call, move the lead's
    /// rowversion from a second connection — the edit another salesperson would commit in the
    /// gap before this action's next save. A save that is still inside a transaction is not
    /// committed, so it is left alone; bumping it there would wait on the lock this action holds.
    /// </summary>
    private sealed class BumpAfterCommittedInsertInterceptor : SaveChangesInterceptor
    {
        private readonly string _connectionString;
        private readonly int _leadId;
        private readonly Func<object, bool> _inserted;
        private bool _arm;
        private bool _committedInsert;

        public BumpAfterCommittedInsertInterceptor(string connectionString, int leadId, Func<object, bool> inserted)
        {
            _connectionString = connectionString;
            _leadId = leadId;
            _inserted = inserted;
        }

        public bool SawASaveAfterACommittedInsert { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_committedInsert)
                SawASaveAfterACommittedInsert = true;

            var context = eventData.Context;
            if (context != null && !_committedInsert && context.Database.CurrentTransaction == null
                && context.ChangeTracker.Entries().Any(e => e.State == EntityState.Added && _inserted(e.Entity)))
                _arm = true;

            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            if (_arm && context?.Database.CurrentTransaction == null)
            {
                _arm = false;
                _committedInsert = true;
                await using var other = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlServer(_connectionString).Options);
                var lead = await other.Leads.SingleAsync(l => l.Id == _leadId, cancellationToken);
                lead.Notes = "Changed while the action was saving";
                await other.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    /// <summary>Another edit wins after this action has read the lead and before it writes.</summary>
    private sealed class BumpLeadBeforeWriteInterceptor : SaveChangesInterceptor
    {
        private readonly string _connectionString;
        private readonly int _leadId;
        private bool _bumped;

        public BumpLeadBeforeWriteInterceptor(string connectionString, int leadId)
        {
            _connectionString = connectionString;
            _leadId = leadId;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            if (!_bumped && context != null
                && context.ChangeTracker.Entries<Lead>().Any(e => e.Entity.Id == _leadId && e.State == EntityState.Modified)
                && context.ChangeTracker.Entries<LeadFollowUp>().Any(e =>
                    e.State == EntityState.Added && e.Entity.LeadId == _leadId))
            {
                _bumped = true;
                await using var other = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlServer(_connectionString).Options);
                var lead = await other.Leads.SingleAsync(l => l.Id == _leadId, cancellationToken);
                lead.Notes = "Changed before the follow-up was saved";
                await other.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }
}
