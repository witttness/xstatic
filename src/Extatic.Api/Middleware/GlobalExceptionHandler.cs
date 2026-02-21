using Extatic.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Middleware;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        var (statusCode, title, extensions) = exception switch
        {
            NotFoundException => (404, "Not Found", (object?)null),
            ForbiddenException => (403, "Forbidden", (object?)null),
            ConflictException => (409, "Conflict", (object?)null),
            FileTooLargeException => (413, "Payload Too Large", (object?)null),
            ValidationException ve => (422, "Validation Failed",
                new { errors = ve.Errors.Select(e => new { pointer = e.Pointer, message = e.Message }) }),
            _ => (500, "Internal Server Error", (object?)null)
        };

        if (statusCode == 500)
            logger.LogError(exception, "Unhandled exception");

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = context.Request.Path
        };

        if (extensions is not null)
            problem.Extensions["errors"] = extensions;

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}
