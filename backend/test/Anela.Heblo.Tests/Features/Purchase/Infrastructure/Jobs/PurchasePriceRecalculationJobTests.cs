using Anela.Heblo.Application.Features.Purchase.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase.Infrastructure.Jobs;

public sealed class PurchasePriceRecalculationJobTests
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

    private static PurchasePriceRecalculationJob CreateJob(
        Mock<IMediator> mediatorMock, Mock<ITelemetryService> telemetryMock)
    {
        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(true);

        return new PurchasePriceRecalculationJob(
            mediatorMock.Object,
            NullLogger<PurchasePriceRecalculationJob>.Instance,
            statusChecker.Object,
            telemetryMock.Object);
    }

    private static RecalculatePurchasePriceResponse ResponseWithPriceSyncFailed(int failed) =>
        new()
        {
            PriceSync = new PurchasePriceSyncSummary { Candidates = 5, Written = 5 - failed, Failed = failed },
        };

    [Fact]
    public async Task Execute_WhenPriceSyncHasFailures_TracksPartialFailureStatus()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<RecalculatePurchasePriceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResponseWithPriceSyncFailed(failed: 1));

        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, telemetry);

        // Act
        await job.ExecuteAsync();

        // Assert
        _trackedEvents.Should().ContainSingle();
        _trackedEvents[0].Properties["Status"].Should().Be("PartialFailure");
        _trackedEvents[0].Properties["PriceSyncFailed"].Should().Be("1");
    }

    [Fact]
    public async Task Execute_WhenPriceSyncHasNoFailures_TracksSuccessStatus()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<RecalculatePurchasePriceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResponseWithPriceSyncFailed(failed: 0));

        var telemetry = CreateTelemetryMock();
        var job = CreateJob(mediator, telemetry);

        // Act
        await job.ExecuteAsync();

        // Assert
        _trackedEvents.Should().ContainSingle();
        _trackedEvents[0].Properties["Status"].Should().Be("Success");
        _trackedEvents[0].Properties["PriceSyncFailed"].Should().Be("0");
    }
}
