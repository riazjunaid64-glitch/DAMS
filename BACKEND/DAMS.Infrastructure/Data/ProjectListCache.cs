namespace DAMS.Infrastructure.Data
{
    /// <summary>
    /// Bumped whenever a unit row is inserted, updated or deleted, so the project list
    /// cannot keep serving unit counts from before that change.
    /// </summary>
    public static class ProjectListCache
    {
        private static int _version;

        public static int Version => _version;

        public static void Bump() => System.Threading.Interlocked.Increment(ref _version);
    }
}
