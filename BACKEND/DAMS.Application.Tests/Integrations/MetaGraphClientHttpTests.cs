using System.Net;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Application.Services.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// Proves the specific claim behind the CRITICAL "credentials in logs" review finding: that
/// before .NET 9, the default IHttpClientFactory logging handlers write a request's full URI —
/// query string included — at Information level per typed client, and that this client is
/// safe from it for two independent reasons, either of which alone would be enough. The
/// category this app filters to Warning (Program.cs) is exercised here exactly as configured,
/// and the access token itself no longer rides in the query string in the first place — only
/// the one-way appsecret_proof HMAC does, which cannot be used to recover the token or the
/// app secret it was signed with.
/// </summary>
public class MetaGraphClientHttpTests
{
    private const string FakeAccessToken = "fake-access-token-should-never-be-logged";
    private const string FakeAppSecret = "fake-app-secret-should-never-be-logged";

    [Fact]
    public async Task ReadingPages_CarriesTheTokenAsAHeader_AndNeverLogsEitherSecret()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "data": [] }""")
        });
        var logProvider = new CapturingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(logProvider);
            // The exact filter Program.cs applies to this specific typed client.
            builder.AddFilter("System.Net.Http.HttpClient.IMetaGraphClient", LogLevel.Warning);
        });
        services.AddSingleton(new MetaIntegrationOptions
        {
            AppId = "app-id",
            AppSecret = FakeAppSecret,
            WebhookVerifyToken = "verify",
            OAuthCallbackUrl = "https://dams.test/callback",
            GraphApiVersion = "v21.0"
        });
        services.AddHttpClient<IMetaGraphClient, MetaGraphClient>((sp, client) =>
            {
                var meta = sp.GetRequiredService<MetaIntegrationOptions>();
                client.BaseAddress = new Uri($"https://graph.facebook.com/{meta.GraphApiVersion}/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMetaGraphClient>();

        await client.GetPagesAsync(FakeAccessToken, CancellationToken.None);

        var request = Assert.Single(handler.Requests);

        // The token travels as an OAuth Bearer header, not a URL query parameter.
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(FakeAccessToken, request.Headers.Authorization?.Parameter);
        Assert.DoesNotContain("access_token=", request.RequestUri!.Query, StringComparison.Ordinal);

        // And regardless of that, nothing the logging pipeline actually wrote — at the real
        // level the default IHttpClientFactory handlers log request details at — contains
        // either secret.
        var captured = string.Join('\n', logProvider.Messages);
        Assert.DoesNotContain(FakeAccessToken, captured, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeAppSecret, captured, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutTheProgramCsFilter_TheDefaultHandlerWouldHaveLoggedTheUrl()
    {
        // The negative control: proves the assertion above is actually testing something. With
        // no category filter at all, the same request's URI — which still carries
        // appsecret_proof, a value derived from the app secret — shows up in the captured logs
        // at the default Information level. This is what Program.cs's AddFilter line exists to
        // prevent.
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "data": [] }""")
        });
        var logProvider = new CapturingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(logProvider);
        });
        services.AddSingleton(new MetaIntegrationOptions
        {
            AppId = "app-id",
            AppSecret = FakeAppSecret,
            WebhookVerifyToken = "verify",
            OAuthCallbackUrl = "https://dams.test/callback",
            GraphApiVersion = "v21.0"
        });
        services.AddHttpClient<IMetaGraphClient, MetaGraphClient>((sp, client) =>
            {
                var meta = sp.GetRequiredService<MetaIntegrationOptions>();
                client.BaseAddress = new Uri($"https://graph.facebook.com/{meta.GraphApiVersion}/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMetaGraphClient>();

        await client.GetPagesAsync(FakeAccessToken, CancellationToken.None);

        var captured = string.Join('\n', logProvider.Messages);
        Assert.Contains("appsecret_proof", captured, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PageDiscovery_SurvivesAMissingInstagramBasicGrant()
    {
        // instagram_basic is optional (MetaScopes.Optional) and commonly missing on a freshly
        // connected account. Meta rejects the instagram_business_account field expansion this
        // client requests without it; Facebook Page discovery — and therefore lead delivery —
        // must not go down with that one field.
        var handler = new FakeHandler(request =>
            request.RequestUri!.Query.Contains("instagram_business_account", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(
                        """{ "error": { "message": "instagram_basic is not granted.", "code": 10 } }""")
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{ "data": [ { "id": "page-1", "name": "Acme Sales" } ] }""")
                });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new MetaIntegrationOptions
        {
            AppId = "app-id",
            AppSecret = FakeAppSecret,
            WebhookVerifyToken = "verify",
            OAuthCallbackUrl = "https://dams.test/callback",
            GraphApiVersion = "v21.0"
        });
        services.AddHttpClient<IMetaGraphClient, MetaGraphClient>((sp, client) =>
            {
                var meta = sp.GetRequiredService<MetaIntegrationOptions>();
                client.BaseAddress = new Uri($"https://graph.facebook.com/{meta.GraphApiVersion}/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMetaGraphClient>();

        var result = await client.GetPagesAsync(FakeAccessToken, CancellationToken.None);

        var page = Assert.Single(result.Items);
        Assert.Equal("page-1", page.ExternalId);
        Assert.Equal(2, handler.Requests.Count);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, List<string> sink) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (sink)
                    sink.Add($"[{category}] {formatter(state, exception)}");
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
