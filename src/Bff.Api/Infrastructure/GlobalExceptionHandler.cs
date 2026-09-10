using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace Bff.Api.Infrastructure;

// IExceptionHandler -> .NET 8 API
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails, 
    ILogger<GlobalExceptionHandler>log) : IExceptionHandler
    
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The caller went away — nothing to write, and it is not an error.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            return true;

        log.LogError(exception, "Unhandled exception on {Method} {Path}", 
            httpContext.Request.Method, 
            httpContext.Request.Path);

        httpContext.Response.StatusCode = exception switch
        {
            BadHttpRequestException => StatusCodes.Status400BadRequest,
            _                       => StatusCodes.Status500InternalServerError
        };

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Title  = "An unexpected error occurred.",
                Status = httpContext.Response.StatusCode,
                // Correlate a support ticket with a log line — never leak the stack trace.
                Extensions = { ["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier }
            }
        });
    }
}