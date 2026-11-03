namespace StagePass.Api.Entities;

/// <summary>
/// A registered account. Linked to the legacy <see cref="Organizer"/> table by email only (MVP limitation:
/// a full system would have a single user model).
/// </summary>
public sealed class AppUser
{
    public int UserId { get; init; }
    public required string Email { get; set; }
    public required string Name { get; set; }
    public required string PasswordHash { get; set; }
    public required string PasswordSalt { get; set; }
    public bool IsOrganizer { get; set; }
    public DateTime CreatedAt { get; init; }
    public ICollection<AppSession> Sessions { get; } = [];
}
