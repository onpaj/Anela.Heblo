using System.Diagnostics;
using System.Diagnostics.Metrics;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Xcc.Telemetry;

namespace Anela.Heblo.API.Webhooks.Smartsupp;

public sealed class SmartsuppWebhookMetrics : ISmartsuppWebhookMetrics, IDisposable
{
    public const string MeterName = "Anela.Heblo.Smartsupp.Webhooks";

    private readonly Meter _meter;
    private readonly Counter<long> _received;
    private readonly Counter<long> _signatureFailures;
    private readonly Histogram<double> _handleDuration;
    private readonly Histogram<int> _payloadBytes;
    private readonly ITelemetryService _telemetry;

    public SmartsuppWebhookMetrics(IMeterFactory meterFactory, ITelemetryService telemetry)
    {
        _meter = meterFactory.Create(MeterName);
        _telemetry = telemetry;

        _received = _meter.CreateCounter<long>(
            "smartsupp.webhook.received_total",
            description: "Total webhook events received, tagged by event name and outcome");

        _signatureFailures = _meter.CreateCounter<long>(
            "smartsupp.webhook.signature_failures_total",
            description: "Total signature verification failures");

        _handleDuration = _meter.CreateHistogram<double>(
            "smartsupp.webhook.handle_duration_ms",
            unit: "ms",
            description: "Handler duration per event");

        _payloadBytes = _meter.CreateHistogram<int>(
            "smartsupp.webhook.payload_bytes",
            unit: "bytes",
            description: "Webhook payload size in bytes");
    }

    public void RecordReceived(string eventName, string outcome, double durationMs)
    {
        var tags = new TagList
        {
            { "event", eventName },
            { "outcome", outcome },
        };
        _received.Add(1, tags);
        _handleDuration.Record(durationMs, tags);

        _telemetry.TrackBusinessEvent("smartsupp.webhook.received", new Dictionary<string, string>
        {
            ["event"] = eventName,
            ["outcome"] = outcome,
        }, new Dictionary<string, double>
        {
            ["duration_ms"] = durationMs,
        });
    }

    public void RecordSignatureFailure(string reason)
    {
        _signatureFailures.Add(1, new TagList { { "reason", reason } });
        _telemetry.TrackMetric("smartsupp.webhook.signature_failures_total", 1,
            new Dictionary<string, string> { ["reason"] = reason });
    }

    public void RecordPayloadBytes(int bytes)
    {
        _payloadBytes.Record(bytes);
    }

    public void Dispose() => _meter.Dispose();
}
