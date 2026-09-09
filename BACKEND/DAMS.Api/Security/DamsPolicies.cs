using DAMS.Application.Services;
using Microsoft.AspNetCore.Authorization;

namespace DAMS.Api.Security
{
    /// <summary>
    /// Named authorization policies, so a controller states the rule it depends on rather than
    /// re-assembling it out of roles and claims where a future edit can quietly weaken it.
    /// </summary>
    public static class DamsPolicies
    {
        /// <summary>
        /// A client whose email address has actually been proven. Role alone is not enough: a
        /// token minted before verification existed carries <c>Role=Client</c> and no
        /// <c>email_verified</c> claim, so requiring the claim is what makes every one of those
        /// sessions fail closed the moment this ships rather than continuing to work until it
        /// happens to expire.
        /// </summary>
        public const string VerifiedClient = "VerifiedClient";

        public static void AddDamsPolicies(this AuthorizationOptions options)
        {
            options.AddPolicy(VerifiedClient, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole("Client")
                .RequireClaim(TokenService.EmailVerifiedClaimType, "true"));
        }
    }
}
