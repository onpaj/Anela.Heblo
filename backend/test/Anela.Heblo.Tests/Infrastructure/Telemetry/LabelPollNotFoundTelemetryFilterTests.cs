using Anela.Heblo.API.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Moq;

namespace Anela.Heblo.Tests.Infrastructure.Telemetry;

public class LabelPollNotFoundTelemetryFilterTests
{
    private const string LabelPollHeader = "X-Label-Poll";

    private readonly Mock<ITelemetryProcessor> _next = new();

    private LabelPollNotFoundTelemetryFilter CreateFilter(bool withPollHeader)
    {
        var httpContext = new DefaultHttpContext();
        if (withPollHeader)
        {
            httpContext.Request.Headers[LabelPollHeader] = "1";
        }

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        return new LabelPollNotFoundTelemetryFilter(_next.Object, accessor.Object);
    }

    [Fact]
    public void Process_DropsPolling404()
    {
        var filter = CreateFilter(withPollHeader: true);
        var request = new RequestTelemetry { ResponseCode = "404", Success = false };

        filter.Process(request);

        _next.Verify(n => n.Process(It.IsAny<ITelemetry>()), Times.Never);
    }

    [Fact]
    public void Process_ForwardsFinal404WithoutPollHeader()
    {
        var filter = CreateFilter(withPollHeader: false);
        var request = new RequestTelemetry { ResponseCode = "404", Success = false };

        filter.Process(request);

        _next.Verify(n => n.Process(request), Times.Once);
    }

    [Fact]
    public void Process_ForwardsSuccessfulRequestWithPollHeader()
    {
        var filter = CreateFilter(withPollHeader: true);
        var request = new RequestTelemetry { ResponseCode = "200", Success = true };

        filter.Process(request);

        _next.Verify(n => n.Process(request), Times.Once);
    }

    [Fact]
    public void Process_Forwards404FromUnrelatedRequestWithoutHeader()
    {
        var filter = CreateFilter(withPollHeader: false);
        var request = new RequestTelemetry { ResponseCode = "404", Success = false };

        filter.Process(request);

        _next.Verify(n => n.Process(request), Times.Once);
    }

    [Fact]
    public void Process_ForwardsNonRequestTelemetry()
    {
        var filter = CreateFilter(withPollHeader: true);
        var trace = new TraceTelemetry("hello");

        filter.Process(trace);

        _next.Verify(n => n.Process(trace), Times.Once);
    }

    [Fact]
    public void Process_ForwardsWhenHttpContextIsNull()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);
        var filter = new LabelPollNotFoundTelemetryFilter(_next.Object, accessor.Object);
        var request = new RequestTelemetry { ResponseCode = "404", Success = false };

        filter.Process(request);

        _next.Verify(n => n.Process(request), Times.Once);
    }
}
