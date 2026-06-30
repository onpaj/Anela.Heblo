using Anela.Heblo.API.Telemetry;
using FluentAssertions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using ModelContextProtocol;
using Moq;

namespace Anela.Heblo.Tests.Infrastructure.Telemetry;

public class McpProductNotFoundTelemetryFilterTests
{
    private readonly Mock<ITelemetryProcessor> _next = new();
    private readonly McpProductNotFoundTelemetryFilter _filter;

    public McpProductNotFoundTelemetryFilterTests()
    {
        _filter = new McpProductNotFoundTelemetryFilter(_next.Object);
    }

    private static ExceptionTelemetry BuildMcpExceptionTelemetry(string message)
    {
        var exception = new McpException(message);
        var exc = new ExceptionTelemetry(exception);
        exc.Message = message;
        return exc;
    }

    [Fact]
    public void Process_ConvertsMatchingMcpExceptionToWarningTrace()
    {
        var exc = BuildMcpExceptionTelemetry("[ProductNotFound] ProductNotFound: productCode: SA014");

        _filter.Process(exc);

        _next.Verify(n => n.Process(It.Is<ExceptionTelemetry>(_ => true)), Times.Never);
        _next.Verify(n => n.Process(It.Is<TraceTelemetry>(t =>
            t.SeverityLevel == SeverityLevel.Warning &&
            t.Message.Contains("[ProductNotFound]"))), Times.Once);
    }

    [Fact]
    public void Process_CopiesPropertiesFromExceptionToTrace()
    {
        var exc = BuildMcpExceptionTelemetry("[ProductNotFound] ProductNotFound: productCode: SA014");
        exc.Properties["productCode"] = "SA014";
        exc.Properties["someKey"] = "someValue";

        _filter.Process(exc);

        _next.Verify(n => n.Process(It.Is<TraceTelemetry>(t =>
            t.Properties.ContainsKey("productCode") &&
            t.Properties["productCode"] == "SA014" &&
            t.Properties.ContainsKey("someKey") &&
            t.Properties["someKey"] == "someValue")), Times.Once);
    }

    [Fact]
    public void Process_ForwardsOtherMcpExceptionTypes()
    {
        var exception = new McpException("[UNKNOWN_ERROR] Something went wrong.");
        var exc = new ExceptionTelemetry(exception);
        exc.Message = "[UNKNOWN_ERROR] Something went wrong.";

        _filter.Process(exc);

        _next.Verify(n => n.Process(exc), Times.Once);
    }

    [Fact]
    public void Process_ForwardsNonMcpExceptions()
    {
        var exception = new InvalidOperationException("[ProductNotFound] Something failed.");
        var exc = new ExceptionTelemetry(exception);
        exc.Message = "[ProductNotFound] Something failed.";

        _filter.Process(exc);

        _next.Verify(n => n.Process(exc), Times.Once);
    }

    [Fact]
    public void Process_ForwardsNonExceptionTelemetry()
    {
        var trace = new TraceTelemetry("hello world");

        _filter.Process(trace);

        _next.Verify(n => n.Process(trace), Times.Once);
    }
}
