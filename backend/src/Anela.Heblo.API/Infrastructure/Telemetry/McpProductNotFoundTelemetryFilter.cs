using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Anela.Heblo.API.Telemetry;

/// <summary>
/// Converts ExceptionTelemetry for McpException[ProductNotFound] from error-level to a
/// Warning trace, removing it from the exception stream. A product-not-found miss is an
/// expected MCP protocol response (analogous to a cache miss), not an application error.
///
/// Mirrors the established AzureBlobConflictTelemetryFilter pattern.
/// </summary>
public sealed class McpProductNotFoundTelemetryFilter : ITelemetryProcessor
{
    private const string McpExceptionTypeName = "ModelContextProtocol.McpException";
    private const string ProductNotFoundMarker = "[ProductNotFound]";

    private readonly ITelemetryProcessor _next;

    public McpProductNotFoundTelemetryFilter(ITelemetryProcessor next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public void Process(ITelemetry item)
    {
        if (item is ExceptionTelemetry exc
            && exc.Message.Contains(ProductNotFoundMarker, StringComparison.Ordinal)
            && IsMcpException(exc))
        {
            var trace = new TraceTelemetry(exc.Message, SeverityLevel.Warning);
            foreach (var prop in exc.Properties)
            {
                trace.Properties[prop.Key] = prop.Value;
            }
            _next.Process(trace);
            return;
        }

        _next.Process(item);
    }

    private static bool IsMcpException(ExceptionTelemetry exc)
    {
        // When live exceptions are tracked (TrackException or ASP.NET middleware), Exception is set.
        if (exc.Exception != null)
            return string.Equals(exc.Exception.GetType().FullName, McpExceptionTypeName, StringComparison.Ordinal);

        // Fallback for telemetry constructed without a live exception object.
        return exc.ExceptionDetailsInfoList.Count > 0
            && string.Equals(exc.ExceptionDetailsInfoList[0].TypeName, McpExceptionTypeName, StringComparison.Ordinal);
    }
}
