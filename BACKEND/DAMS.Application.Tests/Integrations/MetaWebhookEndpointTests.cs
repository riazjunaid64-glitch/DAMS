using System.Net;
using System.Security.Cryptography;
using System.Text;
using DAMS.Application.Common;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// The webhook endpoint through the real HTTP pipeline: signature verification, size limits,
/// deduplication, and the guarantee that the generic lead-intake endpoint is unaffected.
/// </summary>
public class MetaWebhookEndpointTests : IClassFixture<MetaWebhookEndpointTests.MetaApiFactory>
{
    private const string AppSecret = "webhook-test-app-secret";
    private const string VerifyToken = "webhook-test-verify-token";
    private const string IntakeKey = "generic-intake-test-key";

    private readonly MetaApiFactory _factory;

    public MetaWebhookEndpointTests(MetaApiFactory factory) => _factory = factory;

    private static string Sign(string body) =>
        "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();

    private static HttpRequestMessage Post(string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/meta/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        if (signature is not null)
            request.Headers.TryAddWithoutValidation("X-Hub-Signature-256", signature);

        return request;
    }

    [Fact]
    public async Task TheVerificationHandshake_EchoesTheChallengeOnlyForTheRightToken()
    {
        var client = _factory.CreateClient();

        var ok = await client.GetAsync(
            $"/api/integrations/meta/webhook?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=abc123");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("abc123", await ok.Content.ReadAsStringAsync());

        var wrong = await client.GetAsync(
            "/api/integrations/meta/webhook?hub.mode=subscribe&hub.verify_token=guessed&hub.challenge=abc123");

        Assert.NotEqual(HttpStatusCode.OK, wrong.StatusCode);
        Assert.NotEqual("abc123", await wrong.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUnsignedDelivery_IsRejectedAndPersistsNothing()
    {
        var client = _factory.CreateClient();
        var pageId = await _factory.SeedEnabledPageAsync("page-unsigned");
        var body = MetaIntegrationHarness.WebhookBody(pageId, "lead-unsigned");

        var response = await client.SendAsync(Post(body, signature: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await _factory.CountEventsAsync("lead-unsigned"));
    }

    [Fact]
    public async Task ADeliveryWithATamperedSignature_IsRejected()
    {
        var client = _factory.CreateClient();
        var pageId = await _factory.SeedEnabledPageAsync("page-tampered");
        var body = MetaIntegrationHarness.WebhookBody(pageId, "lead-tampered");

        // Correctly formed, signed with the wrong key — the case a shared-secret check catches
        // and a mere "header is present" check would not.
        var wrongSignature = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes("not-the-app-secret"), Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();

        var response = await client.SendAsync(Post(body, wrongSignature));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await _factory.CountEventsAsync("lead-tampered"));
    }

    [Fact]
    public async Task AProperlySignedDelivery_IsAcceptedAndPersistedBeforeTheResponse()
    {
        var client = _factory.CreateClient();
        var pageId = await _factory.SeedEnabledPageAsync("page-valid");
        var body = MetaIntegrationHarness.WebhookBody(pageId, "lead-valid");

        var response = await client.SendAsync(Post(body, Sign(body)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Durable before the 200 is returned: Meta considers a delivery complete at that point
        // and there is no second chance to write it down.
        Assert.Equal(1, await _factory.CountEventsAsync("lead-valid"));
    }

    [Fact]
    public async Task ARedeliveredWebhook_DoesNotCreateSecondWork()
    {
        var client = _factory.CreateClient();
        var pageId = await _factory.SeedEnabledPageAsync("page-replay");
        var body = MetaIntegrationHarness.WebhookBody(pageId, "lead-replay");

        await client.SendAsync(Post(body, Sign(body)));
        var second = await client.SendAsync(Post(body, Sign(body)));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, await _factory.CountEventsAsync("lead-replay"));
    }

    [Fact]
    public async Task AnOversizedBody_IsRefusedWithoutBeingProcessed()
    {
        var client = _factory.CreateClient();
        var body = new string('x', 600_000);

        var response = await client.SendAsync(Post(body, Sign(body)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task TheGenericLeadIntakeEndpoint_StillBehavesExactlyAsBefore()
    {
        var client = _factory.CreateClient();
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

        // Untouched by this feature: still key-authenticated, still rejecting a wrong key,
        // and still ingesting through the same pipeline when the key is right.
        var rejected = new HttpRequestMessage(HttpMethod.Post, "/api/lead-intake/portal")
        {
            Content = new StringContent(
                """{"firstName":"Ali","phone":"03001234567"}""", Encoding.UTF8, "application/json")
        };
        rejected.Headers.TryAddWithoutValidation("X-Lead-Intake-Key", "wrong-key");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(rejected)).StatusCode);

        var accepted = new HttpRequestMessage(HttpMethod.Post, "/api/lead-intake/portal")
        {
            Content = new StringContent(
                """{"firstName":"Ali","phone":"03005557788","sourceCode":"website"}""",
                Encoding.UTF8, "application/json")
        };
        accepted.Headers.TryAddWithoutValidation("X-Lead-Intake-Key", IntakeKey);

        var response = await client.SendAsync(accepted);

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected OK, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task TheWebhookEndpointIsAnonymous_ButTheAdminApiIsNot()
    {
        var client = _factory.CreateClient();

        // Meta redirects a browser with no session, so these two must be reachable...
        var verify = await client.GetAsync("/api/integrations/meta/webhook?hub.mode=subscribe&hub.verify_token=x&hub.challenge=y");
        Assert.NotEqual(HttpStatusCode.Unauthorized, verify.StatusCode);

        // ...while everything an admin uses stays locked.
        foreach (var route in new[]
                 {
                     "/api/integrations/meta/connections",
                     "/api/integrations/meta/connections/1/resources",
                     "/api/integrations/meta/connections/1/events",
                 })
        {
            var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var connect = await client.PostAsync("/api/integrations/meta/connect",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, connect.StatusCode);

        var sync = await client.PostAsync("/api/integrations/meta/connections/1/sync",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, sync.StatusCode);
    }

    public sealed class MetaApiFactory : WebApplicationFactory<Program>
    {
        private const string DatabaseName = "meta-webhook-tests";

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
                    ["MetaIntegration:AppId"] = "test-app-id",
                    ["MetaIntegration:AppSecret"] = AppSecret,
                    ["MetaIntegration:WebhookVerifyToken"] = VerifyToken,
                    ["MetaIntegration:OAuthCallbackUrl"] = "https://dams.test/api/integrations/meta/callback",
                    ["MetaIntegration:FrontendReturnUrl"] = "https://dams.test/crm/settings",
                    ["MetaIntegration:MaxWebhookBodyBytes"] = "524288",
                    // Configured so the generic intake regression test exercises its real key
                    // check rather than the "feature switched off" path.
                    ["LeadIntake:ApiKey"] = IntakeKey,
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
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(DatabaseName));

                // Timer-driven workers add nothing here and only create noise under the host.
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                    services.Remove(hosted);
            });
        }

        /// <summary>Creates a connected account with one enabled page and returns its Meta id.</summary>
        public async Task<string> SeedEnabledPageAsync(string pageId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

            if (await db.ExternalIntegrationResources.AnyAsync(r => r.ExternalId == pageId))
                return pageId;

            var connection = new ExternalIntegrationConnection
            {
                Provider = IntegrationProviders.Meta,
                ExternalAccountId = $"account-{pageId}",
                DisplayName = "Webhook Test Account",
                Status = ExternalIntegrationConnectionStatus.Connected
            };
            db.ExternalIntegrationConnections.Add(connection);
            await db.SaveChangesAsync();

            db.ExternalIntegrationResources.Add(new ExternalIntegrationResource
            {
                ExternalIntegrationConnectionId = connection.Id,
                Provider = IntegrationProviders.Meta,
                ResourceType = ExternalResourceTypes.FacebookPage,
                ExternalId = pageId,
                Name = $"Page {pageId}",
                IsEnabled = true,
                IsActive = true,
                IsSubscribed = true
            });
            await db.SaveChangesAsync();

            return pageId;
        }

        public async Task<int> CountEventsAsync(string leadgenId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

            return await db.ExternalIntegrationEvents
                .CountAsync(e => e.EventKey.EndsWith(":" + leadgenId));
        }
    }
}
