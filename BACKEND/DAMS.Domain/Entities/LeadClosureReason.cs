using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>Configurable reason a lead was marked Lost or Dormant.</summary>
    public class LeadClosureReason
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public LeadClosureReasonKind Kind { get; set; } = LeadClosureReasonKind.Both;

        public bool IsActive { get; set; } = true;

        public bool IsSystem { get; set; }

        public int DisplayOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
