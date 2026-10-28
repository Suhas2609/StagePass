using StagePass.Api.Enums;

namespace StagePass.Api.Entities;

public class Performance
{
    public int PerformanceId { get; set; }
    public int EventId { get; set; }
    public DateTime StartDateTime { get; set; }
    public PerformanceStatus Status { get; set; } = PerformanceStatus.Upcoming;

    public Event Event { get; set; } = null!;
    public ICollection<TicketType> TicketTypes { get; set; } = new List<TicketType>();
}
