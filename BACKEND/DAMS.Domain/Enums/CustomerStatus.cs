namespace DAMS.Domain.Enums
{
    public enum CustomerStatus
    {
        Active = 0,
        // 1 was Inactive, which meant nothing anywhere; those customers are Active now.
        Blocked = 2
    }
}
