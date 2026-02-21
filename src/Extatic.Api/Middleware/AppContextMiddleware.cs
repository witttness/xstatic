using Extatic.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Middleware;

public class AppContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var appSlug = context.GetRouteValue("app_slug")?.ToString();
        if (!string.IsNullOrEmpty(appSlug))
        {
            var app = await db.Apps.FirstOrDefaultAsync(a => a.Slug == appSlug);
            if (app is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { title = "App not found", status = 404 });
                return;
            }
            context.Items["CurrentApp"] = app;
        }

        await next(context);
    }
}
