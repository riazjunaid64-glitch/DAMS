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

    [Fact]
    public async Task PaginationCursor_HasItsTokenStripped_AndIsNeverFollowedOffTheGraphHost()
    {
        // Meta's own "next" link is a URL it fully constructs and may carry its own copy of the
        // access token. The client must (1) never send that copy of the token onward as-is —
        // replacing it with its own fresh proof instead — and (2) refuse to follow a "next" link
        // that does not actually point back at the Graph API host, in case a response were ever
        // manipulated to redirect a live token somewhere else.
        var callCount = 0;
        var handler = new FakeHandler(request =>
        {
            callCount++;

            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "data": [ { "id": "page-1", "name": "Acme Sales" } ],
                          "paging": { "next": "https://graph.facebook.com/v21.0/me/accounts?access_token=leaked-token&appsecret_proof=stale-proof&after=cursor-1" }
                        }
                        """)
                };
            }

            if (callCount == 2)
            {
                Assert.Equal("graph.facebook.com", request.RequestUri!.Host);
                Assert.DoesNotContain("access_token=leaked-token", request.RequestUri.Query, StringComparison.Ordinal);
                Assert.DoesNotContain("appsecret_proof=stale-proof", request.RequestUri.Query, StringComparison.Ordinal);
                Assert.Contains("after=cursor-1", request.RequestUri.Query, StringComparison.Ordinal);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "data": [ { "id": "page-2", "name": "Acme Rentals" } ],
                          "paging": { "next": "https://evil.example.com/steal?access_token=leaked-token" }
                        }
                        """)
                };
            }

            throw new InvalidOperationException(
                "Pagination must have stopped rather than following a link off the Graph API host.");
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

        // Both pages found before the untrusted link was refused; the walk is reported as
        // truncated rather than "complete", since a link genuinely existed beyond it.
        Assert.Equal(2, result.Items.Count(i => i.ResourceType == ExternalResourceTypes.FacebookPage));
        Assert.True(result.Truncated);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.Host == "evil.example.com");
    }

    [Fact]
    public async Task ReadingLeadForms_KeepsEachFormsQuestionsAndOptions()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                { "data": [{
                    "id": "1180848317322015", "name": "Floria form", "status": "ACTIVE",
                    "questions": [
                      { "key": "are_you_buying_for_?", "label": "Are you buying for ?", "type": "CUSTOM",
                        "options": [ { "key": "investment", "value": "Investment" },
                                     { "key": "personal_living", "value": "Personal Living" } ] },
                      { "key": "full_name", "label": "Full name", "type": "FULL_NAME" }
                    ] }] }
                """)
        });

        var result = await DirectClient(handler).GetLeadFormsAsync("page-1", FakeAccessToken, CancellationToken.None);

        Assert.Contains("questions{key,label,type,options{key,value}}",
            Uri.UnescapeDataString(Assert.Single(handler.Requests).RequestUri!.Query));
        var questions = LeadFormQuestions.Read(Assert.Single(result.Items).MetadataJson);
        Assert.Equal(2, questions.Count);
        Assert.Equal("Are you buying for ?", questions[0].Label);
        Assert.Equal(["investment", "personal_living"], questions[0].Options.Select(o => o.Key));
        Assert.Equal("Personal Living", questions[0].Options[1].Value);
        Assert.Empty(questions[1].Options);
    }

    [Fact]
    public async Task LeadFormsWhoseQuestionsMetaRefuses_AreStillRead_WithoutThem()
    {
        var handler = new FakeHandler(request =>
            Uri.UnescapeDataString(request.RequestUri!.Query).Contains("questions")
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{ "error": { "message": "(#100) Tried accessing nonexisting field (questions)", "code": 100 } }""")
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{ "data": [{ "id": "form-1", "name": "Floria form", "status": "ACTIVE" }] }""")
                });

        var result = await DirectClient(handler).GetLeadFormsAsync("page-1", FakeAccessToken, CancellationToken.None);

        var form = Assert.Single(result.Items);
        Assert.Equal("form-1", form.ExternalId);
        // No questions read means nothing to store — an earlier sync's questions are kept.
        Assert.Null(form.MetadataJson);
        Assert.Equal(2, handler.Requests.Count);
    }

    // ── Leads ───────────────────────────────────────────────────────────────────

    internal const string FloriaLeadgenId = "12345678901234567";

    /// <summary>
    /// A lead as Graph returns it for the live Floria Heights form: the three qualifying
    /// answers, then name and phone. The optional city question was skipped, and Meta leaves an
    /// unanswered question out of field_data entirely rather than sending it empty.
    /// </summary>
    internal static string FloriaLeadJson(string platform = "fb", bool withAdIds = true) => $$"""
        {
          "id": "{{FloriaLeadgenId}}",
          "created_time": "2026-09-20T14:05:31+0000",
          "form_id": "1180848317322015",
          "platform": "{{platform}}",
          "is_organic": false,
          {{(withAdIds
              ? "\"ad_id\": \"120212345678900001\", \"adset_id\": \"120212345678900002\", \"campaign_id\": \"120212345678900003\","
              : "")}}
          "field_data": [
            { "name": "are_you_interested_in_a_5-year_installment_plan?", "values": ["need_more_details"] },
            { "name": "which_apartment_type_are_you_interested_in?", "values": ["2_bedroom_apartment"] },
            { "name": "are_you_buying_for_?", "values": ["investment"] },
            { "name": "full_name", "values": ["Ayesha Siddiqui"] },
            { "name": "phone_number", "values": ["+923001234567"] }
          ]
        }
        """;

    internal const string FloriaAdNamesJson = """
        { "id": "12345678901234567", "ad_name": "Floria 2BR Reel", "adset_name": "Lahore 25-45", "campaign_name": "Floria Heights Launch" }
        """;

    /// <summary>The fields a lead request asked for, so a fake can answer each call on its own terms.</summary>
    internal static string RequestedFields(HttpRequestMessage request) =>
        System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["fields"] ?? string.Empty;

    internal static bool AsksForAdNames(HttpRequestMessage request) =>
        RequestedFields(request).Split(',').Any(f => f is "ad_name" or "adset_name" or "campaign_name");

    /// <summary>Everything Meta's Retrieving Leads guide puts behind ads_management: the names and the ids.</summary>
    internal static bool AsksForAdFields(HttpRequestMessage request) =>
        AsksForAdNames(request)
        || RequestedFields(request).Split(',').Any(f => f is "ad_id" or "adset_id" or "campaign_id");

    internal static HttpResponseMessage GraphJson(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json) };

    internal static HttpResponseMessage GraphError(HttpStatusCode status, int code, string message, int? subCode = null) =>
        GraphJson($$"""
            { "error": { "message": "{{message}}", "type": "OAuthException", "code": {{code}}{{(subCode is null ? "" : $", \"error_subcode\": {subCode}")}} } }
            """, status);

    internal const string AdsManagementRefusal = "(#200) Requires ads_management permission to manage the object";

    [Theory]
    [InlineData("fb")]
    [InlineData("ig")]
    public async Task ReadingALead_ParsesARealFloriaHeightsResponse(string platform)
    {
        var handler = new FakeHandler(_ => GraphJson(FloriaLeadJson(platform)));

        var lead = await DirectClient(handler).GetLeadAsync(FloriaLeadgenId, FakeAccessToken, CancellationToken.None);

        // A 17-digit id is kept as the exact string Meta sent, never passed through a number.
        Assert.Equal(FloriaLeadgenId, lead.LeadgenId);
        Assert.Equal("1180848317322015", lead.FormId);
        Assert.Equal(platform, lead.Platform);
        Assert.False(lead.IsOrganic);
        // Graph writes the offset as "+0000", without the colon ISO 8601 usually has.
        Assert.Equal(new DateTime(2026, 9, 20, 14, 5, 31, DateTimeKind.Utc), lead.CreatedTime);
        Assert.Equal(DateTimeKind.Utc, lead.CreatedTime!.Value.Kind);

        Assert.Equal(
            [
                ("are_you_interested_in_a_5-year_installment_plan?", "need_more_details"),
                ("which_apartment_type_are_you_interested_in?", "2_bedroom_apartment"),
                ("are_you_buying_for_?", "investment"),
                ("full_name", "Ayesha Siddiqui"),
                ("phone_number", "+923001234567")
            ],
            lead.FieldData.Select(f => (f.Name, f.Value)));

        var mapped = MetaLeadFieldMapper.Map(lead.FieldData);
        Assert.Equal("Ayesha", mapped.FirstName);
        Assert.Equal("+923001234567", mapped.Phone);
        Assert.Null(mapped.City);

        Assert.Equal("120212345678900001", lead.AdId);
        Assert.Equal("120212345678900002", lead.AdSetId);
        Assert.Equal("120212345678900003", lead.CampaignId);
        Assert.Contains(FloriaLeadgenId, lead.RawJson, StringComparison.Ordinal);

        // One call, and it never asks for an ad name: that alone is what Meta refuses without
        // ads_management, and it would take the whole lead down with it.
        var request = Assert.Single(handler.Requests);
        Assert.False(AsksForAdNames(request));
        Assert.Null(lead.AdName);
        Assert.Null(lead.AdSetName);
        Assert.Null(lead.CampaignName);
        Assert.Equal(FakeAccessToken, request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ReadingALeadsAdNames_AsksForThemAlone()
    {
        var handler = new FakeHandler(_ => GraphJson(FloriaAdNamesJson));

        var names = await DirectClient(handler).GetLeadAdNamesAsync(FloriaLeadgenId, FakeAccessToken, CancellationToken.None);

        Assert.Equal(new MetaLeadAdNames("Floria 2BR Reel", "Lahore 25-45", "Floria Heights Launch"), names);
        Assert.Equal("ad_name,adset_name,campaign_name", RequestedFields(Assert.Single(handler.Requests)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 200, AdsManagementRefusal)]
    [InlineData(HttpStatusCode.Forbidden, 10, "(#10) Application does not have permission for this action")]
    [InlineData(HttpStatusCode.BadRequest, 100, "(#100) Tried accessing nonexisting field (adset_id) on node type (LeadgenQualifier)")]
    public async Task ALeadWhoseAdIdsMetaRefuses_IsReadWithoutThem_AndTheRefusalIsLoggedWithoutAStackTrace(
        HttpStatusCode status, int code, string message)
    {
        // If Meta holds even the ids back — a permission, or a field renamed in a later Graph
        // version — the lead must still come through rather than parking the whole connection.
        var handler = new FakeHandler(request => RequestedFields(request).Contains("campaign_id")
            ? GraphError(status, code, message)
            : GraphJson(FloriaLeadJson(withAdIds: false)));
        var logs = new CapturingLoggerProvider();

        var lead = await DirectClient(handler, logs).GetLeadAsync(FloriaLeadgenId, FakeAccessToken, CancellationToken.None);

        Assert.Equal(5, lead.FieldData.Count);
        Assert.Null(lead.CampaignId);
        Assert.Null(lead.AdId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("id,created_time,field_data,form_id,platform,is_organic", RequestedFields(handler.Requests[1]));

        var warning = Assert.Single(logs.Messages, m => m.StartsWith("[Warning]", StringComparison.Ordinal));
        Assert.Contains("refused the ad ids", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("[exception:", warning, StringComparison.Ordinal);
    }

    [Theory]
    // A dead session, or a lead that no longer exists, fails the same with or without the ids:
    // asked once, believed at once.
    [InlineData(HttpStatusCode.BadRequest, 190, null, "Error validating access token: The session has been invalidated.", typeof(MetaAuthorizationException), 1)]
    [InlineData(HttpStatusCode.BadRequest, 102, null, "Session key invalid or no longer valid", typeof(MetaAuthorizationException), 1)]
    [InlineData(HttpStatusCode.BadRequest, 200, 460, "The session has been invalidated because the user changed their password.", typeof(MetaAuthorizationException), 1)]
    [InlineData(HttpStatusCode.BadRequest, 100, 33, "Unsupported get request. Object with ID '12345678901234567' does not exist.", typeof(MetaPermanentException), 1)]
    // A permission refusal might be over the ids alone, so it is asked again without them;
    // failing that too, it is about the lead itself and is reported as such.
    [InlineData(HttpStatusCode.Forbidden, 200, null, "(#200) Requires leads_retrieval permission", typeof(MetaAuthorizationException), 2)]
    // Not refusals at all: left for the event processor's own backoff.
    [InlineData(HttpStatusCode.BadRequest, 17, null, "User request limit reached", typeof(MetaTransientException), 1)]
    [InlineData(HttpStatusCode.InternalServerError, 1, null, "An unknown error occurred", typeof(MetaTransientException), 1)]
    public async Task ALeadMetaRefusesOutright_IsClassifiedByItsGraphError(
        HttpStatusCode status, int code, int? subCode, string message, Type expected, int expectedRequests)
    {
        var handler = new FakeHandler(_ => GraphError(status, code, message, subCode));

        var thrown = await Assert.ThrowsAnyAsync<MetaGraphException>(() =>
            DirectClient(handler).GetLeadAsync(FloriaLeadgenId, FakeAccessToken, CancellationToken.None));

        Assert.IsType(expected, thrown);
        Assert.Equal(code, thrown.Code);
        Assert.Equal(subCode, thrown.SubCode);
        Assert.Equal(expectedRequests, handler.Requests.Count);
    }

    internal static MetaGraphClient DirectClient(FakeHandler handler, ILoggerProvider? logs = null) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/v21.0/") },
        new MetaIntegrationOptions
        {
            AppId = "app-id",
            AppSecret = FakeAppSecret,
            WebhookVerifyToken = "verify",
            OAuthCallbackUrl = "https://dams.test/callback"
        },
        logs is null
            ? Microsoft.Extensions.Logging.Abstractions.NullLogger<MetaGraphClient>.Instance
            : LoggerFactory.Create(builder => builder.AddProvider(logs)).CreateLogger<MetaGraphClient>());

    internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    internal sealed class CapturingLoggerProvider : ILoggerProvider
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
                    sink.Add($"[{logLevel}] [{category}] {formatter(state, exception)}" +
                             (exception is null ? "" : $" [exception: {exception.GetType().Name}]"));
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
