using System.ComponentModel.DataAnnotations;

namespace StagePass.Api.DTOs.Organizers;

public class CreateEventRequest
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required]
    public int VenueId { get; set; }

    [Required]
    public int OrganizerId { get; set; }
}
