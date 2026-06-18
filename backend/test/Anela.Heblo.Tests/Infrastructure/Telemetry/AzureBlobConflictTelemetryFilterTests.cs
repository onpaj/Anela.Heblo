using Anela.Heblo.API.Telemetry;
using FluentAssertions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Moq;

namespace Anela.Heblo.Tests.Infrastructure.Telemetry;

public class AzureBlobConflictTelemetryFilterTests
{
    private readonly Mock<ITelemetryProcessor> _next = new();
    private readonly AzureBlobConflictTelemetryFilter _filter;

    public AzureBlobConflictTelemetryFilterTests()
    {
        _filter = new AzureBlobConflictTelemetryFilter(_next.Object);
    }

    [Fact]
    public void Process_DropsAzureBlob409Conflict()
    {
        var dep = new DependencyTelemetry
        {
            Type = "Azure blob",
            Name = "PUT stheblo",
            ResultCode = "409",
            Success = false
        };

        _filter.Process(dep);

        _next.Verify(n => n.Process(It.IsAny<ITelemetry>()), Times.Never);
    }

    [Fact]
    public void Process_DropsAzureBlob409_CaseInsensitiveType()
    {
        var dep = new DependencyTelemetry
        {
            Type = "AZURE BLOB",
            ResultCode = "409",
            Success = false
        };

        _filter.Process(dep);

        _next.Verify(n => n.Process(It.IsAny<ITelemetry>()), Times.Never);
    }

    [Fact]
    public void Process_ForwardsAzureBlobNonConflictFailures()
    {
        var dep = new DependencyTelemetry
        {
            Type = "Azure blob",
            ResultCode = "500",
            Success = false
        };

        _filter.Process(dep);

        _next.Verify(n => n.Process(dep), Times.Once);
    }

    [Fact]
    public void Process_Forwards409FromOtherDependencyTypes()
    {
        var dep = new DependencyTelemetry
        {
            Type = "Http",
            ResultCode = "409",
            Success = false
        };

        _filter.Process(dep);

        _next.Verify(n => n.Process(dep), Times.Once);
    }

    [Fact]
    public void Process_ForwardsNonDependencyTelemetry()
    {
        var trace = new TraceTelemetry("hello");

        _filter.Process(trace);

        _next.Verify(n => n.Process(trace), Times.Once);
    }
}
