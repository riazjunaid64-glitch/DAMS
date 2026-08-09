using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
/// Boots the real API pipeline (auth middleware, JWT validation, role policies) through
/// <see cref="WebApplicationFactory{TEntryPoint}"/> and proves the Admin-only enforcement on the
/// document and commission surfaces. Unlike the attribute-reflection checks, this exercises the
/// actual authentication/authorization stack, so it catches broken JWT role mapping or a policy
/// that silently stops enforcing.
/// </summary>
public sealed class EndpointAuthorizationTests : IClassFixture<EndpointAuthorizationTests.ApiFactory>
{
    private readonly ApiFactory _factory;

    public EndpointAuthorizationTests(ApiFactory factory) => _factory = factory;

    // Every Admin-only GET surface that must never be reachable without the Admin role.
    public static IEnumerable<object[]> AdminOnlyEndpoints() => new[]
    {
        new object[] { "/api/customer-documents/categories" },
        new object[] { "/api/finance/commissions-rebates/summary" },
        new object[] { "/api/finance/commissions-rebates/rules" },
    };

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task AnonymousRequest_IsRejectedWith401(string path)
    {
        var client = _factory.CreateClient(NoRedirect);
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task NonAdminRequest_IsRejectedWith403(string path)
    {
        var client = _factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Authorization = Bearer("Client");
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task AdminRequest_IsAllowed(string path)
    {
        var client = _factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Authorization = Bearer("Admin");
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanReadASeededCustomerChecklist()
    {
        var customerId = await _factory.SeedCustomerAsync("Auth Test Customer");
        var client = _factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Authorization = Bearer("Admin");
        var response = await client.GetAsync($"/api/customer-documents/customers/{customerId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_GuessingAMissingCustomerId_GetsACleanClientError_NotAServerLeak()
    {
        var client = _factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Authorization = Bearer("Admin");
        var response = await client.GetAsync("/api/customer-documents/customers/999999");
        Assert.InRange((int)response.StatusCode, 400, 499);
    }

    [Fact]
    public async Task NonAdmin_CannotReachACustomerChecklist_EvenWithAValidId()
    {
        var customerId = await _factory.SeedCustomerAsync("Guarded Customer");
        var client = _factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Authorization = Bearer("Client");
        var response = await client.GetAsync($"/api/customer-documents/customers/{customerId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Mutation surfaces (create/update). Authorization runs before model binding, so an empty body
    // still exercises the 401/403 gate.
    public static IEnumerable<object[]> AdminOnlyMutations() => new[]
    {
        new object[] { "/api/customer-documents/categories" },
        new object[] { "/api/finance/commissions-rebates/partners" },
        new object[] { "/api/finance/commissions-rebates/rules" },
    };

    [Theory]
    [MemberData(nameof(AdminOnlyMutations))]
    public async Task AnonymousMutation_IsRejectedWith401(string path)
    {
        var client = _factory.CreateClient(NoRedirect);
        var response = await client.PostAsync(path, JsonBody());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyMutations))]
    public async Task NonAdminMutation_IsRejectedWith403(string path)
    {
        var client = _factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Authorization = Bearer("Client");
        var response = await client.PostAsync(path, JsonBody());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Private-file download surfaces: a leak here would expose identity documents / financial evidence.
    public static IEnumerable<object[]> AdminOnlyDownloads() => new[]
    {
        new object[] { "/api/customer-documents/customers/1/requirements/1/versions/1/file" },
        new object[] { "/api/finance/commissions-rebates/evidence/1/file" },
    };

    [Theory]
    [MemberData(nameof(AdminOnlyDownloads))]
    public async Task AnonymousDownload_IsRejectedWith401(string path)
    {
        var client = _factory.CreateClient(NoRedirect);
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyDownloads))]
    public async Task NonAdminDownload_IsRejectedWith403(string path)
    {
        var client = _factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Authorization = Bearer("Client");
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Per-resource reads: a non-admin must not reach ANY customer's checklist/history or a booking's
    // audit by guessing ids — the Admin-only policy is the ownership boundary and rejects before the
    // resource is resolved (so this is the IDOR guard for these back-office surfaces).
    public static IEnumerable<object[]> AdminOnlyResourceReads() => new[]
    {
        new object[] { "/api/customer-documents/customers/1" },
        new object[] { "/api/customer-documents/customers/1/history" },
        new object[] { "/api/finance/commissions-rebates/bookings/1/audit" },
    };

    [Theory]
    [MemberData(nameof(AdminOnlyResourceReads))]
    public async Task AnonymousResourceRead_IsRejectedWith401(string path)
    {
        var client = _factory.CreateClient(NoRedirect);
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyResourceReads))]
    public async Task NonAdminResourceRead_IsRejectedWith403(string path)
    {
        var client = _factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Authorization = Bearer("Client");
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static StringContent JsonBody() => new("{}", Encoding.UTF8, "application/json");

    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };

    private AuthenticationHeaderValue Bearer(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var token = tokens.GenerateAccessToken(
            new User { UserId = 42, Email = "actor@dams.test", FullName = "Test Actor" }, role);
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
                    // 64+ chars so the JWT bearer setup accepts the signing key.
                    ["Jwt:Key"] = new string('k', 64),
                    ["Jwt:Issuer"] = "dams-tests",
                    ["Jwt:Audience"] = "dams-tests",
                    ["Notifications:AllowedPushEndpointHosts:0"] = "push.test",
                });
            });

            builder.ConfigureServices(services =>
            {
                // Swap the SQL Server context (and its pool) for an isolated in-memory store.
                var remove = services.Where(d =>
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    (d.ServiceType.FullName != null && d.ServiceType.FullName.Contains("DbContextPool")) ||
                    (d.ServiceType.FullName != null && d.ServiceType.FullName.Contains("ScopedDbContextLease")))
                    .ToList();
                foreach (var d in remove)
                    services.Remove(d);
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("endpoint-auth-tests"));

                // The background workers poll the database on a timer; they add nothing to auth
                // enforcement and only create noise under the test host.
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                    services.Remove(hosted);
            });
        }

        public async Task<int> SeedCustomerAsync(string name)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var customer = new Customer { FullName = name, Phone = Guid.NewGuid().ToString("N"), Status = Domain.Enums.CustomerStatus.Active };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            return customer.Id;
        }
    }
}
