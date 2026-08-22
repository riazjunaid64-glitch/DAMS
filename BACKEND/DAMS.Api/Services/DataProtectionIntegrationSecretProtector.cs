using System.Security.Cryptography;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace DAMS.Api.Services
{
    /// <summary>
    /// Protects integration credentials with ASP.NET Core Data Protection rather than any
    /// hand-rolled cryptography.
    ///
    /// The purpose string is versioned: changing it invalidates every previously stored
    /// secret, which is a deliberate escape hatch, not something to do casually.
    /// </summary>
    public sealed class DataProtectionIntegrationSecretProtector : IIntegrationSecretProtector
    {
        private const string Purpose = "DAMS.Integrations.v1";

        private readonly IDataProtector _protector;
        private readonly ILogger<DataProtectionIntegrationSecretProtector> _logger;

        public DataProtectionIntegrationSecretProtector(
            IDataProtectionProvider provider,
            ILogger<DataProtectionIntegrationSecretProtector> logger)
        {
            _protector = provider.CreateProtector(Purpose);
            _logger = logger;
        }

        public string Protect(string plaintext) => _protector.Protect(plaintext);

        public string? TryUnprotect(string? protectedValue)
        {
            if (string.IsNullOrWhiteSpace(protectedValue))
                return null;

            try
            {
                return _protector.Unprotect(protectedValue);
            }
            catch (CryptographicException ex)
            {
                // Never log the value itself, and never surface the exception: the caller
                // turns this into "reconnect required", which is the honest remedy.
                _logger.LogError(ex,
                    "A stored integration credential could not be decrypted. The data-protection key ring " +
                    "has most likely been lost or rotated; affected connections must be reconnected.");
                return null;
            }
        }
    }
}
