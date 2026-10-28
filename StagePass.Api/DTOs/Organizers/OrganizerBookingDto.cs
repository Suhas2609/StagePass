namespace StagePass.Api.DTOs.Organizers;

public class OrganizerBookingDto
{
    public string BookingReference { get; set; } = string.Empty;
    public string CustomerIdentifier { get; set; } = string.Empty;
    public string TicketTypeName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime BookingCreatedAt { get; set; }
}
