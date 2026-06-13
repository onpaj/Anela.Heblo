using Anela.Heblo.Adapters.HomeAssistant.Resilience;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Anela.Heblo.Adapters.HomeAssistant.Telemetry;

public sealed class HomeAssistantDependencyTelemetryFilter : ITelemetryProcessor
{
    private readonly ITelemetryProcessor _next;

    public HomeAssistantDependencyTelemetryFilter(ITelemetryProcessor next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public void Process(ITelemetry item)
    {
        if (item is DependencyTelemetry dep
            && dep.Properties.TryGetValue(HomeAssistantRetryActivityTaggingHandler.SuppressTagName, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _next.Process(item);
    }
}
