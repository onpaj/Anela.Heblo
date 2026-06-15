using Anela.Heblo.Adapters.HomeAssistant.Resilience;
using Anela.Heblo.Adapters.HomeAssistant.Telemetry;
using FluentAssertions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Moq;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantDependencyTelemetryFilterTests
{
    private readonly Mock<ITelemetryProcessor> _next = new();
    private readonly HomeAssistantDependencyTelemetryFilter _filter;

    public HomeAssistantDependencyTelemetryFilterTests()
    {
        _filter = new HomeAssistantDependencyTelemetryFilter(_next.Object);
    }

    [Fact]
    public void Process_DropsDependencyTelemetryTaggedSuppress()
    {
        var dep = new DependencyTelemetry { Name = "GET /api/states/sensor.x" };
        dep.Properties[HomeAssistantRetryActivityTaggingHandler.SuppressTagName] = "true";

        _filter.Process(dep);

        _next.Verify(n => n.Process(It.IsAny<ITelemetry>()), Times.Never);
    }

    [Fact]
    public void Process_ForwardsDependencyTelemetryWithoutSuppressTag()
    {
        var dep = new DependencyTelemetry { Name = "GET /api/states/sensor.x" };

        _filter.Process(dep);

        _next.Verify(n => n.Process(dep), Times.Once);
    }

    [Fact]
    public void Process_ForwardsNonDependencyTelemetryEvenIfTaggedSomehow()
    {
        var trace = new TraceTelemetry("hello");
        trace.Properties[HomeAssistantRetryActivityTaggingHandler.SuppressTagName] = "true";

        _filter.Process(trace);

        _next.Verify(n => n.Process(trace), Times.Once);
    }
}
