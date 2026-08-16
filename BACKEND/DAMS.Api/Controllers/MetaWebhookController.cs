using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Application.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DAMS.Api.Controllers
{
    /// <summary>
    /// Where Meta delivers lead notifications.
    ///
    /// Deliberately separate from LeadIntakeController: that endpoint authenticates trusted
    /// portals with a shared key and is unchanged. This one authenticates Meta by HMAC over the
    /// raw body, and does the least work it possibly can before returning.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/integrations/meta/webhook")]
    [EnableRateLimiting("metaWebhook")]
    public class MetaWebhookController : ControllerBase
    {
        private const string SignatureHeader = "X-Hub-Signature-256";

        private readonly IMetaWebhookIntakeService _intake;
        private readonly MetaIntegrationOptions _options;
        private readonly ILogger<MetaWebhookController> _logger;

        public MetaWebhookController(
            IMetaWebhookIntakeService intake,
            MetaIntegrationOptions options,
            ILogger<MetaWebhookController> logger)
        {
            _intake = intake;
            _options = options;
            _logger = logger;
        }

        /// <summary>Meta's one-time subscription handshake. The challenge is echoed only after the token matches.</summary>
        [HttpGet]
        public IActionResult Verify(
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.verify_token")] string? verifyToken,
            [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            if (!_options.IsConfigured)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);

            if (!string.Equals(mode, "subscribe", StringComparison.Ordinal)
                || !MetaSignature.TokensMatch(_options.WebhookVerifyToken, verifyToken)
                || string.IsNullOrEmpty(challenge))
            {
                _logger.LogWarning("A Meta webhook verification attempt was rejected.");
                return Forbid();
            }

            return Content(challenge, "text/plain");
        }

        [HttpPost]
        public async Task<IActionResult> Receive(CancellationToken cancellationToken)
        {
            if (!_options.IsConfigured)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);

            if (Request.ContentLength > _options.MaxWebhookBodyBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);

            var rawBody = await ReadBoundedBodyAsync(cancellationToken);
            if (rawBody is null)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);

            // The signature covers the exact bytes received, so it is checked before the body
            // is parsed — parsing and re-serializing would produce different bytes.
            if (!MetaSignature.IsValid(rawBody, Request.Headers[SignatureHeader], _options.AppSecret))
            {
                // Never log the body: an unverified payload is attacker-controlled.
                _logger.LogWarning("A Meta webhook delivery was rejected because its signature did not match.");
                return Unauthorized();
            }

            var body = System.Text.Encoding.UTF8.GetString(rawBody);

            try
            {
                var recorded = await _intake.RecordAsync(body, cancellationToken);
                if (recorded > 0)
                    _logger.LogInformation("Recorded {Count} Meta lead event(s) for background processing.", recorded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Meta disables a subscription that keeps receiving errors, and it will redeliver
                // anyway. Absorbing the failure keeps the integration alive; the event key makes
                // the redelivery harmless.
                _logger.LogError(ex, "Recording a Meta webhook delivery failed. Meta will redeliver it.");
            }

            return Ok();
        }

        /// <summary>
        /// Reads the body while enforcing the size limit, for the case where no Content-Length
        /// was sent — a chunked request could otherwise stream unbounded data at us.
        /// </summary>
        private async Task<byte[]?> ReadBoundedBodyAsync(CancellationToken cancellationToken)
        {
            var limit = Math.Max(1024, _options.MaxWebhookBodyBytes);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];

            while (true)
            {
                var read = await Request.Body.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                    break;

                if (buffer.Length + read > limit)
                    return null;

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
    }
}
