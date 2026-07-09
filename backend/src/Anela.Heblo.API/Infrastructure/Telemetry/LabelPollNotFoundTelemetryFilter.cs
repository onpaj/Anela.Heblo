using Anela.Heblo.API.Controllers;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Anela.Heblo.API.Telemetry;

/// <summary>
/// Drops the benign HTTP 404 request telemetry emitted while the packaging SPA polls
/// <c>GET .../packages/{n}/label.pdf</c> for a carrier label that is not ready yet.
///
/// When a package is being shipped, the frontend readiness loop
/// (<c>frontend/src/components/baleni/printLabelPdf.ts</c>) repeatedly requests the label PDF.
/// Until the carrier has generated the label, the endpoint legitimately answers 404, so a single
/// print produces a burst of expected 404s. The auto-collected request-tracking module records
/// each of these as a <em>failed</em> <see cref="RequestTelemetry"/>, flooding the App Insights
/// Failures blade even though nothing actually failed.
///
/// Every in-loop poll carries the <see cref="PackagingController.LabelPollHeader"/>
/// (<c>X-Label-Poll</c>) header; the final post-timeout confirmation request deliberately omits it.
/// This filter therefore drops only header-tagged polling 404s and leaves the genuine, un-tagged
/// final 404 (a truly stuck label) intact as a real failure signal.
///
/// Mirrors the established <see cref="AzureBlobConflictTelemetryFilter"/> pattern.
/// </summary>
public sealed class LabelPollNotFoundTelemetryFilter : ITelemetryProcessor
{
    private const string NotFoundResponseCode = "404";

    private readonly ITelemetryProcessor _next;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LabelPollNotFoundTelemetryFilter(
        ITelemetryProcessor next,
        IHttpContextAccessor httpContextAccessor)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public void Process(ITelemetry item)
    {
        if (item is RequestTelemetry request
            && string.Equals(request.ResponseCode, NotFoundResponseCode, StringComparison.Ordinal)
            && IsLabelPoll())
        {
            // Expected transient 404 during the SPA's label-readiness poll — not an application failure.
            return;
        }

        _next.Process(item);
    }

    private bool IsLabelPoll() =>
        _httpContextAccessor.HttpContext?.Request.Headers
            .ContainsKey(PackagingController.LabelPollHeader) ?? false;
}
