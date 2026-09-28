using DAMS.Domain.Enums;

namespace DAMS.Application.Security
{
    /// <summary>
    /// The live facts an access token is checked against. Cached briefly so an ordinary request
    /// does not read them from the database every time, and dropped the moment they change.
    /// </summary>
    public readonly record struct AccessSessionStamp(
        int TokenVersion,
        UserAccountStatus AccountStatus,
        string RoleName,
        bool HasActiveEmployee);

    public interface IAccessSessionCache
    {
        bool TryGet(int userId, out AccessSessionStamp stamp);

        void Set(int userId, AccessSessionStamp stamp);

        void Invalidate(int userId);
    }
}
