using System.Diagnostics;
using System.Text;

namespace Anela.Heblo.API.Middleware;

/// <summary>
/// Middleware for logging HTTP request/response details for diagnostics.
/// Critical for debugging production issues like Content-Type mismatches, serialization errors, etc.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    // Endpoints to log in detail (e.g., problematic endpoints)
    private static readonly HashSet<string> DetailedLoggingPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/bank-statements/import",
        // Add other endpoints that need detailed logging
    };

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only log API requests (skip static files, health checks, etc.)
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var requestPath = context.Request.Path.Value ?? string.Empty;
        var shouldLogDetails = DetailedLoggingPaths.Contains(requestPath);

        // Start timing
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Log incoming request
            await LogRequestAsync(context, shouldLogDetails);

            // Buffer the response for logging
            var originalResponseBody = context.Response.Body;
            await using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;

            try
            {
                // Call next middleware
                await _next(context);

                // Log response
                await LogResponseAsync(context, stopwatch, shouldLogDetails);

                // Copy buffered response to original stream
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                await responseBodyStream.CopyToAsync(originalResponseBody);
            }
            finally
            {
                context.Response.Body = originalResponseBody;
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Request FAILED - {Method} {Path} - Error after {Duration}ms: {ErrorMessage}",
                context.Request.Method,
                requestPath,
                stopwatch.ElapsedMilliseconds,
                ex.Message
            );
            throw;
        }
    }

    private async Task LogRequestAsync(HttpContext context, bool logDetails)
    {
        var request = context.Request;

        // Basic request info (always logged)
        _logger.LogInformation(
            "Request START - {Method} {Path}{QueryString} - ContentType: {ContentType}, ContentLength: {ContentLength}, UserAgent: {UserAgent}",
            request.Method,
            request.Path,
            request.QueryString,
            request.ContentType ?? "null",
            request.ContentLength?.ToString() ?? "null",
            request.Headers.UserAgent.FirstOrDefault() ?? "null"
        );

        // Detailed logging for specific endpoints
        if (logDetails)
        {
            // Log all headers (exclude sensitive ones)
            var headers = request.Headers
                .Where(h => !IsSensitiveHeader(h.Key))
                .Select(h => $"{h.Key}: {h.Value}")
                .ToList();

            _logger.LogInformation(
                "Request HEADERS - {Method} {Path} - Headers: {Headers}",
                request.Method,
                request.Path,
                string.Join(", ", headers)
            );

            // Log request body for POST/PUT/PATCH (if reasonable size)
            if (request.ContentLength > 0 && request.ContentLength < 10_000) // Max 10KB
            {
                request.EnableBuffering(); // Allow multiple reads

                try
                {
                    var bodySnapshot = await ReadRequestBodyAsync(request);
                    _logger.LogInformation(
                        "Request BODY - {Method} {Path} - Body: {Body}",
                        request.Method,
                        request.Path,
                        bodySnapshot
                    );

                    // Reset stream position for next middleware
                    request.Body.Position = 0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to read request body for logging - {Method} {Path}",
                        request.Method,
                        request.Path
                    );
                }
            }
            else if (request.ContentLength >= 10_000)
            {
                _logger.LogInformation(
                    "Request BODY - {Method} {Path} - Body too large to log ({SizeKB}KB)",
                    request.Method,
                    request.Path,
                    request.ContentLength / 1024
                );
            }
        }
    }

    private async Task LogResponseAsync(HttpContext context, Stopwatch stopwatch, bool logDetails)
    {
        stopwatch.Stop();
        var response = context.Response;
        var request = context.Request;

        // Basic response info (always logged)
        _logger.LogInformation(
            "Request COMPLETED - {Method} {Path} - Status: {StatusCode}, Duration: {Duration}ms",
            request.Method,
            request.Path,
            response.StatusCode,
            stopwatch.ElapsedMilliseconds
        );

        // Detailed logging for errors or specific endpoints
        if (logDetails || response.StatusCode >= 400)
        {
            // Log response body for errors or detailed endpoints
            response.Body.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(response.Body).ReadToEndAsync();
            response.Body.Seek(0, SeekOrigin.Begin);

            var bodyToLog = responseBody.Length > 1000
                ? responseBody.Substring(0, 1000) + "... (truncated)"
                : responseBody;

            if (response.StatusCode >= 400)
            {
                _logger.LogWarning(
                    "Response ERROR - {Method} {Path} - Status: {StatusCode}, Body: {Body}",
                    request.Method,
                    request.Path,
                    response.StatusCode,
                    bodyToLog
                );
            }
            else
            {
                _logger.LogInformation(
                    "Response BODY - {Method} {Path} - Status: {StatusCode}, Body: {Body}",
                    request.Method,
                    request.Path,
                    response.StatusCode,
                    bodyToLog
                );
            }
        }
    }

    private async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(
            request.Body,
            encoding: Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        var body = await reader.ReadToEndAsync();

        // Truncate if too long
        return body.Length > 1000
            ? body.Substring(0, 1000) + "... (truncated)"
            : body;
    }

    private bool IsSensitiveHeader(string headerName)
    {
        var sensitiveHeaders = new[]
        {
            "Authorization",
            "Cookie",
            "X-API-Key",
            "X-Auth-Token"
        };

        return sensitiveHeaders.Any(h =>
            h.Equals(headerName, StringComparison.OrdinalIgnoreCase));
    }
}
