using Microsoft.EntityFrameworkCore;
using StagePass.Api.Data;

namespace StagePass.Api.Security;

/// <summary>
/// Resolves "Authorization: Bearer &lt;session-guid&gt;" to an <see cref="Entities.AppUser"/> and stores it in
/// HttpContext.Items["CurrentUser"]. It never rejects a request itself; authorization is done by the
/// [RequireAuth] / [RequireOrganizer] filters. Only requests carrying a Bearer header hit the database.
/// </summary>
public sealed class SessionAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, StagePassDbContext db)
    {
        var header = context.Request.Headers.Authorization.FirstOrDefault();
        if (header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            var raw = header["Bearer ".Length..].Trim();
            if (Guid.TryParse(raw, out var sessionId))
            {
                var session = await db.AppSessions
                    .AsNoTracking()
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s =>
                        s.SessionId == sessionId &&
                        s.ExpiresAt > DateTime.UtcNow,
                        context.RequestAborted);

                if (session is not null)
                {
                    context.Items["CurrentUser"] = session.User;
                    context.Items["CurrentSession"] = session.SessionId;
                }
            }
        }
        await next(context);
    }
}
