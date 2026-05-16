using System.Net;
using System.Net.Sockets;
using Anela.Heblo.Xcc.Http;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.Http;

public class OutboundCallObservabilityHandlerTests
{
    private static OutboundCallObservabilityHandler CreateHandler(
        HttpMessageHandler inner,
        Mock<ITelemetryService>? telemetry = null,
        HttpContext? httpContext = null,
        OutboundResilienceOptions? options = null,
        ILogger<OutboundCallObservabilityHandler>? logger = null)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        var optionsMonitor = new Mock<IOptionsMonitor<OutboundResilienceOptions>>();
        optionsMonitor.SetupGet(m => m.CurrentValue).Returns(options ?? new OutboundResilienceOptions());

        return new OutboundCallObservabilityHandler(
            logger ?? NullLogger<OutboundCallObservabilityHandler>.Instance,
            (telemetry ?? new Mock<ITelemetryService>()).Object,
            accessor.Object,
            optionsMonitor.Object)
        {
            InnerHandler = inner,
        };
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler);

    [Fact]
    public async Task HappyPath_DoesNotLogOrTrack()
    {
        // Arrange
        var inner = new StubHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var telemetry = new Mock<ITelemetryService>(MockBehavior.Strict);
        var handler = CreateHandler(inner, telemetry);
        var client = CreateClient(handler);

        // Act
        var response = await client.GetAsync("https://api.example.com/items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        telemetry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LoggingDisabled_DoesNotTrackOnException()
    {
        // Arrange
        var inner = new StubHandler((req, ct) => throw new HttpRequestException("boom"));
        var telemetry = new Mock<ITelemetryService>(MockBehavior.Strict);
        var options = new OutboundResilienceOptions { LoggingEnabled = false };
        var handler = CreateHandler(inner, telemetry, options: options);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/items");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        telemetry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ClientAborted_WhenHttpContextRequestAbortedFires_LogsWarningWithClientAbortedReason()
    {
        // Arrange
        var inboundCts = new CancellationTokenSource();
        var httpContext = new DefaultHttpContext { RequestAborted = inboundCts.Token };
        var inner = new StubHandler(async (req, ct) =>
        {
            inboundCts.Cancel();
            ct.ThrowIfCancellationRequested();
            return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? capturedProperties = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => capturedProperties = props);

        var loggerMock = new Mock<ILogger<OutboundCallObservabilityHandler>>();
        var handler = CreateHandler(inner, telemetry, httpContext: httpContext, logger: loggerMock.Object);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/items", inboundCts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        capturedProperties.Should().NotBeNull();
        capturedProperties![OutboundCallLogProperties.Reason].Should().Be(nameof(OutboundCallReason.ClientAborted));
        capturedProperties[OutboundCallLogProperties.HttpMethod].Should().Be("GET");
        capturedProperties[OutboundCallLogProperties.TargetHost].Should().Be("api.example.com");
        capturedProperties[OutboundCallLogProperties.TargetPath].Should().Be("/items");
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Timeout_WhenInboundTokenNotCancelled_LogsErrorWithTimeoutReason()
    {
        // Arrange
        var inner = new StubHandler((req, ct) => throw new TaskCanceledException("HttpClient.Timeout"));
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? captured = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => captured = props);
        // HttpContext exists, but its RequestAborted is not signaled.
        var httpContext = new DefaultHttpContext();
        var loggerMock = new Mock<ILogger<OutboundCallObservabilityHandler>>();
        var handler = CreateHandler(inner, telemetry, httpContext: httpContext, logger: loggerMock.Object);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/v1/resource");

        // Assert
        await act.Should().ThrowAsync<TaskCanceledException>();
        captured.Should().NotBeNull();
        captured![OutboundCallLogProperties.Reason].Should().Be(nameof(OutboundCallReason.Timeout));
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Theory]
    [InlineData(typeof(SocketException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(HttpRequestException))]
    public async Task Network_WhenTransportException_LogsErrorWithNetworkReason(Type exceptionType)
    {
        // Arrange
        Exception thrown = exceptionType.Name switch
        {
            nameof(SocketException) => new SocketException(),
            nameof(IOException) => new IOException("connection reset"),
            nameof(HttpRequestException) => new HttpRequestException("name resolution failed"),
            _ => throw new InvalidOperationException(),
        };
        var inner = new StubHandler((req, ct) => throw thrown);
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? captured = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => captured = props);
        var handler = CreateHandler(inner, telemetry);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/x");

        // Assert
        await act.Should().ThrowAsync<Exception>();
        captured.Should().NotBeNull();
        captured![OutboundCallLogProperties.Reason].Should().Be(nameof(OutboundCallReason.Network));
    }

    [Fact]
    public async Task TelemetryProperties_DoNotIncludeQueryString_NorBearerToken()
    {
        // Arrange
        var inner = new StubHandler((req, ct) => throw new HttpRequestException("boom"));
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? captured = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => captured = props);
        var handler = CreateHandler(inner, telemetry);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/v1/secret?token=should-not-leak&apiKey=sk-xyz");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        captured.Should().NotBeNull();
        captured![OutboundCallLogProperties.TargetPath].Should().Be("/v1/secret");
        captured.Values.Should().NotContain(v => v.Contains("token=", StringComparison.OrdinalIgnoreCase));
        captured.Values.Should().NotContain(v => v.Contains("apiKey=", StringComparison.OrdinalIgnoreCase));
        captured.Values.Should().NotContain(v => v.Contains("sk-xyz", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoHttpContext_WhenCallerTokenCancelled_ClassifiesAsClientAborted()
    {
        // Arrange — Hangfire / hosted service: HttpContext is null.
        using var cts = new CancellationTokenSource();
        var inner = new StubHandler(async (req, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? captured = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => captured = props);
        var handler = CreateHandler(inner, telemetry, httpContext: null);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/x", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        captured.Should().NotBeNull();
        captured![OutboundCallLogProperties.Reason].Should().Be(nameof(OutboundCallReason.ClientAborted));
    }

    [Fact]
    public async Task NoHttpContext_WhenCallerTokenNotCancelled_ClassifiesAsTimeout()
    {
        // Arrange — background context, exception is OperationCanceledException with no caller cancellation.
        var inner = new StubHandler((req, ct) => throw new TaskCanceledException("per-call CTS timed out"));
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? captured = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => captured = props);
        var handler = CreateHandler(inner, telemetry, httpContext: null);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/x");

        // Assert
        await act.Should().ThrowAsync<TaskCanceledException>();
        captured.Should().NotBeNull();
        captured![OutboundCallLogProperties.Reason].Should().Be(nameof(OutboundCallReason.Timeout));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _send(request, cancellationToken);
    }
}
