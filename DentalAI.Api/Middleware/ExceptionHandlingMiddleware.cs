using DentalAI.Api.Models;

namespace DentalAI.Api.Middleware;

/// <summary>
/// Turns unhandled exceptions into the standard error envelope. Outside Development the response never contains
/// exception details (they can reveal SQL, file paths or data); it carries a reference ID instead, which is also
/// logged with the full exception so support can find it.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
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
            // The message is ours ("... is busy. Please try again."), so it is safe to show.
            logger.LogWarning("Lock timeout for {Method} {Path}: {Message}", context.Request.Method, context.Request.Path, exception.Message);
            await WriteAsync(context, StatusCodes.Status409Conflict, exception.Message, errors: null);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing to send.
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var reference = context.TraceIdentifier;
            logger.LogError(exception, "Unhandled exception {Reference} for {Method} {Path}",
                reference, context.Request.Method, context.Request.Path);

            var errors = environment.IsDevelopment() ? new[] { exception.Message } : null;
            await WriteAsync(context, StatusCodes.Status500InternalServerError,
                $"Something went wrong on our side. If it keeps happening, contact support with reference {reference}.", errors);
        }
    }

    private static Task WriteAsync(HttpContext context, int status, string message, string[]? errors)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new ApiResponse { Success = false, Message = message, Errors = errors ?? [] });
    }
}
