using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.FileStorage.Infrastructure.Jobs;

public sealed class ProductExportDownloadJobTests
{
    // Captures (eventName, properties) pairs from TrackBusinessEvent calls
    private readonly List<(string EventName, Dictionary<string, string> Properties)> _trackedEvents = new();

    private Mock<ITelemetryService> CreateTelemetryMock()
    {
        var mock = new Mock<ITelemetryService>();
        mock
            .Setup(t => t.TrackBusinessEvent(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, double>>()))
            .Callback<string, Dictionary<string, string>?, Dictionary<string, double>?>(
                (eventName, props, _) =>
                    _trackedEvents.Add((eventName, props ?? new Dictionary<string, string>())));
        return mock;
    }

    private static ProductExportDownloadJob CreateJob(
        Mock<IMediator> mediatorMock,
        Mock<IRecurringJobStatusChecker> statusCheckerMock,
        Mock<ITelemetryService> telemetryMock,
        string exportUrl = "https://export.example.com/products.csv",
        string containerName = "exports")
    {
        var options = Options.Create(new ProductExportOptions
        {
            Url = exportUrl,
            ContainerName = containerName,
            MaxRetryAttempts = 3,
            DownloadTimeout = TimeSpan.FromSeconds(30),
            RetryBaseDelay = TimeSpan.FromSeconds(1),
        });

        return new ProductExportDownloadJob(
            mediatorMock.Object,
            new NullLogger<ProductExportDownloadJob>(),
            statusCheckerMock.Object,
            telemetryMock.Object,
            options);
    }

    private static Mock<IRecurringJobStatusChecker> EnabledStatusChecker()
    {
        var mock = new Mock<IRecurringJobStatusChecker>();
        mock
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return mock;
    }

    private static Mock<IRecurringJobStatusChecker> DisabledStatusChecker()
    {
        var mock = new Mock<IRecurringJobStatusChecker>();
        mock
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return mock;
    }

    [Fact]
    public void Job_HasAutomaticRetryAttribute_WithZeroAttempts()
    {
        // Use CustomAttributeData to inspect without instantiating AutomaticRetryAttribute,
        // whose constructor accesses Hangfire's global LogProvider which may reference a
        // disposed ILoggerFactory from another test's WebApplicationFactory.
        var attributeData = typeof(ProductExportDownloadJob)
            .GetCustomAttributesData()
            .FirstOrDefault(d => d.AttributeType == typeof(AutomaticRetryAttribute));

        attributeData.Should().NotBeNull("ProductExportDownloadJob must have [AutomaticRetry] attribute");

        var attempts = attributeData!.NamedArguments
            .Where(a => a.MemberName == nameof(AutomaticRetryAttribute.Attempts))
            .Select(a => (int)a.TypedValue.Value!)
            .FirstOrDefault();

        attempts.Should().Be(0, "Hangfire retries must be disabled to avoid Polly x Hangfire retry multiplication");
    }

    [Fact]
    public async Task Execute_OnSuccess_EmitsExactlyOneSuccessEvent()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<DownloadFromUrlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadFromUrlResponse
            {
                Success = true,
                BlobUrl = "https://blob/file.csv",
                BlobName = "file.csv",
                ContainerName = "exports",
                FileSizeBytes = 1024,
            });

        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, EnabledStatusChecker(), telemetry);

        // Act
        await job.ExecuteAsync();

        // Assert
        _trackedEvents.Should().HaveCount(1);
        _trackedEvents[0].Properties["Status"].Should().Be("Success");
    }

    [Fact]
    public async Task Execute_OnHandlerFailure_EmitsFailedEvent_AndRethrows()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<DownloadFromUrlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadFromUrlResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.FileDownloadFailed,
                Params = new Dictionary<string, string>
                {
                    ["cause"] = "retry-exhausted",
                    ["attemptCount"] = "4",
                },
            });

        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, EnabledStatusChecker(), telemetry);

        // Act
        var act = async () => await job.ExecuteAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        _trackedEvents.Should().HaveCount(1);
        _trackedEvents[0].Properties["Status"].Should().Be("Failed");
        _trackedEvents[0].Properties["Cause"].Should().Be("retry-exhausted");
        _trackedEvents[0].Properties["AttemptCount"].Should().Be("4");
    }

    [Fact]
    public async Task Execute_OnCallerCancellation_EmitsCancelledEvent_AndRethrows()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<DownloadFromUrlRequest>(), It.IsAny<CancellationToken>()))
            .Returns<DownloadFromUrlRequest, CancellationToken>((_, ct) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(ct);
            });

        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, EnabledStatusChecker(), telemetry);

        // Act
        var act = async () => await job.ExecuteAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();

        _trackedEvents.Should().HaveCount(1);
        _trackedEvents[0].Properties["Status"].Should().Be("Cancelled");
    }

    [Fact]
    public async Task Execute_OnJobDisabled_EmitsSkippedEvent()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, DisabledStatusChecker(), telemetry);

        // Act
        await job.ExecuteAsync();

        // Assert
        _trackedEvents.Should().HaveCount(1);
        _trackedEvents[0].Properties["Status"].Should().Be("Skipped");

        mediator.Verify(
            m => m.Send(It.IsAny<DownloadFromUrlRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_OnSuccess_DoesNotEmitFailedEvent()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<DownloadFromUrlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadFromUrlResponse
            {
                Success = true,
                BlobUrl = "https://blob/file.csv",
                BlobName = "file.csv",
                ContainerName = "exports",
                FileSizeBytes = 2048,
            });

        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, EnabledStatusChecker(), telemetry);

        // Act
        await job.ExecuteAsync();

        // Assert
        _trackedEvents.Should().NotContain(e => e.Properties["Status"] == "Failed");
    }
}
