namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A colleague tagged on an internal comment. Being mentioned also grants that user
    /// access to the lead — this is how a lead is explicitly shared with someone who does
    /// not own it.
    /// </summary>
    public class LeadCommentMention
    {
        public int Id { get; set; }

        public int LeadCommentId { get; set; }

        public int MentionedUserId { get; set; }

        public LeadComment LeadComment { get; set; } = null!;

        public User MentionedUser { get; set; } = null!;
    }
}
