using DAMS.Domain.Entities;

namespace DAMS.Application.Interfaces
{
    public interface ITokenService
    {
        string GenerateAccessToken(User user, string roleName);
        string GenerateRefreshToken();
    }
}
