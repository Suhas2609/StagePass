namespace StagePass.Api.DTOs.Performances;

// Capacity is intentionally not exposed.
public class TicketTypeDto
{
    public int TicketTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Available { get; set; }
}
