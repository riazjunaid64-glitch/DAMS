using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using DAMS.Application.Services;
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
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Http.Features;
using System.IO.Compression;
using Microsoft.Net.Http.Headers;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

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
// "Due today", "overdue" and "inactive" all depend on the current instant; taking it from
// an injected clock keeps those rules deterministic under test.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddControllers()
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
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IFinanceAccountService, FinanceAccountService>();
builder.Services.AddScoped<IRevenueCategoryService, RevenueCategoryService>();
builder.Services.AddScoped<IOpeningBalanceService, OpeningBalanceService>();
builder.Services.AddScoped<ICapitalPartnerService, CapitalPartnerService>();
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
static string ResolvePrivateStoragePath(IServiceProvider sp, string configurationKey, string defaultRelativePath)
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var configuredPath = sp.GetRequiredService<IConfiguration>()[configurationKey];
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
