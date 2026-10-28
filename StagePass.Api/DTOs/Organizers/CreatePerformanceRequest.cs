using System.ComponentModel.DataAnnotations;

namespace StagePass.Api.DTOs.Organizers;

public class CreatePerformanceRequest
{
    [Required]
    public int EventId { get; set; }

    [Required]
    public DateTime StartDateTime { get; set; }
}
