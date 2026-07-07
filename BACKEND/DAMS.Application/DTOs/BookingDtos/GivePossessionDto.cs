namespace DAMS.Application.DTOs.BookingDtos
{
    public class GivePossessionDto
    {
        /// <summary>Defaults to now when omitted.</summary>
        public DateTime? PossessionDate { get; set; }
    }
}
