using DroneControl.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DroneControl.Api.Errors;

/// <summary>
/// Maps the Application layer's execution-failure exceptions to consistent
/// ProblemDetails responses (specs/backend/error-handling.md): a command
/// while disconnected is a state conflict (409), and an unavailable drone
/// implementation is a dependency failure (503, logged — operators need to
/// see it). Everything else returns false and falls through to the default
/// problem pipeline: a logged, generic 500 that never leaks exception
/// details into the response. Controllers stay thin and catch nothing.
/// </summary>
public sealed class DroneExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DroneExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DroneNotConnectedException and not DroneUnavailableException)
        {
            return new ValueTask<bool>(false);
        }

        var unavailable = exception is DroneUnavailableException;

        if (unavailable)
        {
            // Availability incidents are operator-relevant; the 409 is routine
            // client-visible state and stays unlogged.
            logger.LogWarning(
                exception,
                "Drone implementation unavailable while handling {Path}.",
                httpContext.Request.Path);
        }

        // The middleware pre-selects 500 before invoking handlers; the mapped
        // status must be written explicitly before the problem body is sent.
        httpContext.Response.StatusCode = unavailable
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status409Conflict;

        return problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = httpContext.Response.StatusCode,
                Title = unavailable
                    ? "The drone implementation is unavailable"
                    : "The drone is not connected",
                Detail = exception.Message,
                Instance = httpContext.Request.Path,
            },
        });
    }
}
