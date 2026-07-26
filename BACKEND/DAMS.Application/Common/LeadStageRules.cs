using DAMS.Domain.Enums;

namespace DAMS.Application.Common
{
    /// <summary>
    /// The pipeline's movement rules in one place. Closure (Lost/Dormant) and reopening go
    /// through their own operations because they carry mandatory extra information, so they
    /// are not part of the plain transition map.
    /// </summary>
    public static class LeadStageRules
    {
        /// <summary>
        /// Terminal stages. Exposed as an array because this is used inside EF queries,
        /// where a plain array translates to a SQL IN list.
        /// </summary>
        public static readonly LeadStage[] ClosedStages =
            { LeadStage.Won, LeadStage.Lost, LeadStage.Dormant };

        public static readonly IReadOnlySet<LeadStage> ReopenableStages =
            new HashSet<LeadStage> { LeadStage.Lost, LeadStage.Dormant };

        /// <summary>Stages a lead may be reopened into.</summary>
        public static readonly IReadOnlySet<LeadStage> ReopenTargets =
            new HashSet<LeadStage>
            {
                LeadStage.New,
                LeadStage.FirstContactPending,
                LeadStage.Contacted,
                LeadStage.Qualified,
                LeadStage.Negotiation
            };

        private static readonly IReadOnlyDictionary<LeadStage, IReadOnlySet<LeadStage>> Allowed =
            new Dictionary<LeadStage, IReadOnlySet<LeadStage>>
            {
                [LeadStage.New] = Set(LeadStage.FirstContactPending, LeadStage.Contacted),
                [LeadStage.FirstContactPending] = Set(LeadStage.Contacted),
                [LeadStage.Contacted] = Set(
                    LeadStage.Qualified, LeadStage.SiteVisitScheduled, LeadStage.Negotiation),
                [LeadStage.Qualified] = Set(
                    LeadStage.SiteVisitScheduled, LeadStage.Negotiation,
                    LeadStage.DocumentsInProgress, LeadStage.BookingPending),
                [LeadStage.SiteVisitScheduled] = Set(
                    LeadStage.SiteVisitCompleted, LeadStage.Qualified, LeadStage.Contacted),
                [LeadStage.SiteVisitCompleted] = Set(
                    LeadStage.Qualified, LeadStage.SiteVisitScheduled, LeadStage.Negotiation,
                    LeadStage.DocumentsInProgress, LeadStage.BookingPending),
                [LeadStage.Negotiation] = Set(
                    LeadStage.DocumentsInProgress, LeadStage.BookingPending, LeadStage.SiteVisitScheduled),
                [LeadStage.DocumentsInProgress] = Set(
                    LeadStage.BookingPending, LeadStage.Negotiation),
                [LeadStage.BookingPending] = Set(
                    LeadStage.DocumentsInProgress, LeadStage.Negotiation),
                // Terminal. Won never moves; Lost/Dormant move only through Reopen.
                [LeadStage.Won] = Set(),
                [LeadStage.Lost] = Set(),
                [LeadStage.Dormant] = Set()
            };

        public static bool IsClosed(LeadStage stage) => Array.IndexOf(ClosedStages, stage) >= 0;

        /// <summary>
        /// Validates a plain stage change. Throws with an explanation rather than returning
        /// false so every rejection reaches the caller as an actionable message.
        /// </summary>
        public static void EnsureCanTransition(LeadStage from, LeadStage to)
        {
            if (from == to)
                throw new InvalidOperationException($"The lead is already at stage {to}.");

            if (to == LeadStage.Won)
                throw new InvalidOperationException(
                    "A lead becomes Won only by converting it into a customer and booking.");

            if (to is LeadStage.Lost or LeadStage.Dormant)
                throw new InvalidOperationException(
                    $"Use the close operation to mark a lead {to}; a reason is required.");

            if (from == LeadStage.Won)
                throw new InvalidOperationException("A converted lead cannot move to another stage.");

            if (from is LeadStage.Lost or LeadStage.Dormant)
                throw new InvalidOperationException(
                    $"This lead is {from}. Reopen it before changing its stage.");

            if (!Allowed.TryGetValue(from, out var targets) || !targets.Contains(to))
                throw new InvalidOperationException($"A lead cannot move from {from} to {to}.");
        }

        /// <summary>
        /// Any live lead can be converted — a walk-in who decides on the spot is a real
        /// case, and the website approval path converts straight from New. Only a lead that
        /// is already closed is refused.
        /// </summary>
        public static void EnsureCanConvert(LeadStage stage)
        {
            if (stage == LeadStage.Won)
                throw new InvalidOperationException("This lead has already been converted.");

            if (IsClosed(stage))
                throw new InvalidOperationException($"This lead is {stage}. Reopen it before converting.");
        }

        private static IReadOnlySet<LeadStage> Set(params LeadStage[] stages) => new HashSet<LeadStage>(stages);
    }
}
