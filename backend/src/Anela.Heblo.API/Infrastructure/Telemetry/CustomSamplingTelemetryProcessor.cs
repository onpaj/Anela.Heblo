using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Anela.Heblo.API.Telemetry;

/// <summary>
/// Custom sampling processor for fine-grained control over telemetry sampling.
/// </summary>
public class CustomSamplingTelemetryProcessor : ITelemetryProcessor
{
    private readonly ITelemetryProcessor _next;
    private readonly Random _random = new();

    // Sampling rates by telemetry type (0.0 to 1.0)
    private readonly Dictionary<string, double> _samplingRates = new()
    {
        ["Request"] = 0.3,        // Keep 30% of requests
        ["Dependency"] = 0.1,     // Keep 10% of dependencies
        ["Trace"] = 0.05,         // Keep 5% of traces
        ["PageView"] = 0.5,       // Keep 50% of page views
        ["Event"] = 1.0,          // Keep all custom events
        ["Exception"] = 1.0,      // Keep all exceptions
        ["Metric"] = 1.0          // Keep all metrics
    };

    public CustomSamplingTelemetryProcessor(ITelemetryProcessor next)
    {
        _next = next;
    }

    public void Process(ITelemetry item)
    {
        // Always send critical telemetry
        if (item is ExceptionTelemetry)
        {
            _next.Process(item);
            return;
        }

        // Apply custom sampling rules for requests
        if (item is RequestTelemetry request)
        {
            // Always track failed requests
            if (request.Success == false)
            {
                _next.Process(item);
                return;
            }

            // Always track slow requests (> 1 second)
            if (request.Duration > TimeSpan.FromSeconds(1))
            {
                _next.Process(item);
                return;
            }

            // Apply sampling for successful fast requests
            if (ShouldSample("Request"))
            {
                _next.Process(item);
            }
            return;
        }

        // Apply custom sampling for dependencies
        if (item is DependencyTelemetry dependency)
        {
            // Always track failed dependencies
            if (dependency.Success == false)
            {
                _next.Process(item);
                return;
            }

            // Always track slow dependencies (> 500ms)
            if (dependency.Duration > TimeSpan.FromMilliseconds(500))
            {
                _next.Process(item);
                return;
            }

            // Apply sampling for successful fast dependencies
            if (ShouldSample("Dependency"))
            {
                _next.Process(item);
            }
            return;
        }

        // Apply sampling for traces based on severity
        if (item is TraceTelemetry trace)
        {
            // Always keep warnings and above
            if (trace.SeverityLevel >= SeverityLevel.Warning)
            {
                _next.Process(item);
                return;
            }

            // Sample informational and verbose traces
            if (ShouldSample("Trace"))
            {
                _next.Process(item);
            }
            return;
        }

        // Apply default sampling for other telemetry types
        var telemetryType = item.GetType().Name.Replace("Telemetry", "");
        if (_samplingRates.TryGetValue(telemetryType, out var rate))
        {
            if (ShouldSample(telemetryType))
            {
                _next.Process(item);
            }
        }
        else
        {
            // If no specific rate defined, use 10% sampling
            if (_random.NextDouble() < 0.1)
            {
                _next.Process(item);
            }
        }
    }

    private bool ShouldSample(string telemetryType)
    {
        if (_samplingRates.TryGetValue(telemetryType, out var rate))
        {
            return _random.NextDouble() < rate;
        }
        return false;
    }
}