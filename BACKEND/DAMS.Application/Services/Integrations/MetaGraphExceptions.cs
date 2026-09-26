namespace DAMS.Application.Services.Integrations
{
    /// <summary>
    /// Base for every failure the Graph client reports. Nothing else escapes MetaGraphClient,
    /// so callers can decide what to do from the type alone rather than by inspecting status
    /// codes they would have to keep in step with Meta.
    /// </summary>
    public abstract class MetaGraphException : Exception
    {
        protected MetaGraphException(string message, Exception? inner = null, int? code = null, int? subCode = null)
            : base(message, inner)
        {
            Code = code;
            SubCode = subCode;
        }

        /// <summary>Meta's own error code, when the response carried one.</summary>
        public int? Code { get; }

        public int? SubCode { get; }
    }

    /// <summary>Worth trying again later: a timeout, a rate limit, or a Meta-side outage.</summary>
    public sealed class MetaTransientException : MetaGraphException
    {
        public MetaTransientException(string message, Exception? inner = null, int? code = null, int? subCode = null)
            : base(message, inner, code, subCode) { }
    }

    /// <summary>
    /// The authorization is no longer good — expired, revoked, or missing a permission.
    /// Retrying cannot fix this; only an admin reconnecting can.
    /// </summary>
    public sealed class MetaAuthorizationException : MetaGraphException
    {
        public MetaAuthorizationException(string message, Exception? inner = null, int? code = null, int? subCode = null)
            : base(message, inner, code, subCode) { }
    }

    /// <summary>The request will never succeed as asked: a deleted lead, a malformed id.</summary>
    public sealed class MetaPermanentException : MetaGraphException
    {
        public MetaPermanentException(string message, Exception? inner = null, int? code = null, int? subCode = null)
            : base(message, inner, code, subCode) { }
    }
}
