namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A document the office can ask customers for. The Ask every customer switch is
    /// <see cref="AsksEveryCustomer"/>. A removed document that was already used is
    /// <see cref="IsHidden"/> and stays out of Document setup and Add document.
    /// </summary>
    public class CustomerDocumentCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>When on, every customer who is not blocked has this document as Needed, including customers created from now on.</summary>
        public bool AsksEveryCustomer { get; set; }
        /// <summary>Removed after a customer had it. Hidden from Document setup and from Add document.</summary>
        public bool IsHidden { get; set; }
        /// <summary>The seeded free-text type. It is not a Document setup row; Add document uses a name instead.</summary>
        public bool IsOther { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public byte[] RowVersion { get; set; } = [];

        public ICollection<CustomerDocumentRequirement> Requirements { get; set; } = new List<CustomerDocumentRequirement>();
    }
}
