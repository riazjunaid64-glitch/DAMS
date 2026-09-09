using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace DAMS.Application.Services
{
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _configuration;

        public TokenService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// The claim that says this login's email address has been proven. Named as a bare
        /// <c>email_verified</c> rather than a URI so it reads the same way in a decoded token as
        /// it does in the policy that requires it.
        /// </summary>
        public const string EmailVerifiedClaimType = "email_verified";

        public string GenerateAccessToken(User user, string roleName)
        {
            // NameIdentifier is the authenticated subject and the only thing an ownership
            // decision may read. Email rides along for display and contact — it is a label on the
            // token, never a key, and nothing downstream may authorize on it.
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Name, user.FullName),
                new(ClaimTypes.Role, roleName)
            };

            // Proof, carried in the token, that this account's mailbox was verified. Client portal
            // endpoints require it, which is what makes tokens minted before verification existed
            // fail closed the moment this ships: they cannot carry a claim that did not exist when
            // they were signed.
            if (user.EmailVerifiedAt.HasValue)
                claims.Add(new Claim(EmailVerifiedClaimType, "true"));

            var jwtSettings = _configuration.GetSection("Jwt");
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings["Key"]!));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            var bytes = new byte[64];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes);
        }
    }
}
