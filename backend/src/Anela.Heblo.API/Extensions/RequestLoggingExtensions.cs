using Anela.Heblo.API.Middleware;

namespace Anela.Heblo.API.Extensions;

/// <summary>
/// Extension methods for registering request logging middleware
/// </summary>
public static class RequestLoggingExtensions
{
    /// <summary>
    /// Adds request/response logging middleware to the pipeline.
    /// Logs detailed information for configured endpoints (e.g., /api/bank-statements/import)
    /// to help diagnose production issues like Content-Type mismatches, serialization errors, etc.
    /// </summary>
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestLoggingMiddleware>();
    }
}
