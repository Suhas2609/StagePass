namespace StagePass.Api.Entities;

public class Booking
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public int HoldId { get; set; }
    public string CustomerIdentifier { get; set; } = string.Empty;
    public string Status { get; set; } = "Confirmed";
    public DateTime CreatedAt { get; set; }

    public ReservationHold ReservationHold { get; set; } = null!;
}
