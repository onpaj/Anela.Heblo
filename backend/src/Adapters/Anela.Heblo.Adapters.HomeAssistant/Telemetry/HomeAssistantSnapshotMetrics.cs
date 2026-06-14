using System.Diagnostics.Metrics;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;

namespace Anela.Heblo.Adapters.HomeAssistant.Telemetry;

public sealed class HomeAssistantSnapshotMetrics : IDisposable
{
    public const string MeterName = "Anela.Heblo.HomeAssistant";
    public const string SnapshotCounterName = "homeassistant.snapshot.source";

    private readonly Meter _meter;
    private readonly Counter<long> _snapshotCounter;

    public HomeAssistantSnapshotMetrics()
    {
        _meter = new Meter(MeterName);
        _snapshotCounter = _meter.CreateCounter<long>(SnapshotCounterName);
    }

    public void RecordSnapshot(ConditionsReadingSource source)
    {
        _snapshotCounter.Add(1, new KeyValuePair<string, object?>("source", source.ToString()));
    }

    public void Dispose() => _meter.Dispose();
}
