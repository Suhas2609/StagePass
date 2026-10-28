using System.ComponentModel.DataAnnotations;

namespace StagePass.Api.DTOs.Organizers;

// Available is not part of this DTO; the controller sets Available = Capacity.
public class CreateTicketTypeRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int Capacity { get; set; }
}
