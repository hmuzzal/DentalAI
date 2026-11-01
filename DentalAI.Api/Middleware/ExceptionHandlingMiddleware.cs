using DentalAI.Api.Models;

namespace DentalAI.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (TimeoutException exception) when (!context.Response.HasStarted)
        {
            // A record lock (booking, invoice) couldn't be taken in time: someone else is changing it. Retryable.
            logger.LogWarning("Lock timeout for {Method} {Path}: {Message}", context.Request.Method, context.Request.Path, exception.Message);
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new ApiResponse { Success = false, Message = exception.Message });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new ApiResponse
            {
                Success = false,
                Message = "An unexpected error occurred.",
                Errors = [exception.Message]
            });
        }
    }
}
