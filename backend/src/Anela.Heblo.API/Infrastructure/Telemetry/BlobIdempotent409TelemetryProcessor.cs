using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Anela.Heblo.API.Telemetry;

/// <summary>
/// Defensive backstop that neutralises App Insights 409 noise from
/// <c>BlobContainerClient.CreateIfNotExistsAsync</c> / <c>CreateAsync</c> calls (PUT container).
/// Genuine blob-level 409s (lease collisions, conditional PUTs) remain visible.
/// </summary>
/// <remarks>
/// Matches <c>Data</c> shapes ending with the container name and no further path segments,
/// e.g. <c>https://stheblo.blob.core.windows.net/photobank</c>. The processor does NOT
/// suppress 409s on individual blob URIs such as <c>.../photobank/thumbnail/abc.jpg</c>.
/// </remarks>
public class BlobIdempotent409TelemetryProcessor : ITelemetryProcessor
{
    private readonly ITelemetryProcessor _next;

    public BlobIdempotent409TelemetryProcessor(ITelemetryProcessor next)
    {
        _next = next;
    }

    public void Process(ITelemetry item)
    {
        if (item is DependencyTelemetry dependency &&
            dependency.Type == "Azure blob" &&
            dependency.ResultCode == "409" &&
            IsPutContainerShape(dependency.Data))
        {
            dependency.Success = true;
        }

        _next.Process(item);
    }

    private static bool IsPutContainerShape(string? data)
    {
        if (string.IsNullOrEmpty(data) || !Uri.TryCreate(data, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // PUT container: URI path is exactly "/{container}" (one segment, no trailing blob path).
        var trimmed = uri.AbsolutePath.Trim('/');
        return !string.IsNullOrEmpty(trimmed) && !trimmed.Contains('/');
    }
}
