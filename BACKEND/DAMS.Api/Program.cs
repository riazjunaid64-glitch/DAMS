using DAMS.Api.Filters;
using DAMS.Api.Security;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using DAMS.Application.Services;
using DAMS.Application.Services.Integrations;
using DAMS.Application.Services.Notifications;
using DAMS.Application.Interfaces;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using DAMS.Api;
using DAMS.Api.Middleware;
using DAMS.Api.Services;
using DAMS.Application.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Http.Features;
using System.IO.Compression;
using Microsoft.Net.Http.Headers;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// The default HttpClientFactory handler logs each request's full URI (including its query
// string) at Information level. Meta's Graph client puts the access token, appsecret_proof and
// (for the OAuth token exchange) the app's client_secret directly in that query string, so
// leaving this at the default level would write live credentials into the application log on
// every Graph call. Nothing else needs Information-level detail from this specific client.
builder.Logging.AddFilter("System.Net.Http.HttpClient.IMetaGraphClient", LogLevel.Warning);

builder.Services.AddMemoryCache();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/json" });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = Math.Max(
        FinanceAttachmentFileValidator.MaxRequestSize,
        CustomerDocumentService.MaxRequestSize);
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString(),
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(15),
                PermitLimit = 20,
                SegmentsPerWindow = 3,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
    // External lead providers push in bursts; the window is generous but bounded so a
    // misbehaving integration cannot flood the pipeline.
    options.AddPolicy("leadIntake", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 120,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
    // Meta delivers lead webhooks in bursts and disables a subscription that keeps failing.
    // Its own bucket, so neither this nor the generic intake endpoint can starve the other.
    options.AddPolicy("metaWebhook", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 600,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
    // Notification settings, templates, test sends and subscription writes are cheap to
    // call and expensive to abuse; bound them per signed-in user rather than per IP so one
    // shared office address cannot lock everybody out.
    options.AddPolicy("notificationWrite", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: NotificationPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 60,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
    // Mass sending is the one operation where an accident is expensive and irreversible.
    options.AddPolicy("notificationSend", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: NotificationPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(5),
                PermitLimit = 10,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.Configure<LeadAlertOptions>(builder.Configuration.GetSection(LeadAlertOptions.SectionName));
builder.Services.AddOptions<NotificationOptions>()
    .Bind(builder.Configuration.GetSection(NotificationOptions.SectionName))
    .Validate(o => o.DeliveryIntervalSeconds >= 0 && o.ScheduleIntervalSeconds >= 0,
        "Notification worker intervals cannot be negative.")
    .Validate(o => o.DeliveryBatchSize is >= 1 and <= 1000 && o.JobBatchSize is >= 1 and <= 100,
        "Notification batch sizes are outside the supported range.")
    .Validate(o => o.LeaseMinutes is >= 1 and <= 60,
        "Notification lease duration must be between 1 and 60 minutes.")
    .Validate(o => o.MaxAttempts is >= 1 and <= 20
                   && o.BaseRetryDelaySeconds is >= 1 and <= 3600
                   && o.MaxRetryDelayMinutes is >= 1 and <= 1440,
        "Notification retry settings are outside the supported range.")
    .Validate(o => o.MaxBroadcastRecipients is >= 1 and <= 100000
                   && o.LargeAudienceThreshold >= 1
                   && o.LargeAudienceThreshold <= o.MaxBroadcastRecipients,
        "Notification audience limits are invalid.")
    .Validate(o => o.InboxRetentionDays >= 0
                   && o.PushFailureThreshold is >= 1 and <= 20
                   && o.MaxPushSubscriptionsPerUser is >= 1 and <= 20
                   && o.StreamHeartbeatSeconds is >= 5 and <= 300,
        "Notification retention, push, or stream settings are outside the supported range.")
    .Validate(o => o.LeaseMinutes * 60 >= o.MaxPushSubscriptionsPerUser * 20 + 30,
        "The notification lease must exceed the worst-case sequential push-send time.")
    .Validate(o => o.AllowedPushEndpointHosts is { Length: > 0 }
                   && o.AllowedPushEndpointHosts.All(IsValidPushHostPattern),
        "Allowed push endpoint hosts must be plain DNS names or dot-prefixed DNS suffixes.")
    .ValidateOnStart();
// The delivery processor and the push sender need the plain options object, not the
// IOptions wrapper, because they are also constructed directly in tests.
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<NotificationOptions>>().Value);

// ── External integrations (Meta Lead Ads) ────────────────────────────────────────
// Validation covers ranges and all-or-nothing credentials only. Missing credentials must
// not stop the application booting: an unconfigured integration is simply switched off,
// exactly as an unconfigured LeadIntake:ApiKey is.
builder.Services.AddOptions<MetaIntegrationOptions>()
    .Bind(builder.Configuration.GetSection(MetaIntegrationOptions.SectionName))
    .Validate(o => o.EventIntervalSeconds >= 0 && o.ResourceSyncIntervalSeconds >= 0,
        "Meta integration worker intervals cannot be negative.")
    .Validate(o => o.EventBatchSize is >= 1 and <= 200,
        "Meta integration batch size is outside the supported range.")
    .Validate(o => o.StartupDelaySeconds is >= 0 and <= 300
                   && o.StartupRetryMaxDelaySeconds is >= 1 and <= 3600,
        "Meta integration startup retry settings are outside the supported range.")
    .Validate(o => o.LeaseMinutes is >= 1 and <= 60,
        "Meta integration lease duration must be between 1 and 60 minutes.")
    .Validate(o => o.SyncLeaseMinutes is >= 1 and <= 120,
        "Meta integration sync lease duration must be between 1 and 120 minutes.")
    .Validate(o => o.MaxAttempts is >= 1 and <= 20
                   && o.BaseRetryDelaySeconds is >= 1 and <= 3600
                   && o.MaxRetryDelayMinutes is >= 1 and <= 1440
                   && o.AuthRetryDelayHours is >= 1 and <= 168,
        "Meta integration retry settings are outside the supported range.")
    .Validate(o => o.OAuthStateLifetimeMinutes is >= 1 and <= 60,
        "The Meta OAuth state lifetime must be between 1 and 60 minutes.")
    .Validate(o => o.RequestTimeoutSeconds is >= 5 and <= 300
                   && o.MaxWebhookBodyBytes is >= 1024 and <= 5242880
                   && o.MaxGraphPages is >= 1 and <= 200,
        "Meta integration request limits are outside the supported range.")
    .Validate(o => System.Text.RegularExpressions.Regex.IsMatch(o.GraphApiVersion ?? "", @"^v\d+\.\d+$"),
        "MetaIntegration:GraphApiVersion must look like \"v21.0\".")
    .Validate(o => string.IsNullOrWhiteSpace(o.OAuthCallbackUrl)
                   || (Uri.TryCreate(o.OAuthCallbackUrl, UriKind.Absolute, out var callback)
                       && callback.Scheme == Uri.UriSchemeHttps),
        "MetaIntegration:OAuthCallbackUrl must be an absolute HTTPS URL.")
    .Validate(o => IsAllOrNoneConfigured(o),
        "MetaIntegration needs AppId, AppSecret, WebhookVerifyToken and OAuthCallbackUrl together, or none of them.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MetaIntegrationOptions>>().Value);

// Provider tokens are encrypted at rest with this key ring. It is persisted outside wwwroot
// because losing it makes every stored token undecryptable — recoverable only by having each
// admin reconnect. In production this directory must be backed up and, on multi-server
// deployments, shared between instances. It must never be committed.
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("DAMS")
    .PersistKeysToFileSystem(new DirectoryInfo(ResolvePrivateStoragePathFor(
        builder.Environment,
        builder.Configuration,
        "DataProtection:KeyRingPath",
        Path.Combine("App_Data", "dataprotection-keys"))));

// ASP.NET Core only encrypts the key ring at rest automatically when it picks the storage
// location itself; pointing PersistKeysToFileSystem at an explicit directory (above, required so
// the path is configurable and outside wwwroot) opts out of that. Without this, the keys that
// decrypt every stored Meta token would sit on disk as plain XML, protected only by filesystem
// ACLs. DPAPI ties the ciphertext to this Windows account, so set
// DataProtection:ProtectWithDpapi to false only once a non-Windows host or a shared key ring
// across multiple machines makes that unworkable — at which point a certificate
// (ProtectKeysWithCertificate) is the replacement, not going without protection.
if (OperatingSystem.IsWindows() && builder.Configuration.GetValue("DataProtection:ProtectWithDpapi", true))
    dataProtectionBuilder.ProtectKeysWithDpapiNG();
// "Due today", "overdue" and "inactive" all depend on the current instant; taking it from
// an injected clock keeps those rules deterministic under test.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddControllers(options =>
    {
        // Names the admin behind every finance correction. See ActorAttributionFilter.
        options.Filters.Add<ActorAttributionFilter>();
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddDbContextPool<AppDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null));
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IUnitService, UnitService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ICustomerDocumentService, CustomerDocumentService>();
builder.Services.AddScoped<CustomerDocumentReconciliationService>();
builder.Services.AddHostedService<CustomerDocumentReconciliationWorker>();
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IInstallmentService, InstallmentService>();
builder.Services.AddScoped<IBookingRequestService, BookingRequestService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IStaffManagementService, StaffManagementService>();
builder.Services.AddScoped<IStaffInvitationService, StaffInvitationService>();
builder.Services.AddScoped<IClientEmailVerificationService, ClientEmailVerificationService>();
// The only registered component permitted to write Customer.UserId.
builder.Services.AddScoped<ICustomerAccountLinkService, CustomerAccountLinkService>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IFinanceAccountService, FinanceAccountService>();
builder.Services.AddScoped<IStaffCashService, StaffCashService>();
builder.Services.AddScoped<IRevenueCategoryService, RevenueCategoryService>();
builder.Services.AddScoped<IOpeningBalanceService, OpeningBalanceService>();
builder.Services.AddScoped<ICapitalPartnerService, CapitalPartnerService>();
builder.Services.AddScoped<ILoanService, LoanService>();
builder.Services.AddScoped<IExpenseCategoryService, ExpenseCategoryService>();
builder.Services.AddScoped<IVendorService, VendorService>();
builder.Services.AddScoped<IWhtService, WhtService>();
builder.Services.AddScoped<CommissionRebateService>();
builder.Services.AddScoped<ICommissionRebateService>(sp => sp.GetRequiredService<CommissionRebateService>());
builder.Services.AddScoped<ICommissionBookingLifecycle>(sp => sp.GetRequiredService<CommissionRebateService>());
builder.Services.AddScoped<ILeadUserContextResolver, LeadUserContextResolver>();
builder.Services.AddScoped<ILeadNotificationService, LeadNotificationService>();
builder.Services.AddScoped<ILeadService, LeadService>();
builder.Services.AddScoped<ILeadCommunicationService, LeadCommunicationService>();
builder.Services.AddScoped<ILeadFollowUpService, LeadFollowUpService>();
builder.Services.AddScoped<ILeadSiteVisitService, LeadSiteVisitService>();
builder.Services.AddScoped<ILeadDocumentService, LeadDocumentService>();
builder.Services.AddScoped<ILeadConfigurationService, LeadConfigurationService>();
builder.Services.AddScoped<ILeadReportingService, LeadReportingService>();
builder.Services.AddScoped<ILeadAlertService, LeadAlertService>();
builder.Services.AddHostedService<LeadAlertBackgroundService>();
builder.Services.AddSingleton<IIntegrationSecretProtector, DataProtectionIntegrationSecretProtector>();
// Deliberately no Polly: retries are already durable in ExternalIntegrationEvents, where
// Attempts and AvailableAt survive a restart. An in-memory policy would duplicate that
// state and lose it at exactly the wrong moment.
builder.Services.AddHttpClient<IMetaGraphClient, MetaGraphClient>((sp, client) =>
{
    var meta = sp.GetRequiredService<MetaIntegrationOptions>();
    client.BaseAddress = new Uri($"https://graph.facebook.com/{meta.GraphApiVersion}/");
    client.Timeout = TimeSpan.FromSeconds(meta.RequestTimeoutSeconds);
});
builder.Services.AddScoped<IMetaResourceSyncService, MetaResourceSyncService>();
builder.Services.AddScoped<IMetaIntegrationService, MetaIntegrationService>();
builder.Services.AddScoped<IMetaWebhookIntakeService, MetaWebhookIntakeService>();
builder.Services.AddScoped<IMetaLeadEventProcessor, MetaLeadEventProcessor>();
builder.Services.AddHostedService<IntegrationBackgroundService>();

// ── Notification platform ────────────────────────────────────────────────────────
// Business modules depend only on INotificationDispatcher and INotificationEventService.
// Channels are registered as a collection, so adding WhatsApp, SMS or mobile push later is
// one more INotificationChannelSender and one more enum value — no module changes shape.
builder.Services.AddSingleton<INotificationRealtimeBroker, NotificationRealtimeBroker>();
builder.Services.AddSingleton<IWebPushSender>(_ => new WebPushClient());
builder.Services.AddScoped<NotificationSettingsStore>();
builder.Services.AddScoped<NotificationEligibilityPolicy>();
builder.Services.AddScoped<NotificationRenderer>();
builder.Services.AddScoped<NotificationReceiptAttachmentBuilder>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddScoped<INotificationInboxService, NotificationInboxService>();
builder.Services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
builder.Services.AddScoped<PushSubscriptionService>();
builder.Services.AddScoped<IPushSubscriptionService>(sp => sp.GetRequiredService<PushSubscriptionService>());
builder.Services.AddScoped<INotificationRecipientResolver, NotificationRecipientResolver>();
builder.Services.AddScoped<INotificationConfigurationService, NotificationConfigurationService>();
builder.Services.AddScoped<INotificationAdminService, NotificationAdminService>();
builder.Services.AddScoped<INotificationEventService, NotificationEventService>();
builder.Services.AddScoped<INotificationDeliveryProcessor, NotificationDeliveryProcessor>();
builder.Services.AddScoped<INotificationUserContextResolver, NotificationUserContextResolver>();
builder.Services.AddScoped<INotificationChannelSender, InAppChannelSender>();
builder.Services.AddScoped<INotificationChannelSender, EmailChannelSender>();
builder.Services.AddScoped<INotificationChannelSender, WebPushChannelSender>();
builder.Services.AddHostedService<NotificationBackgroundService>();
builder.Services.AddScoped<ILeadDocumentStorage>(sp =>
    new PrivateLeadDocumentStorage(ResolvePrivateStoragePath(
        sp, "LeadDocuments:StoragePath", Path.Combine("App_Data", "lead-documents"))));
builder.Services.AddScoped<IFileStorageService>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    return new LocalFileStorageService(env.WebRootPath);
});
builder.Services.AddScoped<IFinanceAttachmentStorage>(sp =>
    new PrivateFinanceAttachmentStorage(ResolvePrivateStoragePath(
        sp, "FinanceAttachments:StoragePath", Path.Combine("App_Data", "finance-attachments"))));
builder.Services.AddScoped<IFinanceAttachmentWriter, FinanceAttachmentWriter>();
builder.Services.AddScoped<ICustomerDocumentStorage>(sp =>
    new PrivateCustomerDocumentStorage(ResolvePrivateStoragePath(
        sp, "CustomerDocuments:StoragePath", Path.Combine("App_Data", "customer-documents"))));
builder.Services.AddScoped<IFinancialEvidenceStorage>(sp =>
    new PrivateFinancialEvidenceStorage(ResolvePrivateStoragePath(
        sp, "CommissionRebates:EvidenceStoragePath", Path.Combine("App_Data", "commission-rebate-evidence"))));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwtSettings = builder.Configuration.GetSection("Jwt");
    var configuredKey = jwtSettings["Key"];
    if (string.IsNullOrWhiteSpace(configuredKey) || configuredKey.Length < 64)
        throw new InvalidOperationException(
            "Jwt:Key must be supplied through secure configuration (for example Jwt__Key) and contain at least 64 characters.");
    var key = Encoding.UTF8.GetBytes(configuredKey);

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

builder.Services.AddAuthorization(options => options.AddDamsPolicies());

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(static state =>
    {
        var ctx = (HttpContext)state!;
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            ctx.Response.Headers.Pragma = "no-cache";
        }
        return Task.CompletedTask;
    }, context);
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseResponseCompression();
app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path;
        if (path.StartsWithSegments("/uploads"))
            ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=2592000, immutable";
    }
});

app.UseCors("AllowFrontend");
app.UseAuthentication();
// User-partitioned notification limits must run after authentication; otherwise every
// employee behind the same office NAT shares one anonymous IP bucket.
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Signed-in callers are limited per account; anonymous ones fall back to their address so an
// unauthenticated flood is still bounded.
static string NotificationPartitionKey(HttpContext context) =>
    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
    ?? context.Connection.RemoteIpAddress?.ToString()
    ?? IPAddress.None.ToString();

// Half-configured credentials are worse than none: the integration would appear available
// and then fail at the first Graph call. Demand all four together or none at all.
static bool IsAllOrNoneConfigured(MetaIntegrationOptions options)
{
    string?[] credentials =
    [
        options.AppId, options.AppSecret, options.WebhookVerifyToken, options.OAuthCallbackUrl
    ];

    var supplied = credentials.Count(value => !string.IsNullOrWhiteSpace(value));
    return supplied == 0 || supplied == credentials.Length;
}

static bool IsValidPushHostPattern(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return false;

    var host = value[0] == '.' ? value[1..] : value;
    return Uri.CheckHostName(host) == UriHostNameType.Dns
           && !value.Contains('/')
           && !value.Contains(':')
           && !value.Contains('*');
}

// Private documents (finance evidence, lead paperwork) are served only through authorised
// endpoints, so their storage must never sit anywhere the static-file middleware can reach.
static string ResolvePrivateStoragePath(IServiceProvider sp, string configurationKey, string defaultRelativePath) =>
    ResolvePrivateStoragePathFor(
        sp.GetRequiredService<IWebHostEnvironment>(),
        sp.GetRequiredService<IConfiguration>(),
        configurationKey,
        defaultRelativePath);

// Used at startup, where the environment and configuration are already to hand and building
// a throwaway service provider would create a second container.
static string ResolvePrivateStoragePathFor(
    IWebHostEnvironment env, IConfiguration configuration, string configurationKey, string defaultRelativePath)
{
    var configuredPath = configuration[configurationKey];
    var storagePath = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath)
        ? Path.Combine(env.ContentRootPath, defaultRelativePath)
        : Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(env.ContentRootPath, configuredPath));

    var webRoot = Path.GetFullPath(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"));
    if (storagePath.Equals(webRoot, StringComparison.OrdinalIgnoreCase)
        || storagePath.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Private storage for '{configurationKey}' must be outside wwwroot.");

    return storagePath;
}

// Exposed so the integration-test project can boot the real pipeline via WebApplicationFactory<Program>.
public partial class Program { }
