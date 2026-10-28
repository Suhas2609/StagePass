namespace StagePass.Api.DTOs.Bookings;

public class BookingDetailsDto
{
    public string BookingReference { get; set; } = string.Empty;
    public string EventTitle { get; set; } = string.Empty;
    public DateTime PerformanceStartDateTime { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string TicketTypeName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
