namespace StagePass.Api.DTOs.Performances;

public class PerformanceDetailsDto
{
    public int PerformanceId { get; set; }
    public string EventTitle { get; set; } = string.Empty;
    public string? EventDescription { get; set; }
    public string Category { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
    public string VenueAddress { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public DateTime StartDateTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<TicketTypeDto> TicketTypes { get; set; } = new();
}
