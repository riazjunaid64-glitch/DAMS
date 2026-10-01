using System.ComponentModel.DataAnnotations;

namespace DAMS.Application.DTOs.CustomerDtos
{
    public class BlockCustomerDto
    {
        [Required(ErrorMessage = "Enter the reason for blocking this customer.")]
        [StringLength(500)]
        public string Reason { get; set; } = string.Empty;
    }
}
