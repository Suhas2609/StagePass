using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace StagePass.Api.Filters;

/// <summary>
/// DEMONSTRATION MECHANISM ONLY - NOT PRODUCTION AUTHENTICATION.
/// Requires the request header "X-Api-Key" to equal the configured "OrganizerApiKey" value.
/// Limitations: one shared static secret for all organizers (no identity, per-user authorization,
/// rotation, expiry or audit trail); the key travels in a plain header and is only as safe as the
/// transport (HTTPS) and the configuration source; any caller holding the key can act for ANY organizer.
/// Replace with real authentication/authorization (e.g. JWT/OIDC with organizer ownership checks)
/// before any real-world use.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class OrganizerApiKeyAttribute : Attribute, IActionFilter
{
    public const string HeaderName = "X-Api-Key";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var configuredKey = context.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["OrganizerApiKey"];

        if (string.IsNullOrWhiteSpace(configuredKey) ||
            string.Equals(configuredKey, "REPLACE_FOR_DEMO", StringComparison.OrdinalIgnoreCase) ||
            !context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided) ||
            !KeysMatch(provided.ToString(), configuredKey))
        {
            context.Result = new UnauthorizedResult();
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    // Constant-time comparison to avoid leaking key contents through timing.
    private static bool KeysMatch(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
