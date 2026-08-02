using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// A miniature but complete DAMS: seeded roles, sources and closure reasons, one project
/// with units, a manager and two employees on a team, plus every lead service wired to the
/// same DbContext exactly as the API wires them.
/// </summary>
internal sealed class LeadTestHarness : IAsyncDisposable
{
    public AppDbContext Db { get; }
    public LeadService Leads { get; }
    public LeadNotificationService Notifications { get; }
    public NotificationDispatcher Dispatcher { get; }
    public NotificationInboxService Inbox { get; }
    public NotificationRealtimeBroker Realtime { get; }
    public LeadCommunicationService Communications { get; }
    public LeadFollowUpService FollowUps { get; }
    public LeadSiteVisitService SiteVisits { get; }
    public LeadDocumentService Documents { get; }
    public LeadConfigurationService Configuration { get; }
    public LeadReportingService Reporting { get; }
    public LeadAlertService Alerts { get; }
    public BookingRequestService BookingRequests { get; }
    public MemoryLeadDocumentStorage DocumentStorage { get; }

    public LeadUserContext Admin { get; private set; } = null!;
    public LeadUserContext Manager { get; private set; } = null!;
    public LeadUserContext Sales { get; private set; } = null!;
    public LeadUserContext OtherSales { get; private set; } = null!;
    public LeadUserContext Client { get; private set; } = null!;

    public int AdminUserId { get; private set; }
    public int ManagerUserId { get; private set; }
    public int SalesUserId { get; private set; }
    public int OtherSalesUserId { get; private set; }
    public int ClientUserId { get; private set; }

    public int ManagerEmployeeId { get; private set; }
    public int SalesEmployeeId { get; private set; }
    public int OtherSalesEmployeeId { get; private set; }
    public int TeamId { get; private set; }
    public int ProjectId { get; private set; }
    public int UnitId { get; private set; }
    public int SecondUnitId { get; private set; }

    /// <summary>
    /// Drives the "due today", "overdue" and "inactive" rules. It starts on the real clock
    /// so it agrees with the write-side validation, and calendar-day tests move it to a
    /// deliberate instant instead of hoping the suite does not run near midnight UTC.
    /// </summary>
    public FakeClock Clock { get; }

    private LeadTestHarness(AppDbContext db, LeadAlertOptions options)
    {
        Db = db;
        var alertOptions = Options.Create(options);
        Clock = new FakeClock(DateTime.UtcNow);

        Realtime = new NotificationRealtimeBroker();
        var eligibility = new NotificationEligibilityPolicy(db);
        Dispatcher = new NotificationDispatcher(db, new NotificationSettingsStore(db), Realtime, Clock, eligibility,
            NullLogger<NotificationDispatcher>.Instance);
        Inbox = new NotificationInboxService(db, Clock, eligibility);
        Notifications = new LeadNotificationService(db, Dispatcher);
        var customers = new CustomerService(db);
        var bookings = new BookingService(db, customers);
        Leads = new LeadService(db, customers, bookings, Notifications, alertOptions);
        Communications = new LeadCommunicationService(db, Notifications);
        FollowUps = new LeadFollowUpService(db, Notifications);
        SiteVisits = new LeadSiteVisitService(db, Notifications);
        DocumentStorage = new MemoryLeadDocumentStorage();
        Documents = new LeadDocumentService(db, DocumentStorage, NullLogger<LeadDocumentService>.Instance);
        Configuration = new LeadConfigurationService(db);
        Reporting = new LeadReportingService(db, alertOptions, Clock);
        Alerts = new LeadAlertService(db, Notifications, alertOptions, Clock);
        BookingRequests = new BookingRequestService(db, Leads);
    }

    public static async Task<LeadTestHarness> CreateAsync(LeadAlertOptions? options = null)
    {
        var db = CreateContext();
        var harness = new LeadTestHarness(db, options ?? new LeadAlertOptions());
        await harness.SeedAsync();
        return harness;
    }

    public static AppDbContext CreateContext()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // The lead services run their multi-step writes inside a transaction; the
            // in-memory store has none, and ignoring the warning makes those calls no-ops
            // instead of exceptions.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        // Applies the seeded roles, lead sources and closure reasons.
        context.Database.EnsureCreated();
        return context;
    }

    private async Task SeedAsync()
    {
        var admin = NewUser("Ayesha Admin", "admin@dams.test", 1);
        var manager = NewUser("Mahmood Manager", "manager@dams.test", 3);
        var sales = NewUser("Sana Sales", "sales@dams.test", 4);
        var otherSales = NewUser("Omar Sales", "omar@dams.test", 4);
        var client = NewUser("Client Person", "client@dams.test", 2);
        Db.Users.AddRange(admin, manager, sales, otherSales, client);
        await Db.SaveChangesAsync();

        AdminUserId = admin.UserId;
        ManagerUserId = manager.UserId;
        SalesUserId = sales.UserId;
        OtherSalesUserId = otherSales.UserId;
        ClientUserId = client.UserId;

        var managerEmployee = NewEmployee("Mahmood Manager", manager.UserId);
        var salesEmployee = NewEmployee("Sana Sales", sales.UserId);
        var otherEmployee = NewEmployee("Omar Sales", otherSales.UserId);
        Db.Employees.AddRange(managerEmployee, salesEmployee, otherEmployee);
        await Db.SaveChangesAsync();

        ManagerEmployeeId = managerEmployee.Id;
        SalesEmployeeId = salesEmployee.Id;
        OtherSalesEmployeeId = otherEmployee.Id;

        var team = new Team { Name = "North Sales", ManagerEmployeeId = managerEmployee.Id, IsActive = true };
        Db.Teams.Add(team);
        await Db.SaveChangesAsync();
        TeamId = team.Id;

        managerEmployee.TeamId = team.Id;
        salesEmployee.TeamId = team.Id;
        // Omar is deliberately left off the team so cross-team access can be tested.
        await Db.SaveChangesAsync();

        var project = new Project { ProjectName = "Floria Heights", Location = "Lahore", CreatedById = admin.UserId };
        Db.Projects.Add(project);
        await Db.SaveChangesAsync();
        ProjectId = project.Id;

        var unit = NewUnit(project.Id, "A-101");
        var secondUnit = NewUnit(project.Id, "A-102");
        Db.Units.AddRange(unit, secondUnit);
        await Db.SaveChangesAsync();
        UnitId = unit.Id;
        SecondUnitId = secondUnit.Id;

        Admin = Context(admin.UserId, LeadRoles.Admin, "Ayesha Admin");
        Manager = Context(manager.UserId, LeadRoles.Manager, "Mahmood Manager", managerEmployee.Id, team.Id, new[] { team.Id });
        Sales = Context(sales.UserId, LeadRoles.Employee, "Sana Sales", salesEmployee.Id, team.Id);
        OtherSales = Context(otherSales.UserId, LeadRoles.Employee, "Omar Sales", otherEmployee.Id);
        Client = Context(client.UserId, "Client", "Client Person");
    }

    /// <summary>The same person, as the notification platform sees them.</summary>
    public NotificationUserContext Notify(LeadUserContext ctx) => new()
    {
        UserId = ctx.UserId,
        Role = ctx.Role,
        DisplayName = ctx.DisplayName,
        Email = $"user{ctx.UserId}@dams.test"
    };

    private static LeadUserContext Context(
        int userId, string role, string name, int? employeeId = null, int? teamId = null, int[]? managedTeams = null) =>
        new()
        {
            UserId = userId,
            Role = role,
            DisplayName = name,
            EmployeeId = employeeId,
            TeamId = teamId,
            ManagedTeamIds = managedTeams ?? Array.Empty<int>()
        };

    private static User NewUser(string name, string email, int roleId) =>
        new() { FullName = name, Email = email, Password = "hash", RoleId = roleId };

    private static Employee NewEmployee(string name, int userId) => new()
    {
        FullName = name,
        JobTitle = "Sales Executive",
        Department = "Sales",
        Phone = "03001112222",
        JoinDate = DateTime.UtcNow.AddYears(-1),
        Status = EmployeeStatus.Active,
        UserId = userId
    };

    private static Unit NewUnit(int projectId, string number) => new()
    {
        ProjectId = projectId,
        UnitNumber = number,
        UnitType = "2 Bed",
        FloorNumber = 1,
        Size = 1200m,
        Price = 10_000_000m,
        Status = UnitStatus.Available
    };

    // ── Convenience builders ────────────────────────────────────────────────────

    public static LeadIntakeDto Intake(
        string firstName = "Bilal",
        string phone = "0300-1234567",
        string? email = "bilal@example.com",
        string sourceCode = "walk_in") =>
        new()
        {
            FirstName = firstName,
            LastName = "Khan",
            Phone = phone,
            Email = email,
            SourceCode = sourceCode
        };

    /// <summary>Creates a lead assigned to the sales employee and already contacted.</summary>
    public async Task<int> CreateWorkedLeadAsync(string phone = "0300-1234567")
    {
        // Distinct email per phone so callers can build several worked leads without
        // tripping duplicate detection.
        var email = $"lead{LeadContactNormalizer.NormalizePhone(phone)}@example.com";
        var created = await Leads.IngestAsync(Intake(phone: phone, email: email), Admin);
        Assert.NotNull(created.Lead);
        var leadId = created.Lead!.Id;

        await Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = SalesEmployeeId }, Admin);
        await Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Phone,
            Direction = LeadCommunicationDirection.Outbound,
            Summary = "Discussed requirements and budget."
        }, Sales);

        return leadId;
    }

    public async Task<int> CreateLeadAsync(LeadIntakeDto? dto = null)
    {
        var created = await Leads.IngestAsync(dto ?? Intake(), Admin);
        return created.Lead!.Id;
    }

    public Task<Lead> LoadLeadAsync(int leadId)
    {
        Db.ChangeTracker.Clear();
        return Db.Leads.AsNoTracking().FirstAsync(l => l.Id == leadId);
    }

    public Task<List<LeadActivity>> TimelineAsync(int leadId) =>
        Db.LeadActivities.AsNoTracking().Where(a => a.LeadId == leadId).ToListAsync();

    public ValueTask DisposeAsync() => Db.DisposeAsync();

    /// <summary>A clock the tests can hold still or move forward deliberately.</summary>
    internal sealed class FakeClock : TimeProvider
    {
        public FakeClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; private set; }

        public void Set(DateTime utcNow) => UtcNow = utcNow;

        /// <summary>Tomorrow at a fixed hour — always in the future and never near midnight.</summary>
        public static DateTime TomorrowAt(int hour) => DateTime.UtcNow.Date.AddDays(1).AddHours(hour);

        public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    }

    internal sealed class MemoryLeadDocumentStorage : ILeadDocumentStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public bool FailSaves { get; set; }

        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            if (FailSaves) throw new IOException("Simulated storage failure");
            var key = $"{Guid.NewGuid():N}{extension}";
            content.Position = 0;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Files[key] = buffer.ToArray();
            return key;
        }

        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storedFileName, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
        {
            Files.Remove(storedFileName);
            return Task.CompletedTask;
        }
    }

    public static LeadDocumentUpload Pdf(string name = "quotation.pdf")
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");
        return new LeadDocumentUpload
        {
            Content = new MemoryStream(bytes),
            FileName = name,
            ContentType = "application/pdf",
            Length = bytes.Length
        };
    }
}
