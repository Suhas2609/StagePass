using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StagePass.Api.Entities;

namespace StagePass.Api.Security;

/// <summary>Requires a valid session of a user with IsOrganizer (401 AuthRequired / 403 OrganizerRequired).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireOrganizerAttribute : TypeFilterAttribute
{
    public RequireOrganizerAttribute() : base(typeof(RequireOrganizerFilter)) { }
}

public sealed class RequireOrganizerFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext ctx)
    {
        var user = ctx.HttpContext.Items["CurrentUser"] as AppUser;
        if (user is null)
            ctx.Result = new UnauthorizedObjectResult(new { error = "AuthRequired" });
        else if (!user.IsOrganizer)
            ctx.Result = new ObjectResult(new { error = "OrganizerRequired" }) { StatusCode = 403 };
    }

    public void OnActionExecuted(ActionExecutedContext ctx) { }
}
