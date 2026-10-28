namespace StagePass.Api.DTOs.Reservations;

public class ReservationResponse
{
    public int HoldId { get; set; }
    public int TicketTypeId { get; set; }
    public string TicketTypeName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
