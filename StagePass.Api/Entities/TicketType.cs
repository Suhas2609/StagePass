namespace StagePass.Api.Entities;

public class TicketType
{
    public int TicketTypeId { get; set; }
    public int PerformanceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Capacity { get; set; }
    public int Available { get; set; }

    public Performance Performance { get; set; } = null!;
    public ICollection<ReservationHold> ReservationHolds { get; set; } = new List<ReservationHold>();
}
