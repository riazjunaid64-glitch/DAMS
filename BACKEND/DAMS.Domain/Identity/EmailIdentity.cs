namespace DAMS.Domain.Identity
{
    /// <summary>
    /// The one place DAMS decides when two email addresses are the same login identity.
    ///
    /// <para>
    /// Every comparison that answers "does this account already exist?", "which account is
    /// signing in?" or "is this address taken?" goes through <see cref="Normalize"/>, and the
    /// result is stored in <c>Users.NormalizedEmail</c> behind a unique index. That index — not
    /// a read-then-write in application code — is what makes the invariant hold when two
    /// registrations arrive at the same instant.
    /// </para>
    ///
    /// <para>
    /// The policy is deliberately minimal: trim surrounding whitespace, and fold case. Nothing
    /// else. DAMS does not strip <c>+tags</c>, does not remove dots and does not know which
    /// provider is on the other side — those transformations are provider-specific folklore, and
    /// applying them would merge two addresses that a mail server treats as two different people.
    /// </para>
    ///
    /// <para>
    /// Case is folded with <see cref="string.ToUpperInvariant"/> rather than lowercasing: upper
    /// invariant is the round-trip-safe direction for case-insensitive comparison, and it is what
    /// ASP.NET Identity's own normalized columns use. The address the person typed is kept
    /// separately in <c>Users.Email</c> for display and delivery — normalization decides identity,
    /// never what gets shown or mailed.
    /// </para>
    /// </summary>
    public static class EmailIdentity
    {
        /// <summary>
        /// The canonical comparison key for a login email, or null when there is no address at
        /// all. Null and blank collapse to the same answer so a caller cannot accidentally index
        /// on an empty string.
        /// </summary>
        public static string? Normalize(string? email) =>
            string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();

        /// <summary>
        /// Whether two addresses are the same login identity under DAMS's policy. Blank is never
        /// equal to anything, including another blank — an account without an address does not
        /// match one.
        /// </summary>
        public static bool AreSame(string? left, string? right)
        {
            var a = Normalize(left);
            return a != null && a == Normalize(right);
        }
    }
}
