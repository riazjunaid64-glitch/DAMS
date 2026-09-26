using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services.Integrations
{
    /// <summary>
    /// The single place Meta Graph endpoints are constructed and its responses interpreted.
    ///
    /// Three rules hold throughout. Every token-bearing call carries an app secret proof, so a
    /// stolen token alone cannot be used against this app. Every failure is classified into
    /// transient, authorization or permanent, because that decision belongs next to the wire
    /// format rather than in the worker. And every message leaving this class is scrubbed,
    /// because Graph errors quote the request — including its access token — back at you.
    /// </summary>
    public sealed class MetaGraphClient : IMetaGraphClient
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        /// <summary>Graph codes that mean "busy, try later" rather than "wrong".</summary>
        private static readonly HashSet<int> TransientCodes = [1, 2, 4, 17, 32, 341, 613];

        /// <summary>Graph codes that mean the authorization itself is no longer usable.</summary>
        private static readonly HashSet<int> AuthorizationCodes = [10, 102, 190, 200, 299, 803];

        private readonly HttpClient _http;
        private readonly MetaIntegrationOptions _options;
        private readonly ILogger<MetaGraphClient> _logger;

        public MetaGraphClient(HttpClient http, MetaIntegrationOptions options, ILogger<MetaGraphClient> logger)
        {
            _http = http;
            _options = options;
            _logger = logger;
        }

        // ── Authorization ───────────────────────────────────────────────────────────

        public async Task<MetaAuthorizationResult> CompleteAuthorizationAsync(
            string code, CancellationToken cancellationToken = default)
        {
            EnsureConfigured();

            // Short-lived token first; it is only ever used to obtain the long-lived one. Meta's
            // token-exchange endpoint has no access token yet to carry in a header, so unlike
            // every other call in this class (see SendCoreAsync) its credentials travel in the
            // query string, as Meta's own OAuth documentation specifies.
            //
            // KNOWN RESIDUAL RISK, deliberately left open rather than guessed at. On .NET 8 the
            // System.Net.Http EventSource emits the full request URI — query string included —
            // so anything that attaches an EventListener or ETW/APM collector to that provider
            // can observe client_secret and the authorization code. This was verified by running
            // a real loopback request against net8.0 with a live EventListener, not inferred:
            // the secret appears verbatim in the RequestStart payload. It is a different surface
            // from the IHttpClientFactory ILogger categories this app already filters, and there
            // is no supported .NET 8 API to redact it — default URI-query redaction only arrives
            // in .NET 9.
            //
            // Closing it needs a decision that belongs to whoever runs this in production, not to
            // this class: confirm Meta accepts these parameters over a non-URI transport and move
            // them there, move the runtime to a version that redacts by default, or accept it
            // under an explicit policy that no System.Net.Http URI-query diagnostics are
            // collected from this process.
            var shortLived = await GetAsync(
                $"oauth/access_token?client_id={Uri.EscapeDataString(_options.AppId!)}" +
                $"&client_secret={Uri.EscapeDataString(_options.AppSecret!)}" +
                $"&redirect_uri={Uri.EscapeDataString(_options.OAuthCallbackUrl!)}" +
                $"&code={Uri.EscapeDataString(code)}",
                accessToken: null,
                cancellationToken);

            var shortToken = shortLived.RootElement.TryGetProperty("access_token", out var shortTokenElement)
                ? shortTokenElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(shortToken))
                throw new MetaPermanentException("Meta did not return an access token for this authorization.");

            var longLived = await GetAsync(
                $"oauth/access_token?grant_type=fb_exchange_token" +
                $"&client_id={Uri.EscapeDataString(_options.AppId!)}" +
                $"&client_secret={Uri.EscapeDataString(_options.AppSecret!)}" +
                $"&fb_exchange_token={Uri.EscapeDataString(shortToken)}",
                accessToken: null,
                cancellationToken);

            var token = longLived.RootElement.TryGetProperty("access_token", out var tokenElement)
                ? tokenElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(token))
                throw new MetaPermanentException("Meta did not return a long-lived access token.");

            DateTime? expiresAt = longLived.RootElement.TryGetProperty("expires_in", out var expiresIn)
                                  && expiresIn.TryGetInt64(out var seconds) && seconds > 0
                ? DateTime.UtcNow.AddSeconds(seconds)
                : null;

            var me = await GetAsync(WithProof("me?fields=id,name", token), token, cancellationToken);
            var permissions = await GetAsync(WithProof("me/permissions", token), token, cancellationToken);

            return new MetaAuthorizationResult
            {
                AccessToken = token,
                ExpiresAt = expiresAt,
                UserId = ReadString(me.RootElement, "id") ?? string.Empty,
                DisplayName = ReadString(me.RootElement, "name") ?? "Meta account",
                GrantedScopes = ReadGrantedScopes(permissions.RootElement)
            };
        }

        private static List<string> ReadGrantedScopes(JsonElement root)
        {
            var granted = new List<string>();
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return granted;

            foreach (var entry in data.EnumerateArray())
            {
                if (string.Equals(ReadString(entry, "status"), "granted", StringComparison.OrdinalIgnoreCase)
                    && ReadString(entry, "permission") is { Length: > 0 } permission)
                {
                    granted.Add(permission);
                }
            }

            return granted;
        }

        // ── Discovery ───────────────────────────────────────────────────────────────

        private const string PagesWithInstagramFields =
            "me/accounts?fields=id,name,access_token,instagram_business_account{id,name,username}&limit=100";
        private const string PagesWithoutInstagramFields =
            "me/accounts?fields=id,name,access_token&limit=100";

        public async Task<MetaDiscoveryPage> GetPagesAsync(
            string userAccessToken, CancellationToken cancellationToken = default)
        {
            var resources = new List<MetaDiscoveredResource>();

            (List<JsonElement> Items, bool Truncated) result;
            var includesInstagramAccounts = true;
            try
            {
                result = await CollectAsync(PagesWithInstagramFields, userAccessToken, cancellationToken);
            }
            catch (MetaAuthorizationException)
            {
                // instagram_basic is optional (see MetaScopes.Optional) and commonly missing on
                // a freshly connected account. Facebook Page discovery — and therefore lead
                // delivery — must not be held hostage by a field expansion nobody guaranteed. If
                // this retry also fails, it is a real authorization problem and the exception is
                // allowed to propagate normally.
                _logger.LogWarning(
                    "Reading Pages with their linked Instagram account failed, likely because " +
                    "instagram_basic is not granted. Retrying without it.");
                result = await CollectAsync(PagesWithoutInstagramFields, userAccessToken, cancellationToken);
                includesInstagramAccounts = false;
            }

            var (items, truncated) = result;

            foreach (var page in items)
            {
                var pageId = ReadString(page, "id");
                if (pageId is null)
                    continue;

                resources.Add(new MetaDiscoveredResource
                {
                    ResourceType = ExternalResourceTypes.FacebookPage,
                    ExternalId = pageId,
                    Name = ReadString(page, "name"),
                    ResourceToken = ReadString(page, "access_token")
                });

                // An Instagram business account is reached through its page, so it is stored as
                // a child of it — that parent link is what later lets an Instagram lead be
                // labelled correctly instead of being lumped in with Facebook.
                if (page.TryGetProperty("instagram_business_account", out var instagram)
                    && instagram.ValueKind == JsonValueKind.Object
                    && ReadString(instagram, "id") is { Length: > 0 } instagramId)
                {
                    resources.Add(new MetaDiscoveredResource
                    {
                        ResourceType = ExternalResourceTypes.InstagramAccount,
                        ExternalId = instagramId,
                        ParentExternalId = pageId,
                        Name = ReadString(instagram, "username") ?? ReadString(instagram, "name")
                    });
                }
            }

            return new MetaDiscoveryPage
            {
                Items = resources,
                Truncated = truncated,
                IncludesInstagramAccounts = includesInstagramAccounts
            };
        }

        public async Task<MetaDiscoveryPage> GetAdAccountsAsync(
            string userAccessToken, CancellationToken cancellationToken = default)
        {
            var resources = new List<MetaDiscoveredResource>();

            var (items, truncated) = await CollectAsync(
                "me/adaccounts?fields=id,account_id,name,account_status&limit=100",
                userAccessToken, cancellationToken);

            foreach (var account in items)
            {
                if (ReadString(account, "id") is not { Length: > 0 } id)
                    continue;

                resources.Add(new MetaDiscoveredResource
                {
                    ResourceType = ExternalResourceTypes.AdAccount,
                    ExternalId = id,
                    Name = ReadString(account, "name"),
                    ExternalStatus = ReadNumberOrString(account, "account_status")
                });
            }

            return new MetaDiscoveryPage { Items = resources, Truncated = truncated };
        }

        public async Task<MetaDiscoveryPage> GetAdAccountChildrenAsync(
            string adAccountExternalId, string userAccessToken, CancellationToken cancellationToken = default)
        {
            var resources = new List<MetaDiscoveredResource>();
            var account = Uri.EscapeDataString(adAccountExternalId);
            var truncated = false;

            var (campaigns, campaignsTruncated) = await CollectAsync(
                $"{account}/campaigns?fields=id,name,status&limit=100", userAccessToken, cancellationToken);
            truncated |= campaignsTruncated;
            foreach (var campaign in campaigns)
            {
                if (ReadString(campaign, "id") is { Length: > 0 } id)
                    resources.Add(Child(ExternalResourceTypes.Campaign, id, adAccountExternalId, campaign));
            }

            var (adSets, adSetsTruncated) = await CollectAsync(
                $"{account}/adsets?fields=id,name,status,campaign_id&limit=100", userAccessToken, cancellationToken);
            truncated |= adSetsTruncated;
            foreach (var adSet in adSets)
            {
                if (ReadString(adSet, "id") is { Length: > 0 } id)
                    resources.Add(Child(ExternalResourceTypes.AdSet, id, ReadString(adSet, "campaign_id"), adSet));
            }

            var (ads, adsTruncated) = await CollectAsync(
                $"{account}/ads?fields=id,name,status,adset_id&limit=100", userAccessToken, cancellationToken);
            truncated |= adsTruncated;
            foreach (var ad in ads)
            {
                if (ReadString(ad, "id") is { Length: > 0 } id)
                    resources.Add(Child(ExternalResourceTypes.Ad, id, ReadString(ad, "adset_id"), ad));
            }

            return new MetaDiscoveryPage { Items = resources, Truncated = truncated };
        }

        private static MetaDiscoveredResource Child(
            string resourceType, string externalId, string? parentExternalId, JsonElement element) =>
            new()
            {
                ResourceType = resourceType,
                ExternalId = externalId,
                ParentExternalId = parentExternalId,
                Name = ReadString(element, "name"),
                ExternalStatus = ReadString(element, "status")
            };

        public async Task<MetaDiscoveryPage> GetLeadFormsAsync(
            string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default)
        {
            var resources = new List<MetaDiscoveredResource>();
            var page = Uri.EscapeDataString(pageExternalId);

            (List<JsonElement> Items, bool Truncated) result;
            try
            {
                result = await CollectAsync(
                    $"{page}/leadgen_forms?fields=id,name,status,questions{{key,label,type,options{{key,value}}}}&limit=100",
                    pageAccessToken, cancellationToken);
            }
            catch (MetaPermanentException ex)
            {
                // The questions only make answers readable and mappable; they must never cost
                // the forms themselves. Without them, whatever an earlier sync stored is kept.
                _logger.LogWarning(ex,
                    "Reading lead forms with their questions failed for page {PageId}. Retrying without them.",
                    pageExternalId);
                result = await CollectAsync(
                    $"{page}/leadgen_forms?fields=id,name,status&limit=100", pageAccessToken, cancellationToken);
            }

            var (items, truncated) = result;

            foreach (var form in items)
            {
                if (ReadString(form, "id") is not { Length: > 0 } id)
                    continue;

                var resource = Child(ExternalResourceTypes.LeadForm, id, pageExternalId, form);
                if (form.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
                    resource.MetadataJson = LeadFormQuestions.Serialize(ReadQuestions(questions));
                resources.Add(resource);
            }

            return new MetaDiscoveryPage { Items = resources, Truncated = truncated };
        }

        private static List<LeadFormQuestionDto> ReadQuestions(JsonElement questions)
        {
            var read = new List<LeadFormQuestionDto>();

            foreach (var question in questions.EnumerateArray())
            {
                if (question.ValueKind != JsonValueKind.Object || ReadString(question, "key") is not { Length: > 0 } key)
                    continue;

                var item = new LeadFormQuestionDto
                {
                    Key = key,
                    Label = ReadString(question, "label"),
                    Type = ReadString(question, "type")
                };

                if (question.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                {
                    foreach (var option in options.EnumerateArray())
                    {
                        if (option.ValueKind == JsonValueKind.Object && ReadString(option, "key") is { Length: > 0 } optionKey)
                            item.Options.Add(new LeadFormOptionDto { Key = optionKey, Value = ReadString(option, "value") });
                    }
                }

                read.Add(item);
            }

            return read;
        }

        // ── Leads ───────────────────────────────────────────────────────────────────

        private const string LeadFields = "id,created_time,field_data,form_id,platform,is_organic";
        private const string LeadAdIdFields = "ad_id,adset_id,campaign_id";
        private const string LeadAdNameFields = "ad_name,adset_name,campaign_name";

        /// <summary>
        /// Meta puts ad-specific lead fields behind ads_management, which DAMS does not request,
        /// and refuses the whole call when it refuses one field. So the lead is read on its own
        /// terms first and its ad attribution is only ever added on top: an attribution Meta will
        /// not share must cost the names, never the lead, and never the connection's status.
        /// </summary>
        public async Task<MetaLead> GetLeadAsync(
            string leadgenId, string accessToken, CancellationToken cancellationToken = default)
        {
            var lead = await ReadLeadAsync(leadgenId, accessToken, cancellationToken);

            if (lead.AdId is not null || lead.AdSetId is not null || lead.CampaignId is not null)
                await ReadAdNamesAsync(lead, accessToken, cancellationToken);

            return lead;
        }

        private async Task<MetaLead> ReadLeadAsync(
            string leadgenId, string accessToken, CancellationToken cancellationToken)
        {
            var path = $"{Uri.EscapeDataString(leadgenId)}?fields=";

            JsonDocument document;
            try
            {
                document = await GetAsync(
                    WithProof($"{path}{LeadFields},{LeadAdIdFields}", accessToken), accessToken, cancellationToken);
            }
            catch (Exception ex) when (ex is MetaAuthorizationException or MetaPermanentException)
            {
                // The ids are what tie a lead to its campaign, but whether Meta shares even them
                // without ads permissions is not something DAMS can count on. If this retry also
                // fails, the lead itself is unreadable and that exception is the real one.
                _logger.LogWarning(ex,
                    "Reading lead {LeadgenId} with its ad, ad set and campaign ids failed. Retrying without them.",
                    leadgenId);
                document = await GetAsync(WithProof($"{path}{LeadFields}", accessToken), accessToken, cancellationToken);
            }

            using (document)
                return ReadLead(document.RootElement, leadgenId);
        }

        private async Task ReadAdNamesAsync(MetaLead lead, string accessToken, CancellationToken cancellationToken)
        {
            try
            {
                using var document = await GetAsync(
                    WithProof($"{Uri.EscapeDataString(lead.LeadgenId)}?fields={LeadAdNameFields}", accessToken),
                    accessToken,
                    cancellationToken);

                var root = document.RootElement;
                lead.AdName = ReadString(root, "ad_name");
                lead.AdSetName = ReadString(root, "adset_name");
                lead.CampaignName = ReadString(root, "campaign_name");
            }
            catch (MetaGraphException ex)
            {
                // Whatever the reason — a permission, a renamed field, Meta being busy — the names
                // are only labels. The lead goes ahead without them, and the event processor falls
                // back to whatever resource sync has discovered for these ids.
                _logger.LogWarning(ex,
                    "Reading the ad, ad set and campaign names of lead {LeadgenId} failed. Continuing without them.",
                    lead.LeadgenId);
            }
        }

        private static MetaLead ReadLead(JsonElement root, string leadgenId)
        {
            var lead = new MetaLead
            {
                LeadgenId = ReadString(root, "id") ?? leadgenId,
                FormId = ReadString(root, "form_id"),
                AdId = ReadString(root, "ad_id"),
                AdSetId = ReadString(root, "adset_id"),
                CampaignId = ReadString(root, "campaign_id"),
                Platform = ReadString(root, "platform"),
                // Preserved verbatim: a question DAMS does not recognise today must still be
                // recoverable tomorrow without asking Meta again.
                RawJson = root.GetRawText()
            };

            if (root.TryGetProperty("is_organic", out var organic)
                && organic.ValueKind is JsonValueKind.True or JsonValueKind.False)
                lead.IsOrganic = organic.GetBoolean();

            if (ReadString(root, "created_time") is { Length: > 0 } created
                && DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAt))
                lead.CreatedTime = createdAt.UtcDateTime;

            if (root.TryGetProperty("field_data", out var fields) && fields.ValueKind == JsonValueKind.Array)
            {
                foreach (var field in fields.EnumerateArray())
                {
                    if (ReadString(field, "name") is not { Length: > 0 } name)
                        continue;

                    string? value = null;
                    if (field.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
                    {
                        var parts = values.EnumerateArray()
                            .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
                            .Where(v => !string.IsNullOrWhiteSpace(v));
                        value = string.Join(", ", parts);
                    }

                    lead.FieldData.Add(new MetaFieldAnswer(name, string.IsNullOrWhiteSpace(value) ? null : value));
                }
            }

            return lead;
        }

        // ── Webhook subscription ────────────────────────────────────────────────────

        public Task SubscribePageAsync(string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default) =>
            PostAsync(
                WithProof($"{Uri.EscapeDataString(pageExternalId)}/subscribed_apps?subscribed_fields=leadgen", pageAccessToken),
                pageAccessToken,
                cancellationToken);

        public Task UnsubscribePageAsync(string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default) =>
            SendAsync(
                HttpMethod.Delete,
                WithProof($"{Uri.EscapeDataString(pageExternalId)}/subscribed_apps", pageAccessToken),
                pageAccessToken,
                cancellationToken);

        // ── Plumbing ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Walks a cursor-paginated edge to completion and reports whether it actually reached
        /// the end. The page cap is a safety net — a malformed "next" link that pointed back at
        /// itself would otherwise loop until the process died — but hitting it means the result
        /// is known to be incomplete, which the caller must not mistake for "there is no more".
        /// The same is true if a "next" link is refused for pointing somewhere untrusted: the
        /// walk stops, but the result is still incomplete, not empty.
        /// </summary>
        private async Task<(List<JsonElement> Items, bool Truncated)> CollectAsync(
            string relativeUrl, string accessToken, CancellationToken cancellationToken)
        {
            var items = new List<JsonElement>();
            var url = WithProof(relativeUrl, accessToken);
            var pagesFetched = 0;
            var stoppedForUntrustedUrl = false;

            while (url is not null && pagesFetched < Math.Max(1, _options.MaxGraphPages))
            {
                pagesFetched++;

                // The Authorization header is attached on every page, not only the first.
                using var document = await GetAsync(url, accessToken, cancellationToken);
                var root = document.RootElement;

                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                        items.Add(item.Clone());
                }

                var next = ReadNextPageUrl(root);
                if (next is null)
                {
                    url = null;
                }
                else
                {
                    url = SanitizeNextPageUrl(next, accessToken);
                    if (url is null)
                        stoppedForUntrustedUrl = true;
                }
            }

            var truncated = stoppedForUntrustedUrl || url is not null;
            if (truncated)
                _logger.LogWarning(
                    "Stopped following Meta pagination after {Pages} pages. Some resources may not have been discovered.",
                    pagesFetched);

            return (items, truncated);
        }

        private static string? ReadNextPageUrl(JsonElement root) =>
            root.TryGetProperty("paging", out var paging)
            && paging.ValueKind == JsonValueKind.Object
            && ReadString(paging, "next") is { Length: > 0 } next
                ? next
                : null;

        /// <summary>
        /// Meta's own "next" cursor is a fully-formed URL it constructs itself, commonly
        /// carrying its own copy of the access token as a query parameter — a detail of how
        /// Graph implements pagination, not something this client's move to a Bearer header can
        /// change. It is refused outright unless it actually points back at the Graph API host
        /// this client talks to, so a manipulated response cannot walk this client — carrying a
        /// live access token — off to an attacker-controlled server. Once trusted, any token or
        /// proof Meta put on it is stripped and replaced with a fresh proof of this call's own
        /// token, so the credential riding in the URL is never simply whatever Meta handed back.
        /// </summary>
        private string? SanitizeNextPageUrl(string nextUrl, string accessToken)
        {
            if (!Uri.TryCreate(nextUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !IsTrustedGraphHost(uri.Host))
            {
                _logger.LogWarning(
                    "Meta returned a pagination link that did not point at the expected Graph API host; refusing to follow it.");
                return null;
            }

            var keptParams = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith("access_token=", StringComparison.OrdinalIgnoreCase)
                            && !p.StartsWith("appsecret_proof=", StringComparison.OrdinalIgnoreCase));

            var sanitizedQuery = string.Join('&', keptParams);
            var basePath = uri.GetLeftPart(UriPartial.Path);
            var sanitized = sanitizedQuery.Length > 0 ? $"{basePath}?{sanitizedQuery}" : basePath;

            return WithProof(sanitized, accessToken);
        }

        private bool IsTrustedGraphHost(string host) =>
            string.Equals(_http.BaseAddress?.Host ?? "graph.facebook.com", host, StringComparison.OrdinalIgnoreCase);

        private async Task<JsonDocument> GetAsync(string url, string? accessToken, CancellationToken cancellationToken)
        {
            using var response = await SendCoreAsync(HttpMethod.Get, url, accessToken, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            EnsureSuccess(response, body);

            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                throw new MetaPermanentException("Meta returned a response that could not be read as JSON.", ex);
            }
        }

        private Task PostAsync(string url, string? accessToken, CancellationToken cancellationToken) =>
            SendAsync(HttpMethod.Post, url, accessToken, cancellationToken);

        private async Task SendAsync(HttpMethod method, string url, string? accessToken, CancellationToken cancellationToken)
        {
            using var response = await SendCoreAsync(method, url, accessToken, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, body);
        }

        /// <summary>
        /// Carries the token as an OAuth 2.0 Bearer header rather than a URL query parameter,
        /// which Meta accepts equally — the query string is reserved for appsecret_proof, so
        /// nothing this client builds puts the token itself somewhere a proxy, an APM agent or
        /// any other logger that captures full request URIs could pick it up.
        /// </summary>
        private async Task<HttpResponseMessage> SendCoreAsync(
            HttpMethod method, string url, string? accessToken, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);
                if (accessToken is { Length: > 0 })
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                return await _http.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MetaTransientException("The request to Meta timed out.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new MetaTransientException("Meta could not be reached.", ex);
            }
        }

        /// <summary>
        /// Turns an unsuccessful response into the one exception type that tells the caller what
        /// to do. Meta's own error code is more reliable than the HTTP status, so it wins where
        /// both are present.
        /// </summary>
        private static void EnsureSuccess(HttpResponseMessage response, string body)
        {
            if (response.IsSuccessStatusCode)
                return;

            var (code, subCode, message) = ReadError(body);
            var detail = MetaCredentialScrubber.Scrub(message) ?? $"Meta returned {(int)response.StatusCode}.";

            if (code is { } errorCode)
            {
                if (AuthorizationCodes.Contains(errorCode) || subCode is >= 458 and <= 467)
                    throw new MetaAuthorizationException(detail);

                if (TransientCodes.Contains(errorCode))
                    throw new MetaTransientException(detail);
            }

            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new MetaAuthorizationException(detail),
                HttpStatusCode.TooManyRequests => new MetaTransientException(detail),
                >= HttpStatusCode.InternalServerError => new MetaTransientException(detail),
                _ => (MetaGraphException)new MetaPermanentException(detail)
            };
        }

        private static (int? Code, int? SubCode, string? Message) ReadError(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return (null, null, null);

            try
            {
                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                    return (null, null, null);

                int? code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var codeValue) ? codeValue : null;
                int? subCode = error.TryGetProperty("error_subcode", out var s) && s.TryGetInt32(out var subValue) ? subValue : null;

                return (code, subCode, ReadString(error, "message"));
            }
            catch (JsonException)
            {
                return (null, null, null);
            }
        }

        /// <summary>
        /// Appends only the app secret proof — never the access token itself, which travels as
        /// an Authorization header instead (see <see cref="SendCoreAsync"/>). The proof is a
        /// one-way HMAC of the token, safe to sit in a URL: it lets Meta reject a call made with
        /// a token stolen from somewhere else, since only this app knows the secret used to sign
        /// it, but it cannot be used to reconstruct the token or the secret.
        /// </summary>
        private string WithProof(string relativeUrl, string accessToken)
        {
            if (string.IsNullOrWhiteSpace(_options.AppSecret))
                return relativeUrl;

            var separator = relativeUrl.Contains('?') ? '&' : '?';
            return $"{relativeUrl}{separator}appsecret_proof={AppSecretProof(accessToken, _options.AppSecret)}";
        }

        private static string AppSecretProof(string accessToken, string appSecret)
        {
            var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(accessToken));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private void EnsureConfigured()
        {
            if (!_options.IsConfigured)
                throw new MetaPermanentException("The Meta integration is not configured.");
        }

        private static string? ReadString(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static string? ReadNumberOrString(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null
                }
                : null;
    }
}
