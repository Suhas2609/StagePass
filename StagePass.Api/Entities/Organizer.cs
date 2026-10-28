namespace StagePass.Api.Entities;

public class Organizer
{
    public int OrganizerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public ICollection<Event> Events { get; set; } = new List<Event>();
}
