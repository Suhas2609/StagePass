using StagePass.Api.Enums;

namespace StagePass.Api.Entities;

public class ReservationHold
{
    public int HoldId { get; set; }
    public Guid IdempotencyKey { get; set; }
    public string CustomerIdentifier { get; set; } = string.Empty;
    public int TicketTypeId { get; set; }
    public int Quantity { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public TicketType TicketType { get; set; } = null!;
    public Booking? Booking { get; set; }
}
