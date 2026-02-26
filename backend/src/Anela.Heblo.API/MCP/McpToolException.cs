namespace Anela.Heblo.API.MCP;

/// <summary>
/// Exception thrown by MCP tools to signal errors to MCP clients.
/// The MCP framework translates this into proper MCP protocol error responses.
/// </summary>
public class McpToolException : Exception
{
    /// <summary>
    /// Error code (e.g., "NOT_FOUND", "VALIDATION_ERROR")
    /// </summary>
    public string Code { get; }

    public McpToolException(string code, string message) : base(message)
    {
        Code = code;
    }

    public McpToolException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }
}
