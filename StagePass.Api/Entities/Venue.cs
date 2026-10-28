namespace StagePass.Api.Entities;

public class Venue
{
    public int VenueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;

    public ICollection<Event> Events { get; set; } = new List<Event>();
}
