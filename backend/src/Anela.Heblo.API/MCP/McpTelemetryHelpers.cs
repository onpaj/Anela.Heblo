namespace Anela.Heblo.API.MCP;

/// <summary>
/// Small helpers shared across MCP middleware for consistent telemetry values.
/// </summary>
internal static class McpTelemetryHelpers
{
    /// <summary>
    /// Returns the first 8 characters of <paramref name="value"/> if non-empty,
    /// or "(missing)" if null/empty. Never returns more than 8 characters.
    /// A header present but empty is treated as absent per FR-1.
    /// </summary>
    public static string TruncateSessionId(string? value)
        => string.IsNullOrEmpty(value)
            ? "(missing)"
            : value.Length <= 8 ? value : value[..8];
}
