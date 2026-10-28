namespace StagePass.Api.DTOs.Performances;

public class PerformanceListItemDto
{
    public int PerformanceId { get; set; }
    public string EventTitle { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public DateTime StartDateTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal LowestPrice { get; set; }
    public int TotalAvailable { get; set; }
}
