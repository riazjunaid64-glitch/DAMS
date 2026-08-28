using System.Net;
using System.Net.Http.Headers;
using DAMS.Api.Filters;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// Guards the money routes that are bound from a multipart form against the one binding trap they
/// all share.
/// <para>
/// A movement and its bank slip are saved in a single multipart request, so those DTOs are bound by
/// the form value provider rather than the JSON formatter — and the form binder converts an empty
/// field to <c>null</c>. With nullable reference types enabled, a non-nullable <c>string</c>
/// property is implicitly required, so a create — which legitimately has no row version to send —
/// is rejected with "The ConcurrencyToken field is required." before the action ever runs. The
/// service never sees it, nothing reaches the concurrency check, and the operator is told a field
/// is missing that they could not have supplied.
/// </para>
/// <para>
/// It has now happened twice: first on staff cash, then on loans, where every new drawdown and
/// repayment was refused. These tests run the real MVC pipeline, so they fail on the binding
/// rejection rather than on the shape of a DTO — the same way the operator meets it.
/// </para>
/// </summary>
public sealed class FinanceFormBindingTests : IClassFixture<FinanceFormBindingTests.ApiFactory>
{
    private readonly ApiFactory _factory;

    public FinanceFormBindingTests(ApiFactory factory) => _factory = factory;

    /// <summary>
    /// A brand new drawdown, exactly as the Loans page sends one: the multipart route, an empty
    /// concurrency token, no file. The loan id is deliberately one that does not exist — reaching
    /// "Loan not found." is the proof that binding and validation let the request through to the
    /// service, which is the whole point. What must never come back is a model-state rejection.
    /// </summary>
    [Fact]
    public async Task NewLoanDrawdown_WithAnEmptyConcurrencyToken_ReachesTheService()
    {
        var body = new MultipartFormDataContent
        {
            { new StringContent("Drawdown"), "type" },
            { new StringContent("500000"), "principalAmount" },
            { new StringContent("0"), "interestAmount" },
            { new StringContent("2026-08-28"), "date" },
            { new StringContent("1"), "financeAccountId" },
            // The field the page always sends, and empty on a create because there is no row yet.
            { new StringContent(string.Empty), "concurrencyToken" },
        };

        var response = await PostAsync("/api/finance/loans/999999/transactions/form", body);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNotAModelBindingRejection(payload);
        Assert.Contains("Loan not found.", payload, StringComparison.Ordinal);
    }

    /// <summary>A correction still carries its row version, and must remain equally acceptable.</summary>
    [Fact]
    public async Task LoanCorrection_WithARealConcurrencyToken_ReachesTheService()
    {
        var body = new MultipartFormDataContent
        {
            { new StringContent("Repayment"), "type" },
            { new StringContent("1000"), "principalAmount" },
            { new StringContent("50"), "interestAmount" },
            { new StringContent("2026-08-28"), "date" },
            { new StringContent("1"), "financeAccountId" },
            { new StringContent(Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8])), "concurrencyToken" },
        };

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/finance/loans/999999/transactions/1/form")
        {
            Content = body
        };
        request.Headers.Authorization = Bearer("Admin");
        var response = await _factory.CreateClient(NoRedirect).SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNotAModelBindingRejection(payload);
    }

    /// <summary>The sibling route the same trap was found on first, kept from regressing.</summary>
    [Fact]
    public async Task NewStaffCashTransfer_WithAnEmptyConcurrencyToken_ReachesTheService()
    {
        var body = new MultipartFormDataContent
        {
            { new StringContent("FundsGiven"), "type" },
            { new StringContent("2500"), "amount" },
            { new StringContent("2026-08-28"), "date" },
            { new StringContent("1"), "counterpartyFinanceAccountId" },
            { new StringContent(string.Empty), "concurrencyToken" },
        };

        var response = await PostAsync("/api/finance/staff-cash/999999/transfers/form", body);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNotAModelBindingRejection(payload);
    }

    /// <summary>
    /// A refusal the service chose reads as <c>{"message":"…"}</c>; a model-state rejection is a
    /// ValidationProblemDetails, which always carries an "errors" object naming the field. Asserting
    /// on both keeps this honest whichever way the binder decides to complain.
    /// </summary>
    private static void AssertNotAModelBindingRejection(string payload)
    {
        Assert.DoesNotContain("ConcurrencyToken", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"errors\"", payload, StringComparison.Ordinal);
    }

    private Task<HttpResponseMessage> PostAsync(string path, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Authorization = Bearer("Admin");
        // These routes are idempotency-guarded, and the filter refuses a request without a key
        // before the action runs — which would hide the binding behaviour these tests are about.
        request.Headers.Add(IdempotentMoneyOperationFilter.HeaderName, Guid.NewGuid().ToString("N"));
        return _factory.CreateClient(NoRedirect).SendAsync(request);
    }

    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };

    private AuthenticationHeaderValue Bearer(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var token = tokens.GenerateAccessToken(
            new User { UserId = 7, Email = "form-binding@dams.test", FullName = "Form Binding Actor" }, role);
        return new AuthenticationHeaderValue("Bearer", token);
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=(test);Database=unused;");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = new string('k', 64),
                    ["Jwt:Issuer"] = "dams-tests",
                    ["Jwt:Audience"] = "dams-tests",
                    ["Notifications:AllowedPushEndpointHosts:0"] = "push.test",
                });
            });

            builder.ConfigureServices(services =>
            {
                var remove = services.Where(d =>
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    (d.ServiceType.FullName != null && d.ServiceType.FullName.Contains("DbContextPool")) ||
                    (d.ServiceType.FullName != null && d.ServiceType.FullName.Contains("ScopedDbContextLease")))
                    .ToList();
                foreach (var d in remove)
                    services.Remove(d);
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("finance-form-binding-tests"));

                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                    services.Remove(hosted);
            });
        }
    }
}
