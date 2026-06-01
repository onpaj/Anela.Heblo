using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Anela.Heblo.API.Telemetry;

/// <summary>
/// Telemetry processor that filters out unnecessary data to reduce Application Insights costs.
/// </summary>
public class CostOptimizedTelemetryProcessor : ITelemetryProcessor
{
    private readonly ITelemetryProcessor _next;
    private readonly HashSet<string> _excludedPaths = new()
    {
        "/health",
        "/healthz",
        "/ready",
        "/live",
        "/api/health",
        "/api/diagnostics",
        "/swagger",
        "/favicon.ico",
        "/robots.txt",
        "/sitemap.xml"
    };

    private readonly HashSet<string> _excludedExtensions = new()
    {
        ".js", ".css", ".map", ".jpg", ".jpeg", ".png", ".gif", ".svg",
        ".ico", ".woff", ".woff2", ".ttf", ".eot"
    };

    public CostOptimizedTelemetryProcessor(ITelemetryProcessor next)
    {
        _next = next;
    }

    public void Process(ITelemetry item)
    {
        // Filter out static file requests
        if (item is RequestTelemetry request)
        {
            // Skip health checks and monitoring endpoints
            if (_excludedPaths.Any(path => request.Url?.AbsolutePath?.StartsWith(path, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return;
            }

            // Skip static files
            if (_excludedExtensions.Any(ext => request.Url?.AbsolutePath?.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return;
            }

            // Skip successful OPTIONS requests (CORS preflight)
            if (request.ResponseCode == "200" && request.Name?.Contains("OPTIONS") == true)
            {
                return;
            }

            // Skip very fast requests (< 10ms) unless they failed
            if (request.Duration < TimeSpan.FromMilliseconds(10) && request.Success == true)
            {
                return;
            }
        }

        // Filter out fast dependency calls (< 50ms) unless they failed
        if (item is DependencyTelemetry dependency)
        {
            // Always track failed dependencies
            if (dependency.Success == false)
            {
                _next.Process(item);
                return;
            }

            // Skip very fast DB calls
            if (dependency.Type == "SQL" && dependency.Duration < TimeSpan.FromMilliseconds(50))
            {
                return;
            }

            // Skip fast HTTP calls
            if (dependency.Type == "Http" && dependency.Duration < TimeSpan.FromMilliseconds(100))
            {
                return;
            }
        }

        // Filter out verbose trace messages in production
        if (item is TraceTelemetry trace)
        {
            // Only keep Warning and above in production
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            if (environment == "Production" &&
                (trace.SeverityLevel == SeverityLevel.Verbose ||
                 trace.SeverityLevel == SeverityLevel.Information))
            {
                return;
            }
        }

        // Process all other telemetry
        _next.Process(item);
    }
}