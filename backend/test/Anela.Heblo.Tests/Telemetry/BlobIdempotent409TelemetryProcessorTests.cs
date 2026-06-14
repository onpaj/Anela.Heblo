using Anela.Heblo.API.Telemetry;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Telemetry;

public class BlobIdempotent409TelemetryProcessorTests
{
    private readonly Mock<ITelemetryProcessor> _next = new();
    private readonly BlobIdempotent409TelemetryProcessor _processor;

    public BlobIdempotent409TelemetryProcessorTests()
    {
        _processor = new BlobIdempotent409TelemetryProcessor(_next.Object);
    }

    [Fact]
    public void Process_AzureBlob409OnPutContainer_MarksSuccessAndForwards()
    {
        // Arrange — Data ends with the container name, no further path segments (PUT container shape)
        var telemetry = new DependencyTelemetry
        {
            Type = "Azure blob",
            ResultCode = "409",
            Data = "https://stheblo.blob.core.windows.net/photobank",
            Success = false
        };

        // Act
        _processor.Process(telemetry);

        // Assert — re-marked as success and forwarded
        Assert.True(telemetry.Success);
        _next.Verify(n => n.Process(telemetry), Times.Once);
    }

    [Fact]
    public void Process_AzureBlob409OnPutBlob_DoesNotMarkSuccess()
    {
        // Arrange — Data contains a blob path after the container (PUT blob shape)
        var telemetry = new DependencyTelemetry
        {
            Type = "Azure blob",
            ResultCode = "409",
            Data = "https://stheblo.blob.core.windows.net/photobank/thumbnail/abc.jpg",
            Success = false
        };

        // Act
        _processor.Process(telemetry);

        // Assert — Success remains false (genuine blob conflict — let it surface)
        Assert.False(telemetry.Success);
        _next.Verify(n => n.Process(telemetry), Times.Once);
    }

    [Fact]
    public void Process_AzureBlob200_PassesThroughUnchanged()
    {
        // Arrange
        var telemetry = new DependencyTelemetry
        {
            Type = "Azure blob",
            ResultCode = "200",
            Data = "https://stheblo.blob.core.windows.net/photobank",
            Success = true
        };

        // Act
        _processor.Process(telemetry);

        // Assert
        Assert.True(telemetry.Success);
        _next.Verify(n => n.Process(telemetry), Times.Once);
    }

    [Fact]
    public void Process_Sql409_PassesThroughUnchanged()
    {
        // Arrange
        var telemetry = new DependencyTelemetry
        {
            Type = "SQL",
            ResultCode = "409",
            Data = "INSERT INTO X",
            Success = false
        };

        // Act
        _processor.Process(telemetry);

        // Assert
        Assert.False(telemetry.Success);
        _next.Verify(n => n.Process(telemetry), Times.Once);
    }

    [Fact]
    public void Process_NonDependencyTelemetry_PassesThroughUnchanged()
    {
        // Arrange
        var telemetry = new TraceTelemetry("hello");

        // Act
        _processor.Process(telemetry);

        // Assert
        _next.Verify(n => n.Process(telemetry), Times.Once);
    }
}
