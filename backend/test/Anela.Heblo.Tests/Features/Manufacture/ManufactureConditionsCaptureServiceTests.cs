using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureConditionsCaptureServiceTests
{
    private readonly Mock<IConditionsReadingProvider> _conditionsProviderMock;
    private readonly Mock<ILogger<ManufactureConditionsCaptureService>> _loggerMock;
    private readonly ManufactureConditionsCaptureService _service;

    private const int ValidOrderId = 1;

    public ManufactureConditionsCaptureServiceTests()
    {
        _conditionsProviderMock = new Mock<IConditionsReadingProvider>();
        _loggerMock = new Mock<ILogger<ManufactureConditionsCaptureService>>();

        _service = new ManufactureConditionsCaptureService(
            TimeProvider.System,
            _loggerMock.Object,
            _conditionsProviderMock.Object);
    }

    [Fact]
    public async Task CaptureAsync_WithLiveSnapshot_ReturnsReadingWithMappedFields()
    {
        // Arrange
        var order = CreateOrder();
        var snapshot = new ConditionsSnapshot(
            InnerTemperature: 21.5m,
            InnerHumidity: 55.0m,
            OuterTemperature: 18.2m,
            OuterHumidity: 72.3m,
            RecordedAt: DateTime.UtcNow,
            Source: ConditionsReadingSource.Live);
        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var reading = await _service.CaptureAsync(order, ManufactureOrderState.SemiProductManufactured, CancellationToken.None);

        // Assert
        reading.ManufactureOrderId.Should().Be(ValidOrderId);
        reading.Stage.Should().Be(ManufactureOrderState.SemiProductManufactured);
        reading.InnerTemperature.Should().Be(21.5m);
        reading.InnerHumidity.Should().Be(55.0m);
        reading.OuterTemperature.Should().Be(18.2m);
        reading.OuterHumidity.Should().Be(72.3m);
        reading.Source.Should().Be(ConditionsReadingSource.Live);
    }

    [Fact]
    public async Task CaptureAsync_SetsRequestedStage()
    {
        // Arrange
        var order = CreateOrder();
        var snapshot = new ConditionsSnapshot(21m, 50m, 15m, 65m, DateTime.UtcNow, ConditionsReadingSource.Live);
        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var reading = await _service.CaptureAsync(order, ManufactureOrderState.Completed, CancellationToken.None);

        // Assert
        reading.Stage.Should().Be(ManufactureOrderState.Completed);
    }

    [Fact]
    public async Task CaptureAsync_WhenProviderReturnsUnavailable_ReadingPersistedWithNullValues()
    {
        // Arrange
        var order = CreateOrder();
        var snapshot = new ConditionsSnapshot(null, null, null, null, DateTime.UtcNow, ConditionsReadingSource.Unavailable);
        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var reading = await _service.CaptureAsync(order, ManufactureOrderState.SemiProductManufactured, CancellationToken.None);

        // Assert
        reading.InnerTemperature.Should().BeNull();
        reading.Source.Should().Be(ConditionsReadingSource.Unavailable);
    }

    [Fact]
    public async Task CaptureAsync_WhenProviderThrows_ReturnsFallbackUnavailableReading()
    {
        // Arrange
        var order = CreateOrder();
        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var reading = await _service.CaptureAsync(order, ManufactureOrderState.SemiProductManufactured, CancellationToken.None);

        // Assert
        reading.Source.Should().Be(ConditionsReadingSource.Unavailable);
        reading.InnerTemperature.Should().BeNull();
    }

    private static ManufactureOrder CreateOrder()
    {
        var order = new ManufactureOrder
        {
            Id = ValidOrderId,
            OrderNumber = "MO-2026-001",
            CreatedByUser = "creator",
            CreatedDate = DateTime.UtcNow.AddDays(-1),
            PlannedDate = DateOnly.FromDateTime(DateTime.Today),
        };
        order.InitializeState(ManufactureOrderState.Planned, DateTime.UtcNow, "previous-user");
        return order;
    }
}
