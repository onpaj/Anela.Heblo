namespace Anela.Heblo.API.MCP;

/// <summary>
/// Middleware that logs structured diagnostics for MCP endpoint 404 responses.
/// Captures session ID, User-Agent, and Accept header to aid investigation of
/// client session-resumption failures and probe traffic spikes (issue #599).
/// </summary>
public class McpDiagnosticsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<McpDiagnosticsMiddleware> _logger;

    public McpDiagnosticsMiddleware(RequestDelegate next, ILogger<McpDiagnosticsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        if (!IsMcpGetRequest(context) || context.Response.StatusCode != 404)
            return;

        var sessionId = context.Request.Query["sessionId"].FirstOrDefault();
        var userAgent = context.Request.Headers.UserAgent.FirstOrDefault() ?? "unknown";
        var acceptHeader = context.Request.Headers.Accept.ToString();
        var path = context.Request.Path.Value;

        if (sessionId is not null)
        {
            _logger.LogWarning(
                "MCP session resumption failed (404) — session not found on server. " +
                "SessionIdPrefix: {SessionIdPrefix}, UserAgent: {UserAgent}, Accept: {Accept}",
                TruncateId(sessionId),
                userAgent,
                acceptHeader);
        }
        else
        {
            _logger.LogWarning(
                "MCP GET returned 404. Path: {Path}, UserAgent: {UserAgent}, Accept: {Accept}",
                path,
                userAgent,
                acceptHeader);
        }
    }

    private static bool IsMcpGetRequest(HttpContext context)
        => context.Request.Method == HttpMethods.Get
           && context.Request.Path.StartsWithSegments("/mcp");

    internal static string TruncateId(string id)
        => id.Length > 8 ? id[..8] + "***" : "***";
}
