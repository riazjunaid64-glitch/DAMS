using DAMS.Application.Security;
using Microsoft.Extensions.Caching.Memory;

namespace DAMS.Api.Security
{
    /// <summary>
    /// Process-local copy of each login's session stamp. Thirty seconds is the longest a stamp
    /// can outlive a change this process did not see; a change made here removes it at once.
    /// </summary>
    public sealed class MemoryAccessSessionCache : IAccessSessionCache
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

        private readonly IMemoryCache _cache;

        public MemoryAccessSessionCache(IMemoryCache cache) => _cache = cache;

        private static string Key(int userId) => "access-session:" + userId;

        public bool TryGet(int userId, out AccessSessionStamp stamp)
        {
            if (_cache.TryGetValue(Key(userId), out AccessSessionStamp? found) && found.HasValue)
            {
                stamp = found.Value;
                return true;
            }

            stamp = default;
            return false;
        }

        public void Set(int userId, AccessSessionStamp stamp) =>
            _cache.Set(Key(userId), stamp, Lifetime);

        public void Invalidate(int userId) => _cache.Remove(Key(userId));
    }
}
