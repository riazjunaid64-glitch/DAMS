using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAMS.Application.Tests;

/// <summary>
/// A complete notification platform wired exactly as the API wires it, over a seeded DAMS:
/// an admin, a manager, two sales employees, a customer with a login, a customer without
/// one, a project, units, a booking and an installment plan.
///
/// The two providers are fakes that record what they were asked to do and can be told to
/// fail, which is what lets the tests assert on retry, bounce and permanent-failure
/// behaviour without a network.
/// </summary>
internal sealed class NotificationTestHarness : IAsyncDisposable
{
    public AppDbContext Db { get; }
    public NotificationOptions Options { get; }
    public LeadTestHarness.FakeClock Clock { get; }

    public NotificationSettingsStore Settings { get; }
    public NotificationEligibilityPolicy Eligibility { get; }
    public NotificationDispatcher Dispatcher { get; }
    public NotificationInboxService Inbox { get; }
    public NotificationPreferenceService Preferences { get; }
    public PushSubscriptionService Push { get; }
    public NotificationRecipientResolver Recipients { get; }
    public NotificationConfigurationService Configuration { get; }
    public NotificationAdminService Admin { get; }
    public NotificationEventService Events { get; }
    public NotificationDeliveryProcessor Processor { get; }
    public NotificationRenderer Renderer { get; }
    public NotificationRealtimeBroker Realtime { get; }

    public FakeEmailSender Email { get; }
    public FakePushSender PushSender { get; }

    public BookingService Bookings { get; }
    public InstallmentService Installments { get; }

    public int AdminUserId { get; private set; }
    public int ManagerUserId { get; private set; }
    public int SalesUserId { get; private set; }
    public int OtherSalesUserId { get; private set; }
    public int CustomerUserId { get; private set; }
    public int SecondCustomerUserId { get; private set; }

    public int CustomerId { get; private set; }
    public int SecondCustomerId { get; private set; }
    /// <summary>A customer with no DAMS login — receipts must still reach them by email.</summary>
    public int LoginlessCustomerId { get; private set; }

    public int TeamId { get; private set; }
    public int ProjectId { get; private set; }
    public int UnitId { get; private set; }
    public int SecondUnitId { get; private set; }
    public int ThirdUnitId { get; private set; }
    public int BookingId { get; private set; }
    public int FinanceAccountId { get; private set; }

    public NotificationUserContext AdminCtx { get; private set; } = null!;
    public NotificationUserContext ManagerCtx { get; private set; } = null!;
    public NotificationUserContext SalesCtx { get; private set; } = null!;
    public NotificationUserContext OtherSalesCtx { get; private set; } = null!;
    public NotificationUserContext CustomerCtx { get; private set; } = null!;
    public NotificationUserContext SecondCustomerCtx { get; private set; } = null!;

    private NotificationTestHarness(AppDbContext db, NotificationOptions options)
    {
        Db = db;
        Options = options;
        Clock = new LeadTestHarness.FakeClock(DateTime.UtcNow);

        Settings = new NotificationSettingsStore(db);
        Eligibility = new NotificationEligibilityPolicy(db);
        Realtime = new NotificationRealtimeBroker();
        Dispatcher = new NotificationDispatcher(db, Settings, Realtime, Clock, Eligibility,
            NullLogger<NotificationDispatcher>.Instance);
        Inbox = new NotificationInboxService(db, Clock, Eligibility);
        Preferences = new NotificationPreferenceService(db, Settings, Eligibility);
        Renderer = new NotificationRenderer(db, Settings);

        Email = new FakeEmailSender();
        PushSender = new FakePushSender();

        options.AllowedPushEndpointHosts = new[] { "push.test" };
        Push = new PushSubscriptionService(db, Settings, PushSender, options);
        Recipients = new NotificationRecipientResolver(db);
        Configuration = new NotificationConfigurationService(db, Settings, Renderer, Email);
        Admin = new NotificationAdminService(db, Recipients, options, Clock, Eligibility);

        var customers = new CustomerService(db);
        Events = new NotificationEventService(db, Dispatcher, Settings, Clock,
            NullLogger<NotificationEventService>.Instance);
        var accounts = new FinanceAccountService(db);
        Bookings = new BookingService(db, customers, accounts, Events);
        Installments = new InstallmentService(db, accounts, Events);

        var attachments = new NotificationReceiptAttachmentBuilder(
            Bookings, Settings, NullLogger<NotificationReceiptAttachmentBuilder>.Instance);

        var senders = new INotificationChannelSender[]
        {
            new InAppChannelSender(Realtime),
            new EmailChannelSender(db, Settings, Renderer, Email, attachments),
            new WebPushChannelSender(db, Settings, Renderer, PushSender, Push, options)
        };

        Processor = new NotificationDeliveryProcessor(db, senders, Dispatcher, Recipients, options, Clock, Eligibility,
            NullLogger<NotificationDeliveryProcessor>.Instance);
    }

    public static async Task<NotificationTestHarness> CreateAsync(NotificationOptions? options = null)
    {
        var db = LeadTestHarness.CreateContext();
        var harness = new NotificationTestHarness(db, options ?? new NotificationOptions());
        await harness.SeedAsync();
        return harness;
    }

    private async Task SeedAsync()
    {
        var admin = NewUser("Ayesha Admin", "admin@dams.test", 1);
        var manager = NewUser("Mahmood Manager", "manager@dams.test", 3);
        var sales = NewUser("Sana Sales", "sales@dams.test", 4);
        var otherSales = NewUser("Omar Sales", "omar@dams.test", 4);
        var customer = NewUser("Client Person", "client@dams.test", 2);
        var secondCustomer = NewUser("Second Client", "client2@dams.test", 2);
        Db.Users.AddRange(admin, manager, sales, otherSales, customer, secondCustomer);
        await Db.SaveChangesAsync();

        AdminUserId = admin.UserId;
        ManagerUserId = manager.UserId;
        SalesUserId = sales.UserId;
        OtherSalesUserId = otherSales.UserId;
        CustomerUserId = customer.UserId;
        SecondCustomerUserId = secondCustomer.UserId;

        var managerEmployee = NewEmployee("Mahmood Manager", manager.UserId);
        var salesEmployee = NewEmployee("Sana Sales", sales.UserId);
        var otherEmployee = NewEmployee("Omar Sales", otherSales.UserId);
        Db.Employees.AddRange(managerEmployee, salesEmployee, otherEmployee);
        await Db.SaveChangesAsync();

        var team = new Team { Name = "North Sales", ManagerEmployeeId = managerEmployee.Id, IsActive = true };
        Db.Teams.Add(team);
        await Db.SaveChangesAsync();
        TeamId = team.Id;

        managerEmployee.TeamId = team.Id;
        salesEmployee.TeamId = team.Id;
        await Db.SaveChangesAsync();

        var project = new Project { ProjectName = "Floria Heights", Location = "Lahore", CreatedById = admin.UserId };
        Db.Projects.Add(project);
        await Db.SaveChangesAsync();
        ProjectId = project.Id;

        var unit = NewUnit(project.Id, "A-101");
        var secondUnit = NewUnit(project.Id, "A-102");
        var thirdUnit = NewUnit(project.Id, "A-103");
        Db.Units.AddRange(unit, secondUnit, thirdUnit);
        await Db.SaveChangesAsync();
        UnitId = unit.Id;
        SecondUnitId = secondUnit.Id;
        ThirdUnitId = thirdUnit.Id;

        var buyer = NewCustomer("Client Person", "client@dams.test", customer.UserId);
        var secondBuyer = NewCustomer("Second Client", "client2@dams.test", secondCustomer.UserId);
        var loginless = NewCustomer("Walk In Buyer", "walkin@dams.test", null);
        Db.Customers.AddRange(buyer, secondBuyer, loginless);
        await Db.SaveChangesAsync();

        CustomerId = buyer.Id;
        SecondCustomerId = secondBuyer.Id;
        LoginlessCustomerId = loginless.Id;

        var booking = new Booking
        {
            BookingReference = "BK-000001",
            CustomerId = buyer.Id,
            UnitId = unit.Id,
            Status = BookingStatus.AwaitingBookingAmount,
            ListPrice = 10_000_000m,
            AgreedSalePrice = 10_000_000m,
            BookingAmountRequired = 1_000_000m,
            BookingAmountReceived = 0m,
            TotalInstallmentAmount = 9_000_000m,
            BookingDate = DateTime.UtcNow,
            CreatedByUserId = admin.UserId
        };
        Db.Bookings.Add(booking);
        unit.Status = UnitStatus.Booked;

        // Every payment has to name the account it landed in, so the harness needs one.
        var account = new FinanceAccount
        {
            Name = "HBL Main", Type = FinanceAccountType.Bank,
            AccountHolderName = "DAMS Estates", OpeningBalance = 0m, IsActive = true
        };
        Db.FinanceAccounts.Add(account);
        await Db.SaveChangesAsync();
        BookingId = booking.Id;
        FinanceAccountId = account.Id;

        AdminCtx = Ctx(AdminUserId, LeadRoles.Admin, "Ayesha Admin", "admin@dams.test");
        ManagerCtx = Ctx(ManagerUserId, LeadRoles.Manager, "Mahmood Manager", "manager@dams.test");
        SalesCtx = Ctx(SalesUserId, LeadRoles.Employee, "Sana Sales", "sales@dams.test");
        OtherSalesCtx = Ctx(OtherSalesUserId, LeadRoles.Employee, "Omar Sales", "omar@dams.test");
        CustomerCtx = Ctx(CustomerUserId, "Client", "Client Person", "client@dams.test");
        SecondCustomerCtx = Ctx(SecondCustomerUserId, "Client", "Second Client", "client2@dams.test");
    }

    /// <summary>Switches both channels on with a working configuration.</summary>
    public async Task EnableChannelsAsync(bool email = true, bool push = true)
    {
        await Settings.SetAsync(NotificationSettingKeys.EmailEnabled, email ? "true" : "false", AdminUserId);
        await Settings.SetAsync(NotificationSettingKeys.EmailSmtpHost, "smtp.test", AdminUserId);
        await Settings.SetAsync(NotificationSettingKeys.EmailSenderAddress, "no-reply@dams.test", AdminUserId);
        await Settings.SetAsync(NotificationSettingKeys.EmailSenderName, "DAMS", AdminUserId);
        await Settings.SetAsync(NotificationSettingKeys.CompanyName, "DAMS Estates", AdminUserId);
        await Settings.SetAsync(NotificationSettingKeys.PublicBaseUrl, "https://dams.test", AdminUserId);

        await Settings.SetAsync(NotificationSettingKeys.PushEnabled, push ? "true" : "false", AdminUserId);
        var (publicKey, privateKey) = WebPushClient.GenerateVapidKeys();
        await Settings.SetAsync(NotificationSettingKeys.PushVapidPublicKey, publicKey, AdminUserId);
        await Settings.SetAsync(NotificationSettingKeys.PushVapidPrivateKey, privateKey, AdminUserId);
        await Settings.SetAsync(NotificationSettingKeys.PushVapidSubject, "mailto:admin@dams.test", AdminUserId);

        await Db.SaveChangesAsync();
    }

    public async Task<PushSubscription> AddPushSubscriptionAsync(int userId, string endpointSuffix, string? label = null)
    {
        var subscription = new PushSubscription
        {
            UserId = userId,
            Endpoint = $"https://push.test/{endpointSuffix}",
            P256dh = "test-p256dh",
            Auth = "test-auth",
            DeviceLabel = label ?? $"Device {endpointSuffix}",
            IsActive = true
        };

        Db.PushSubscriptions.Add(subscription);
        await Db.SaveChangesAsync();
        return subscription;
    }

    /// <summary>Records a booking-amount payment through the real booking service.</summary>
    public async Task<int> RecordPaymentAsync(decimal amount = 250_000m)
    {
        await Bookings.RecordBookingAmountPaymentAsync(BookingId,
            new DTOs.BookingDtos.RecordBookingAmountPaymentDto
            {
                Amount = amount,
                PaymentMethod = PaymentMethod.BankTransfer,
                FinanceAccountId = FinanceAccountId
            }, AdminUserId);

        Db.ChangeTracker.Clear();
        return await Db.Payments.AsNoTracking()
            .Where(p => p.BookingId == BookingId)
            .OrderByDescending(p => p.Id)
            .Select(p => p.Id)
            .FirstAsync();
    }

    public Task<List<Notification>> NotificationsAsync() =>
        Db.Notifications.AsNoTracking().ToListAsync();

    public Task<List<NotificationDelivery>> DeliveriesAsync() =>
        Db.NotificationDeliveries.AsNoTracking().Include(d => d.Notification).ToListAsync();

    public Task<List<NotificationDelivery>> DeliveriesForAsync(NotificationChannel channel) =>
        Db.NotificationDeliveries.AsNoTracking()
            .Include(d => d.Notification)
            .Where(d => d.Channel == channel)
            .ToListAsync();

    private static NotificationUserContext Ctx(int userId, string role, string name, string email) =>
        new() { UserId = userId, Role = role, DisplayName = name, Email = email };

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

    private static Customer NewCustomer(string name, string email, int? userId) => new()
    {
        FullName = name,
        Phone = "03001110000",
        Email = email,
        UserId = userId,
        Status = CustomerStatus.Active
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

    public ValueTask DisposeAsync() => Db.DisposeAsync();

    // ── Fakes ───────────────────────────────────────────────────────────────────

    /// <summary>Records every message and can be told exactly how to fail.</summary>
    internal sealed class FakeEmailSender : IEmailSender
    {
        public string ProviderName => "fake";

        public List<EmailMessage> Sent { get; } = new();

        /// <summary>Set to make the next sends fail transiently (a provider outage).</summary>
        public int TransientFailuresRemaining { get; set; }

        public bool FailPermanently { get; set; }

        public bool FailAsHardBounce { get; set; }

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (FailAsHardBounce)
                return Task.FromResult(new EmailSendResult
                {
                    Success = false,
                    IsHardBounce = true,
                    IsPermanent = true,
                    Error = "550 mailbox does not exist"
                });

            if (FailPermanently)
                return Task.FromResult(new EmailSendResult { Success = false, IsPermanent = true, Error = "rejected" });

            if (TransientFailuresRemaining > 0)
            {
                TransientFailuresRemaining--;
                return Task.FromResult(new EmailSendResult { Success = false, Error = "connection refused" });
            }

            Sent.Add(message);
            return Task.FromResult(new EmailSendResult { Success = true, ProviderReference = $"msg-{Sent.Count}" });
        }
    }

    internal sealed class FakePushSender : IWebPushSender
    {
        public List<(string Endpoint, string Payload)> Sent { get; } = new();

        public HashSet<string> GoneEndpoints { get; } = new();

        public bool FailTransiently { get; set; }

        public Task<WebPushResult> SendAsync(
            WebPushTarget target, string payloadJson, WebPushCredentials credentials, CancellationToken cancellationToken = default)
        {
            if (GoneEndpoints.Contains(target.Endpoint))
                return Task.FromResult(new WebPushResult { Success = false, StatusCode = 410, SubscriptionGone = true, Error = "gone" });

            if (FailTransiently)
                return Task.FromResult(new WebPushResult { Success = false, StatusCode = 503, Error = "service unavailable" });

            Sent.Add((target.Endpoint, payloadJson));
            return Task.FromResult(new WebPushResult { Success = true, StatusCode = 201 });
        }
    }
}
