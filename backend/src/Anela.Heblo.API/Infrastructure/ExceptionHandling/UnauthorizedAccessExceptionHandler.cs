using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.API.Infrastructure.ExceptionHandling;

/// <summary>
/// Maps <see cref="UnauthorizedAccessException"/> to a 401 ProblemDetails response.
/// Body intentionally omits `detail`: the exception message is logged server-side only,
/// never returned to the client. This is the infrastructure-layer 401; business-layer
/// 401s flow through BaseApiController.HandleResponse and use the BaseResponse shape.
/// </summary>
public sealed class UnauthorizedAccessExceptionHandler : IExceptionHandler
{
    private readonly ILogger<UnauthorizedAccessExceptionHandler> _logger;

    public UnauthorizedAccessExceptionHandler(ILogger<UnauthorizedAccessExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not UnauthorizedAccessException uax)
        {
            return false;
        }

        _logger.LogWarning(uax, "Unauthorized access: {Message}", uax.Message);

        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Type = "https://tools.ietf.org/html/rfc7235#section-3.1"
        }, cancellationToken);

        return true;
    }
}
