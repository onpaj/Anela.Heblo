using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.API.Infrastructure.ExceptionHandling;

/// <summary>
/// Maps <see cref="ArgumentException"/> (and subclasses) to a 400 Bad Request ProblemDetails response.
/// The exception message is included in the <c>detail</c> field because argument exceptions represent
/// user-facing validation failures (bad input), not internal system errors.
/// </summary>
public sealed class ArgumentExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ArgumentExceptionHandler> _logger;

    public ArgumentExceptionHandler(ILogger<ArgumentExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Exclude ArgumentNullException — its message reveals internal parameter names
        // (e.g. "Value cannot be null. (Parameter 'mediator')") and should not be
        // surfaced to API clients. Let it fall through to the default 500 handler.
        if (exception is not ArgumentException argumentException || exception is ArgumentNullException)
        {
            return false;
        }

        _logger.LogWarning(argumentException, "Invalid argument: {Message}", argumentException.Message);

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Bad Request",
            Detail = argumentException.Message,
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
        }, cancellationToken);

        return true;
    }
}
