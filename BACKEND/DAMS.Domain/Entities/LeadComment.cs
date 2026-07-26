namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Internal note or comment between staff. Never exposed on any customer-facing
    /// endpoint — a lead has no customer-visible message concept.
    /// </summary>
    public class LeadComment
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public int? ParentCommentId { get; set; }

        public string Body { get; set; } = string.Empty;

        // A comment flagged for manager review shows up on the manager dashboard.
        public bool IsManagerReviewRequest { get; set; }

        public bool IsDecisionRecord { get; set; }

        public int AuthorUserId { get; set; }

        public string? AuthorName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Lead Lead { get; set; } = null!;

        public LeadComment? ParentComment { get; set; }

        public ICollection<LeadCommentMention> Mentions { get; set; } = new List<LeadCommentMention>();
    }
}
