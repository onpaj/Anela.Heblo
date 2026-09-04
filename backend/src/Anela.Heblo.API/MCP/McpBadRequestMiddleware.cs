using System.Diagnostics;

namespace Anela.Heblo.API.MCP;

/// <summary>
/// Middleware that mitigates scanner/probe traffic on the MCP endpoint and logs
/// structured diagnostics when GET /mcp returns 400.
///
/// Two behaviours:
/// 1. Short-circuit to 404 for GET /mcp requests that lack the required
///    "Accept: text/event-stream" or "Accept: application/json" header.
///    This reduces noise from generic crawlers and avoids leaking that an MCP
///    endpoint exists (issue #593 — suggested action 2).
/// 2. After passing through the pipeline, if a well-formed request still gets
///    a 400, log User-Agent, Accept, and Origin so we can identify the client
///    (issue #593 — suggested action 1).
/// </summary>
public class McpBadRequestMiddleware
{
    // MCP Streamable HTTP transport accepts either SSE or plain JSON responses.
    private static readonly string[] ValidMcpAcceptValues =
    [
        "text/event-stream",
        "application/json",
    ];

    private static readonly EventId GetBadRequestEvent = new(5931, "McpBadRequest");
    private static readonly EventId PostBadRequestEvent = new(5932, "McpBadRequest");

    private readonly RequestDelegate _next;
    private readonly ILogger<McpBadRequestMiddleware> _logger;

    public McpBadRequestMiddleware(RequestDelegate next, ILogger<McpBadRequestMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsMcpGetRequest(context))
        {
            await _next(context);
            return;
        }

        var getStart = Stopwatch.GetTimestamp();

        // Short-circuit probes/scanners that don't send a valid MCP Accept header.
        // Returning 404 instead of 400 avoids advertising that an MCP endpoint exists.
        if (!HasValidMcpAcceptHeader(context))
        {
            var userAgent = context.Request.Headers.UserAgent.FirstOrDefault() ?? "unknown";
            var acceptHeader = context.Request.Headers.Accept.ToString();
            var origin = context.Request.Headers.Origin.FirstOrDefault() ?? "none";

            _logger.LogInformation(
                "MCP probe blocked (missing valid Accept header) — returning 404. " +
                "UserAgent: {UserAgent}, Accept: {Accept}, Origin: {Origin}",
                userAgent,
                acceptHeader,
                origin);

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);

        // Log structured diagnostics for well-formed requests that still get 400.
        if (context.Response.StatusCode == StatusCodes.Status400BadRequest)
        {
            LogBadMcpRequest(context, GetBadRequestEvent, Stopwatch.GetElapsedTime(getStart).TotalMilliseconds);
        }
    }

    private void LogBadMcpRequest(HttpContext ctx, EventId eventId, double elapsedMs)
    {
        var req = ctx.Request;
        var sessionIdRaw = req.Headers["Mcp-Session-Id"].ToString();
        var sessionIdPresent = !string.IsNullOrEmpty(sessionIdRaw);
        var path = (req.Path.Value ?? string.Empty) + (req.QueryString.Value ?? string.Empty);

        _logger.Log(
            LogLevel.Warning,
            eventId,
            "MCP bad request: HTTPMethod={HTTPMethod} Path={Path} StatusCode={StatusCode} UserAgent={UserAgent} Origin={Origin} Accept={Accept} ContentType={ContentType} McpSessionIdPresent={McpSessionIdPresent} McpSessionIdPrefix={McpSessionIdPrefix} RemoteIp={RemoteIp} ElapsedMs={ElapsedMs}",
            req.Method,
            path,
            ctx.Response.StatusCode,
            req.Headers.UserAgent.Count > 0 ? req.Headers.UserAgent.ToString() : "(missing)",
            req.Headers.Origin.Count > 0 ? req.Headers.Origin.ToString() : "(missing)",
            req.Headers.Accept.Count > 0 ? req.Headers.Accept.ToString() : "(missing)",
            req.Headers.ContentType.Count > 0 ? req.Headers.ContentType.ToString() : "(missing)",
            sessionIdPresent,
            McpTelemetryHelpers.TruncateSessionId(sessionIdRaw),
            ctx.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
            elapsedMs);
    }

    private static bool IsMcpGetRequest(HttpContext context)
        => context.Request.Method == HttpMethods.Get
           && context.Request.Path.StartsWithSegments("/mcp");

    internal static bool HasValidMcpAcceptHeader(HttpContext context)
    {
        var acceptHeader = context.Request.Headers.Accept.ToString();
        if (string.IsNullOrWhiteSpace(acceptHeader))
            return false;

        foreach (var valid in ValidMcpAcceptValues)
        {
            if (acceptHeader.Contains(valid, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
