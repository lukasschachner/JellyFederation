using JellyFederation.Server.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Filters;

public class ApiKeyAuthFilter(FederationDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue("X-Api-Key", out var key))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == key.ToString());
        if (server is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        context.HttpContext.Items["Server"] = server;
        await next();
    }
}
