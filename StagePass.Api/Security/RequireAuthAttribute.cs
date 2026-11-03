using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StagePass.Api.Entities;

namespace StagePass.Api.Security;

/// <summary>Requires a valid session (401 AuthRequired otherwise).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAuthAttribute : TypeFilterAttribute
{
    public RequireAuthAttribute() : base(typeof(RequireAuthFilter)) { }
}

public sealed class RequireAuthFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext ctx)
    {
        if (ctx.HttpContext.Items["CurrentUser"] is not AppUser)
            ctx.Result = new UnauthorizedObjectResult(new { error = "AuthRequired" });
    }

    public void OnActionExecuted(ActionExecutedContext ctx) { }
}
