namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One record of a money-creating request that has already been accepted, so that repeating it
    /// cannot record the money twice.
    /// <para>
    /// The failure this exists to stop is not a double click — the UI already blocks that. It is the
    /// request that reaches the server, commits, and then loses its response on the way back: the
    /// operator sees a network error, presses Save again, and a second expense, receipt or deposit is
    /// created. Nothing about that second request looks wrong from the server's side, which is why
    /// the caller has to name the operation and the server has to remember the name.
    /// </para>
    /// <para>
    /// A row is written BEFORE the operation runs, and the unique index on <see cref="Key"/> is what
    /// makes the reservation atomic — two simultaneous copies of the same request cannot both get
    /// past it. It is completed with the response once the operation succeeds, so a later retry is
    /// answered with the original result rather than being executed again. A request that fails
    /// validation deletes its own reservation: the operator has to be able to correct the amount and
    /// submit the same intent again.
    /// </para>
    /// </summary>
    public class IdempotentRequest
    {
        public int Id { get; set; }

        /// <summary>The caller-supplied key, from the <c>Idempotency-Key</c> header. Stable across
        /// retries of one intent and different for a deliberate second entry, which is why it has to
        /// come from the client rather than be generated per request on the server.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Method and path, e.g. <c>POST /api/finance/expenses</c>. Recorded so one key
        /// cannot be replayed against a different operation.</summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>SHA-256 of the request arguments. A retry that changed the amount is a different
        /// request wearing the same name, and is refused rather than silently answered with the
        /// earlier result.</summary>
        public string Fingerprint { get; set; } = string.Empty;

        public int? UserId { get; set; }

        public bool IsCompleted { get; set; }

        /// <summary>The HTTP status and body the original request returned, replayed verbatim.</summary>
        public int? StatusCode { get; set; }

        public string? ResponseBody { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }
    }
}
