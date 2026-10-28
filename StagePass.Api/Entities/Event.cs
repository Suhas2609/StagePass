namespace StagePass.Api.Entities;

public class Event
{
    public int EventId { get; set; }
    public int OrganizerId { get; set; }
    public int VenueId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Organizer Organizer { get; set; } = null!;
    public Venue Venue { get; set; } = null!;
    public ICollection<Performance> Performances { get; set; } = new List<Performance>();
}
