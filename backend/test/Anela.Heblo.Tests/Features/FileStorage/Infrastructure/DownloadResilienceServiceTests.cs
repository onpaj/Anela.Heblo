using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Polly.Timeout;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage.Infrastructure;

public sealed class DownloadResilienceServiceTests
{
    private static IOptions<ProductExportOptions> CreateOptions(
        int maxRetryAttempts = 3,
        TimeSpan? downloadTimeout = null,
        TimeSpan? retryBaseDelay = null)
    {
        var options = new ProductExportOptions
        {
            MaxRetryAttempts = maxRetryAttempts,
            DownloadTimeout = downloadTimeout ?? TimeSpan.FromSeconds(120),
            RetryBaseDelay = retryBaseDelay ?? TimeSpan.FromSeconds(2),
        };
        return Options.Create(options);
    }

    // Returns (service, telemetryMock) so tests can configure the mock after creation.
    // ITelemetryService is Scoped in production; the scope factory pattern mirrors that here.
    private static (DownloadResilienceService Service, Mock<ITelemetryService> Telemetry) CreateService(
        IOptions<ProductExportOptions> options,
        Mock<ILogger<DownloadResilienceService>>? loggerMock = null)
    {
        var telemetryMock = new Mock<ITelemetryService>();
        loggerMock ??= new Mock<ILogger<DownloadResilienceService>>();

        var services = new ServiceCollection();
        services.AddSingleton(telemetryMock.Object);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var service = new DownloadResilienceService(options, scopeFactory, loggerMock.Object);
        return (service, telemetryMock);
    }

    [Fact]
    public void Constructor_Throws_When_WorstCaseExceeds20Minutes()
    {
        // Arrange: (10+1) * 2 min = 22 min > 20 min
        var options = CreateOptions(maxRetryAttempts: 10, downloadTimeout: TimeSpan.FromMinutes(2));

        // Act
        var act = () => CreateService(options);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxRetryAttempts*");
    }

    [Fact]
    public void Constructor_Succeeds_With_Defaults()
    {
        // Arrange: defaults = (3+1) * 120 s = 480 s = 8 min < 20 min
        var options = CreateOptions();

        // Act
        var act = () => CreateService(options);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_ReturnsResult_OnFirstAttemptSuccess()
    {
        // Arrange
        var (service, telemetryMock) = CreateService(CreateOptions());

        // Act
        var result = await service.ExecuteWithResilienceAsync<int>(
            _ => Task.FromResult(42),
            "test-operation");

        // Assert
        result.Should().Be(42);
        telemetryMock.Verify(
            t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_RetriesOn_HttpRequestException_ThenSucceeds()
    {
        // Arrange
        var (service, telemetryMock) = CreateService(
            CreateOptions(maxRetryAttempts: 3, retryBaseDelay: TimeSpan.FromMilliseconds(1)));

        var capturedProperties = new List<Dictionary<string, string>?>();
        telemetryMock
            .Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
            .Callback<Exception, Dictionary<string, string>?>((_, props) => capturedProperties.Add(props));

        var callCount = 0;

        // Act
        var result = await service.ExecuteWithResilienceAsync<string>(
            _ =>
            {
                callCount++;
                if (callCount == 1)
                    throw new HttpRequestException("transient");
                return Task.FromResult("ok");
            },
            "test-op");

        // Assert
        result.Should().Be("ok");
        callCount.Should().Be(2);
        telemetryMock.Verify(
            t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()),
            Times.Once);
        capturedProperties.Should().HaveCount(1);
        capturedProperties[0].Should().ContainKey("IsTerminal").WhoseValue.Should().Be("false");
        capturedProperties[0].Should().ContainKey("AttemptNumber").WhoseValue.Should().Be("1");
        capturedProperties[0].Should().ContainKey("Job").WhoseValue.Should().Be("test-op");
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_DoesNotRetry_OnCallerCancel()
    {
        // Arrange
        var (service, telemetryMock) = CreateService(CreateOptions());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var callerCt = cts.Token;

        // Act
        var act = async () => await service.ExecuteWithResilienceAsync<string>(
            ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult("never");
            },
            "cancel-op",
            callerCt);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        telemetryMock.Verify(
            t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_RetriesOn_InnerTimeout()
    {
        // Arrange: DownloadTimeout = 50ms, MaxRetryAttempts = 1, RetryBaseDelay = 1ms
        // worst case = (1+1) * 50ms = 100ms << 20 min so ctor succeeds
        var options = CreateOptions(
            maxRetryAttempts: 1,
            downloadTimeout: TimeSpan.FromMilliseconds(50),
            retryBaseDelay: TimeSpan.FromMilliseconds(1));

        var (service, telemetryMock) = CreateService(options);
        var callCount = 0;

        // Act & Assert
        var act = async () => await service.ExecuteWithResilienceAsync<string>(
            async ct =>
            {
                callCount++;
                await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                return "should not reach";
            },
            "timeout-op");

        // Polly v8 timeout strategy throws TimeoutRejectedException (subclass of OperationCanceledException)
        await act.Should().ThrowAsync<TimeoutRejectedException>();
        callCount.Should().Be(2); // 1 initial + 1 retry
        telemetryMock.Verify(
            t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_ExhaustsRetries_OnPersistentHttpRequestException()
    {
        // Arrange
        var (service, telemetryMock) = CreateService(
            CreateOptions(maxRetryAttempts: 3, retryBaseDelay: TimeSpan.FromMilliseconds(1)));
        var callCount = 0;

        // Act
        var act = async () => await service.ExecuteWithResilienceAsync<string>(
            _ =>
            {
                callCount++;
                throw new HttpRequestException("boom");
            },
            "persistent-op");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("boom");
        callCount.Should().Be(4); // 1 initial + 3 retries
        telemetryMock.Verify(
            t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_DoesNotRetry_OnNonRetryableException()
    {
        // Arrange
        var (service, telemetryMock) = CreateService(CreateOptions());
        var callCount = 0;

        // Act
        var act = async () => await service.ExecuteWithResilienceAsync<string>(
            _ =>
            {
                callCount++;
                throw new InvalidOperationException("not retryable");
            },
            "non-retryable-op");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("not retryable");
        callCount.Should().Be(1);
        telemetryMock.Verify(
            t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()),
            Times.Never);
    }
}
