using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Anela.Heblo.API.Telemetry;

/// <summary>
/// Drops the benign HTTP 409 Conflict dependency telemetry emitted when the Azure
/// Blob SDK provisions a container that already exists.
///
/// Both blob code paths (<c>AzureBlobStorageService</c> and <c>AzureBlobPrintQueueSink</c>)
/// call <c>CreateIfNotExistsAsync</c> on cold start to guarantee the target container
/// exists. When the container is already present, the storage service answers the
/// underlying <c>PUT container</c> with 409 Conflict; the SDK treats this as an expected
/// no-op and returns a null/false result without throwing. The auto-collected dependency
/// tracking module, however, still records the raw 409 as a <em>failed</em> dependency,
/// which surfaces as a recurring "Azure blob 409 Conflict" reliability signal even though
/// no operation actually failed.
///
/// This codebase performs no leased or conditional blob writes, so a 409 on the
/// "Azure blob" dependency type can only originate from this swallowed container-create
/// conflict. Filtering it removes the false-positive failures without masking any genuine
/// blob error (uploads use unconditional overwrites and surface their own failures).
///
/// Mirrors the established <c>HomeAssistantDependencyTelemetryFilter</c> pattern.
/// </summary>
public sealed class AzureBlobConflictTelemetryFilter : ITelemetryProcessor
{
    private const string AzureBlobDependencyType = "Azure blob";
    private const string ConflictResultCode = "409";

    private readonly ITelemetryProcessor _next;

    public AzureBlobConflictTelemetryFilter(ITelemetryProcessor next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public void Process(ITelemetry item)
    {
        if (item is DependencyTelemetry dep
            && string.Equals(dep.Type, AzureBlobDependencyType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(dep.ResultCode, ConflictResultCode, StringComparison.Ordinal))
        {
            // Swallowed-by-SDK container-create conflict — not an application failure.
            return;
        }

        _next.Process(item);
    }
}
