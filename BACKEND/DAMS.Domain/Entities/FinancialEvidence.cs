namespace DAMS.Domain.Entities
{
    public class FinancialEvidence
    {
        public int Id { get; set; }
        public int? CommissionId { get; set; }
        public int? PayoutId { get; set; }
        public int? RebateId { get; set; }
        public int? RebateDisbursementId { get; set; }
        public string StoredFileName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int? UploadedByUserId { get; set; }
        public string? UploadedByName { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public BookingCommission? Commission { get; set; }
        public CommissionPayout? Payout { get; set; }
        public CustomerRebate? Rebate { get; set; }
        public RebateDisbursement? RebateDisbursement { get; set; }
    }
}
