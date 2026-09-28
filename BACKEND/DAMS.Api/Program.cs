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
    .Validate(o => o.StartupDelaySeconds is >= 0 and <= 300
                   && o.StartupRetryMaxDelaySeconds is >= 1 and <= 3600,
        "Notification worker startup retry settings are outside the supported range.")
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
