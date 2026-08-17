namespace DAMS.Application.Interfaces
{
    /// <summary>
    /// Encrypts provider credentials before they touch the database and decrypts them on the
    /// way back out. The only component in DAMS that ever sees a token in plain text is the
    /// Graph client, and only for the duration of one call.
    /// </summary>
    public interface IIntegrationSecretProtector
    {
        string Protect(string plaintext);

        /// <summary>
        /// Returns null rather than throwing when a value cannot be decrypted — which in
        /// practice means the data-protection key ring was lost or rotated away.
        ///
        /// Callers treat null exactly like a rejected token: the connection is marked as
        /// needing reauthorization and an admin reconnects. Throwing instead would turn a
        /// recoverable operational mistake into a crash loop in the background worker.
        /// </summary>
        string? TryUnprotect(string? protectedValue);
    }
}
