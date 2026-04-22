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
            var userAgent = context.Request.Headers.UserAgent.FirstOrDefault() ?? "unknown";
            var acceptHeader = context.Request.Headers.Accept.ToString();
            var origin = context.Request.Headers.Origin.FirstOrDefault() ?? "none";

            _logger.LogWarning(
                "MCP GET /mcp returned 400 — MCP protocol validation failed. " +
                "UserAgent: {UserAgent}, Accept: {Accept}, Origin: {Origin}",
                userAgent,
                acceptHeader,
                origin);
        }
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
