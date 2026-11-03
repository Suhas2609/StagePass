using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StagePass.Api.Data;
using StagePass.Api.DTOs.Auth;
using StagePass.Api.Entities;
using StagePass.Api.Security;

namespace StagePass.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(StagePassDbContext db, IConfiguration config) : ControllerBase
{
    // Verified against when the email is unknown so response time does not reveal whether an account exists.
    private static readonly Lazy<(string Hash, string Salt)> DummyCredential =
        new(() => PasswordHasher.Hash("not-a-real-password"));

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        if (await db.AppUsers.AnyAsync(u => u.Email == email, ct))
            return Conflict(new { error = "EmailAlreadyRegistered" });

        var (hash, salt) = PasswordHasher.Hash(req.Password);
        var user = new AppUser
        {
            Email = email,
            Name = req.Name.Trim(),
            PasswordHash = hash,
            PasswordSalt = salt,
            IsOrganizer = req.IsOrganizer,
            CreatedAt = DateTime.UtcNow,
        };
        db.AppUsers.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race against a concurrent registration of the same email (unique index IX_AppUsers_Email).
            db.ChangeTracker.Clear();
            if (await db.AppUsers.AsNoTracking().AnyAsync(u => u.Email == email, ct))
                return Conflict(new { error = "EmailAlreadyRegistered" });
            throw;
        }

        return Ok(await CreateSessionAsync(user, ct));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            PasswordHasher.Verify(req.Password, DummyCredential.Value.Hash, DummyCredential.Value.Salt);
            return Unauthorized(new { error = "InvalidCredentials" });
        }

        if (!PasswordHasher.Verify(req.Password, user.PasswordHash, user.PasswordSalt))
            return Unauthorized(new { error = "InvalidCredentials" });

        return Ok(await CreateSessionAsync(user, ct));
    }

    [HttpPost("logout")]
    [RequireAuth]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (HttpContext.Items["CurrentSession"] is Guid sessionId)
        {
            await db.AppSessions.Where(s => s.SessionId == sessionId).ExecuteDeleteAsync(ct);
        }
        return Ok(new { message = "Logged out" });
    }

    [HttpGet("me")]
    [RequireAuth]
    public IActionResult Me()
    {
        var user = (AppUser)HttpContext.Items["CurrentUser"]!;
        return Ok(new
        {
            userId = user.UserId,
            email = user.Email,
            name = user.Name,
            isOrganizer = user.IsOrganizer,
        });
    }

    private async Task<object> CreateSessionAsync(AppUser user, CancellationToken ct)
    {
        var durationDays = config.GetValue("SessionDurationDays", 7);
        var session = new AppSession
        {
            UserId = user.UserId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(durationDays),
        };
        db.AppSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return new
        {
            token = session.SessionId,
            userId = user.UserId,
            name = user.Name,
            email = user.Email,
            isOrganizer = user.IsOrganizer,
            expiresAt = session.ExpiresAt,
        };
    }
}
