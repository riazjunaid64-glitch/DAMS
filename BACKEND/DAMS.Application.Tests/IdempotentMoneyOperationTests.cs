using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DAMS.Api.Filters;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The retry guard on every money-creating endpoint, tested at the layer it actually runs in.
/// <para>
/// The service-level finance suites cannot see this: the filter sits in the MVC pipeline and its
/// whole job is to interpret the ACTION'S OUTCOME. Getting that wrong is invisible to a happy-path
/// test and silent in production — the request still returns the right thing to the operator in
/// front of the screen, and only the retry, days later, is answered with the wrong record.
/// </para>
/// <para>
/// Two things are pinned here. First, that the outcome is read from the context <c>next()</c>
/// returns: the one passed in is null unless a filter short-circuits, and the response has not been
/// written when the filter resumes, so both of the obvious sources say "200, empty" no matter what
/// the action did. Second, what each outcome does to the reservation — kept for a success, released
/// for a refusal, and deliberately held for a fault, because a fault cannot tell you whether the
/// money was written.
/// </para>
/// </summary>
public sealed class IdempotentMoneyOperationTests : IClassFixture<IdempotentMoneyOperationTests.ApiFactory>
{
    private const string Header = "Idempotency-Key";
    private const string ExpensePath = "/api/Finance/expenses";

    private readonly ApiFactory _factory;

    public IdempotentMoneyOperationTests(ApiFactory factory) => _factory = factory;

    // ── Through the real MVC pipeline ──

    [Fact]
    public async Task AMoneyRequestWithoutAKey_IsRefused_AndReservesNothing()
    {
        var before = (await _factory.ReservationsAsync()).Count;
        var client = Admin();
        var response = await client.PostAsJsonAsync(ExpensePath, new { amount = 1000m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, (await _factory.ReservationsAsync()).Count);
    }

    [Fact]
    public async Task AnInputTheActionRefuses_ReleasesTheKey_SoTheCorrectedEntryCanUseItAgain()
    {
        // The regression this exists for: the filter used to read the request's status from
        // Response.StatusCode, which is still 200 while the filter is unwinding. A refusal was
        // therefore filed as a completed success with an empty body, and the operator's corrected
        // retry — same key, because the intent is the same — was answered 200 {} without ever
        // reaching the service. The expense was never recorded and the screen said it was.
        var key = Key();
        var client = Admin();
        var response = await client.SendAsync(Post(ExpensePath, new { amount = 1000m }, key));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _factory.ReservationsAsync(key));
    }

    [Fact]
    public async Task ASuccessfulSave_StoresTheStatusAndTheRealResponseBody()
    {
        var (accountId, categoryId) = await _factory.SeedExpenseTargetsAsync();
        var key = Key();
        var client = Admin();

        var response = await client.SendAsync(Post(ExpensePath,
            new { amount = 4321m, financeAccountId = accountId, categoryId }, key));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returned = await response.Content.ReadAsStringAsync();

        var reservation = Assert.Single(await _factory.ReservationsAsync(key));
        Assert.True(reservation.IsCompleted);
        Assert.Equal(200, reservation.StatusCode);
        Assert.NotNull(reservation.CompletedAt);

        // The point of storing anything at all: a replay has to return the record the first attempt
        // created. An empty object would tell the retrying operator the save produced nothing.
        Assert.NotNull(reservation.ResponseBody);
        Assert.NotEqual("{}", reservation.ResponseBody);
        var stored = JsonDocument.Parse(reservation.ResponseBody!).RootElement;
        Assert.True(stored.GetProperty("id").GetInt32() > 0);
        Assert.Equal(4321m, stored.GetProperty("amount").GetDecimal());
        Assert.Equal(JsonDocument.Parse(returned).RootElement.GetProperty("id").GetInt32(),
            stored.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task AnInvalidKey_IsRefusedBeforeAnythingIsReserved()
    {
        var client = Admin();
        var tooLong = new string('k', 121);
        var response = await client.SendAsync(Post(ExpensePath, new { amount = 1000m }, tooLong));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _factory.ReservationsAsync(tooLong));
    }

    // ── The filter's own outcome handling ──
    //
    // Driven directly so a fault and a short-circuit can be produced deliberately. next() is
    // modelled exactly as MVC drives it: the ActionExecutingContext keeps whatever result it had
    // (none), and the action's own result comes back on the ActionExecutedContext.

    [Fact]
    public async Task TheOutcomeIsReadFromTheContextNextReturns_NotTheOneHandedIn()
    {
        await using var host = FilterHost.Create();
        var key = Key();
        var executing = host.Executing(key);

        await host.Filter().OnActionExecutionAsync(executing,
            () => Task.FromResult(host.Executed(new BadRequestObjectResult(new { message = "Amount is required." }))));

        // Nothing was written to the context the filter was handed — which is precisely why reading
        // it saw "no result" and fell through to a 200 that had not happened.
        Assert.Null(executing.Result);
        Assert.Empty(await host.ReservationsAsync(key));
    }

    [Fact]
    public async Task ASuccessfulResult_IsRecordedWithItsBody()
    {
        await using var host = FilterHost.Create();
        var key = Key();

        await host.Filter().OnActionExecutionAsync(host.Executing(key),
            () => Task.FromResult(host.Executed(new OkObjectResult(new { id = 12, amount = 250m }))));

        var reservation = Assert.Single(await host.ReservationsAsync(key));
        Assert.True(reservation.IsCompleted);
        Assert.Equal(200, reservation.StatusCode);
        // Serialised the way the API serialises, so a replay is byte-identical to the first answer.
        Assert.Equal("{\"id\":12,\"amount\":250}", reservation.ResponseBody);
    }

    [Fact]
    public async Task AnUnhandledException_KeepsTheKeyHeld_BecauseItCannotSayWhetherTheMoneyWasWritten()
    {
        // A throw after the service committed looks identical to one before it. Releasing the key
        // would let the retry record the payment a second time — the exact failure the filter is
        // here to prevent — so the reservation is kept, uncompleted, and the retry is answered with
        // a conflict that asks a human to check first.
        await using var host = FilterHost.Create();
        var key = Key();

        await host.Filter().OnActionExecutionAsync(host.Executing(key),
            () => Task.FromResult(host.Executed(result: null, new InvalidOperationException("boom"))));

        var reservation = Assert.Single(await host.ReservationsAsync(key));
        Assert.False(reservation.IsCompleted);
        Assert.Equal(500, reservation.StatusCode);
        Assert.Null(reservation.ResponseBody);
    }

    [Fact]
    public async Task AHandledExceptionTurnedIntoAServerError_IsTreatedAsTheFaultItIs()
    {
        await using var host = FilterHost.Create();
        var key = Key();

        await host.Filter().OnActionExecutionAsync(host.Executing(key), () =>
        {
            var executed = host.Executed(new ObjectResult(new { message = "sorry" }) { StatusCode = 500 },
                new InvalidOperationException("boom"));
            executed.ExceptionHandled = true;
            return Task.FromResult(executed);
        });

        var reservation = Assert.Single(await host.ReservationsAsync(key));
        Assert.False(reservation.IsCompleted);
        Assert.Equal(500, reservation.StatusCode);
    }

    [Fact]
    public async Task AFilterThatShortCircuitsAfterUs_IsJudgedOnTheResultItSubstituted()
    {
        await using var host = FilterHost.Create();
        var key = Key();

        await host.Filter().OnActionExecutionAsync(host.Executing(key), () =>
        {
            var executed = host.Executed(new ConflictObjectResult(new { message = "stale" }));
            executed.Canceled = true;
            return Task.FromResult(executed);
        });

        // 409 is a refusal: nothing was recorded, so the intent stays retryable.
        Assert.Empty(await host.ReservationsAsync(key));
    }

    [Fact]
    public async Task ANoContentSuccess_IsRecordedWithoutInventingABody()
    {
        await using var host = FilterHost.Create();
        var key = Key();

        await host.Filter().OnActionExecutionAsync(host.Executing(key),
            () => Task.FromResult(host.Executed(new NoContentResult())));

        var reservation = Assert.Single(await host.ReservationsAsync(key));
        Assert.True(reservation.IsCompleted);
        Assert.Equal(204, reservation.StatusCode);
        Assert.Null(reservation.ResponseBody);
    }

    // ── Helpers ──

    private HttpClient Admin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.GenerateAccessToken(
                new User { UserId = 42, Email = "actor@dams.test", FullName = "Idempotency Admin" }, "Admin"));
        return client;
    }

    private static string Key() => $"test-{Guid.NewGuid():N}";

    private static HttpRequestMessage Post(string path, object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation(Header, key);
        return request;
    }

    /// <summary>
    /// The filter plus a store of its own, so its outcome handling can be driven with a result or an
    /// exception that no endpoint would produce on demand.
    /// </summary>
    private sealed class FilterHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly ActionContext _actionContext;
        private readonly DefaultHttpContext _httpContext;

        private FilterHost(ServiceProvider provider, DefaultHttpContext httpContext, ActionContext actionContext)
        {
            _provider = provider;
            _httpContext = httpContext;
            _actionContext = actionContext;
        }

        public static FilterHost Create()
        {
            // Named once, outside the options lambda: AddDbContext builds DbContextOptions per SCOPE,
            // so generating the name inside would hand the filter's scope and the assertion's scope
            // two different empty databases.
            var database = $"idempotency-filter-{Guid.NewGuid():N}";
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(database));
            var provider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = provider };
            httpContext.Request.Method = "POST";
            httpContext.Request.Path = "/api/Finance/expenses";
            return new FilterHost(provider, httpContext,
                new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        }

        public IdempotentMoneyOperationFilter Filter() => new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IdempotentMoneyOperationFilter>.Instance);

        public ActionExecutingContext Executing(string key)
        {
            _httpContext.Request.Headers[Header] = key;
            return new ActionExecutingContext(_actionContext, new List<IFilterMetadata>(),
                new Dictionary<string, object?> { ["dto"] = new { amount = 250m } }, new object());
        }

        public ActionExecutedContext Executed(IActionResult? result, Exception? exception = null) =>
            new(_actionContext, new List<IFilterMetadata>(), new object())
            {
                Result = result!,
                Exception = exception!
            };

        public async Task<List<IdempotentRequest>> ReservationsAsync(string key)
        {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.IdempotentRequests.AsNoTracking().Where(r => r.Key == key).ToListAsync();
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private const string DatabaseName = "idempotency-endpoint-tests";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=(test);Database=unused;");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = new string('k', 64),
                    ["Jwt:Issuer"] = "dams-tests",
                    ["Jwt:Audience"] = "dams-tests",
                    ["Notifications:AllowedPushEndpointHosts:0"] = "push.test",
                }));

            builder.ConfigureServices(services =>
            {
                var remove = services.Where(d =>
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    (d.ServiceType.FullName != null && d.ServiceType.FullName.Contains("DbContextPool")) ||
                    (d.ServiceType.FullName != null && d.ServiceType.FullName.Contains("ScopedDbContextLease")))
                    .ToList();
                foreach (var d in remove) services.Remove(d);
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(DatabaseName));

                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                    services.Remove(hosted);
            });
        }

        /// <summary>An active cash account and an active, non-taxable head: enough for one expense.</summary>
        public async Task<(int AccountId, int CategoryId)> SeedExpenseTargetsAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var account = new FinanceAccount
            {
                Name = $"Petty Cash {Guid.NewGuid():N}", Type = FinanceAccountType.Cash,
                AccountHolderName = "Test", IsActive = true
            };
            var category = new ExpenseCategory
            {
                Name = $"Office Supplies {Guid.NewGuid():N}", Code = Guid.NewGuid().ToString("N")[..8],
                IsActive = true, IsWhtApplicable = false
            };
            db.FinanceAccounts.Add(account);
            db.ExpenseCategories.Add(category);
            await db.SaveChangesAsync();
            return (account.Id, category.Id);
        }

        public async Task<List<IdempotentRequest>> ReservationsAsync(string? key = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var query = db.IdempotentRequests.AsNoTracking();
            if (key != null) query = query.Where(r => r.Key == key);
            return await query.ToListAsync();
        }
    }
}
