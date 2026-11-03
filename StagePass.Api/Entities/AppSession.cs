namespace StagePass.Api.Entities;

/// <summary>Database-backed opaque session token (the GUID <see cref="SessionId"/> is the bearer token).</summary>
public sealed class AppSession
{
    public Guid SessionId { get; init; }
    public int UserId { get; init; }
    public AppUser User { get; init; } = null!;
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; set; }
}
