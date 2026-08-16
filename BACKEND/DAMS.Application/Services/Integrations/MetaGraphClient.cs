using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DAMS.Application.Common;
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

            // Short-lived token first; it is only ever used to obtain the long-lived one.
            var shortLived = await GetAsync(
                $"oauth/access_token?client_id={Uri.EscapeDataString(_options.AppId!)}" +
                $"&client_secret={Uri.EscapeDataString(_options.AppSecret!)}" +
                $"&redirect_uri={Uri.EscapeDataString(_options.OAuthCallbackUrl!)}" +
                $"&code={Uri.EscapeDataString(code)}",
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

            var me = await GetAsync(WithToken("me?fields=id,name", token), cancellationToken);
            var permissions = await GetAsync(WithToken("me/permissions", token), cancellationToken);

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

        public async Task<MetaDiscoveryPage> GetPagesAsync(
            string userAccessToken, CancellationToken cancellationToken = default)
        {
            var resources = new List<MetaDiscoveredResource>();

            var (items, truncated) = await CollectAsync(
                "me/accounts?fields=id,name,access_token,instagram_business_account{id,name,username}&limit=100",
                userAccessToken, cancellationToken);

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

            return new MetaDiscoveryPage { Items = resources, Truncated = truncated };
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

            var (items, truncated) = await CollectAsync(
                $"{Uri.EscapeDataString(pageExternalId)}/leadgen_forms?fields=id,name,status&limit=100",
                pageAccessToken, cancellationToken);

            foreach (var form in items)
            {
                if (ReadString(form, "id") is { Length: > 0 } id)
                    resources.Add(Child(ExternalResourceTypes.LeadForm, id, pageExternalId, form));
            }

            return new MetaDiscoveryPage { Items = resources, Truncated = truncated };
        }

        // ── Leads ───────────────────────────────────────────────────────────────────

        public async Task<MetaLead> GetLeadAsync(
            string leadgenId, string accessToken, CancellationToken cancellationToken = default)
        {
            using var document = await GetAsync(
                WithToken(
                    $"{Uri.EscapeDataString(leadgenId)}?fields=id,created_time,field_data,form_id,platform," +
                    "is_organic,ad_id,ad_name,adset_id,adset_name,campaign_id,campaign_name",
                    accessToken),
                cancellationToken);

            var root = document.RootElement;

            var lead = new MetaLead
            {
                LeadgenId = ReadString(root, "id") ?? leadgenId,
                FormId = ReadString(root, "form_id"),
                AdId = ReadString(root, "ad_id"),
                AdName = ReadString(root, "ad_name"),
                AdSetId = ReadString(root, "adset_id"),
                AdSetName = ReadString(root, "adset_name"),
                CampaignId = ReadString(root, "campaign_id"),
                CampaignName = ReadString(root, "campaign_name"),
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
                WithToken($"{Uri.EscapeDataString(pageExternalId)}/subscribed_apps?subscribed_fields=leadgen", pageAccessToken),
                cancellationToken);

        public Task UnsubscribePageAsync(string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default) =>
            SendAsync(
                HttpMethod.Delete,
                WithToken($"{Uri.EscapeDataString(pageExternalId)}/subscribed_apps", pageAccessToken),
                cancellationToken);

        // ── Plumbing ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Walks a cursor-paginated edge to completion and reports whether it actually reached
        /// the end. The page cap is a safety net — a malformed "next" link that pointed back at
        /// itself would otherwise loop until the process died — but hitting it means the result
        /// is known to be incomplete, which the caller must not mistake for "there is no more".
        /// </summary>
        private async Task<(List<JsonElement> Items, bool Truncated)> CollectAsync(
            string relativeUrl, string accessToken, CancellationToken cancellationToken)
        {
            var items = new List<JsonElement>();
            var url = WithToken(relativeUrl, accessToken);
            var pagesFetched = 0;

            while (url is not null && pagesFetched < Math.Max(1, _options.MaxGraphPages))
            {
                pagesFetched++;

                using var document = await GetAsync(url, cancellationToken);
                var root = document.RootElement;

                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                        items.Add(item.Clone());
                }

                url = ReadNextPageUrl(root);
            }

            var truncated = url is not null;
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

        private async Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken)
        {
            using var response = await SendCoreAsync(HttpMethod.Get, url, cancellationToken);
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

        private Task PostAsync(string url, CancellationToken cancellationToken) =>
            SendAsync(HttpMethod.Post, url, cancellationToken);

        private async Task SendAsync(HttpMethod method, string url, CancellationToken cancellationToken)
        {
            using var response = await SendCoreAsync(method, url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, body);
        }

        private async Task<HttpResponseMessage> SendCoreAsync(
            HttpMethod method, string url, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);
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
        /// Appends the token and its app secret proof. The proof lets Meta reject a call made
        /// with a token that was stolen from somewhere else, since only this app knows the
        /// secret used to sign it.
        /// </summary>
        private string WithToken(string relativeUrl, string accessToken)
        {
            var separator = relativeUrl.Contains('?') ? '&' : '?';
            var url = $"{relativeUrl}{separator}access_token={Uri.EscapeDataString(accessToken)}";

            if (string.IsNullOrWhiteSpace(_options.AppSecret))
                return url;

            return $"{url}&appsecret_proof={AppSecretProof(accessToken, _options.AppSecret)}";
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
