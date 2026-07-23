using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using DAMS.Application.Services;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using DAMS.Api.Middleware;
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
    options.MultipartBodyLengthLimit = FinanceAttachmentFileValidator.MaxRequestSize;
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
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

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
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IInstallmentService, InstallmentService>();
builder.Services.AddScoped<IBookingRequestService, BookingRequestService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IFinanceAccountService, FinanceAccountService>();
builder.Services.AddScoped<IFileStorageService>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    return new LocalFileStorageService(env.WebRootPath);
});
builder.Services.AddScoped<IFinanceAttachmentStorage>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var configuredPath = sp.GetRequiredService<IConfiguration>()["FinanceAttachments:StoragePath"];
    var storagePath = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath)
        ? Path.Combine(env.ContentRootPath, "App_Data", "finance-attachments")
        : Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(env.ContentRootPath, configuredPath));
    var webRoot = Path.GetFullPath(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"));
    if (storagePath.Equals(webRoot, StringComparison.OrdinalIgnoreCase)
        || storagePath.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Finance attachment storage must be outside wwwroot.");
    return new PrivateFinanceAttachmentStorage(storagePath);
});

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
app.UseRateLimiter();

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
app.UseAuthorization();
app.MapControllers();

app.Run();
