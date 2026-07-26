using System.Security.Cryptography;
using System.Text;
using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DAMS.Api.Controllers
{
    /// <summary>
    /// The shared door for external channels — Facebook and Instagram lead ads,
    /// click-to-WhatsApp, property portals, telephony, partner forms. Everything lands in
    /// the same ingestion pipeline as a walk-in, so no channel gets its own lead system.
    ///
    /// Authenticated with a shared key supplied through configuration
    /// (<c>LeadIntake:ApiKey</c>). No key configured means the endpoint is switched off —
    /// there are no built-in or default credentials.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/lead-intake")]
    [EnableRateLimiting("leadIntake")]
    public class LeadIntakeController : ControllerBase
    {
        private const string ApiKeyHeader = "X-Lead-Intake-Key";

        private readonly ILeadService _leads;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LeadIntakeController> _logger;

        public LeadIntakeController(ILeadService leads, IConfiguration configuration, ILogger<LeadIntakeController> logger)
        {
            _leads = leads;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("{provider}")]
        public async Task<IActionResult> Ingest(string provider, [FromBody] LeadIntakeDto dto, CancellationToken cancellationToken)
        {
            var configuredKey = _configuration["LeadIntake:ApiKey"];
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                _logger.LogWarning("Lead intake was called but LeadIntake:ApiKey is not configured.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "External lead intake is not configured." });
            }

            if (!Request.Headers.TryGetValue(ApiKeyHeader, out var suppliedKey) || !KeysMatch(configuredKey, suppliedKey!))
                return Unauthorized(new { message = "Invalid intake key." });

            if (string.IsNullOrWhiteSpace(provider) || provider.Length > 50)
                return BadRequest(new { message = "A provider name of up to 50 characters is required." });

            // The route is the source of truth for who sent this, not the body.
            dto.ExternalProvider = provider.Trim().ToLowerInvariant();
            // An external submission is never rejected as a duplicate: a repeat enquiry is
            // added to the person's existing lead instead of being dropped.
            dto.AllowDuplicate = true;

            try
            {
                var result = await _leads.IngestAsync(dto, actor: null, cancellationToken);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private static bool KeysMatch(string configured, string supplied) =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(configured),
                Encoding.UTF8.GetBytes(supplied));
    }
}
