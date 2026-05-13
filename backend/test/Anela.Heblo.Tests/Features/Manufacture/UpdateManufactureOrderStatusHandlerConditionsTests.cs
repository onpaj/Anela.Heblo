using System.Security.Claims;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class UpdateManufactureOrderStatusHandlerConditionsTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepositoryMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<IConditionsReadingProvider> _conditionsProviderMock;
    private readonly Mock<ILogger<UpdateManufactureOrderStatusHandler>> _loggerMock;

    public UpdateManufactureOrderStatusHandlerConditionsTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _inventoryRepositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderStatusHandler>>();
        _conditionsProviderMock = new Mock<IConditionsReadingProvider>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        var claims = new List<Claim> { new(ClaimTypes.Name, "Test User") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var httpContext = new Mock<HttpContext>();
        httpContext.Setup(x => x.User).Returns(principal);
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext.Object);

        _inventoryRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufacturedProductInventoryItem item, CancellationToken _) => item);
    }

    private UpdateManufactureOrderStatusHandler CreateHandler() =>
        new UpdateManufactureOrderStatusHandler(
            _repositoryMock.Object,
            TimeProvider.System,
            _loggerMock.Object,
            _httpContextAccessorMock.Object,
            _conditionsProviderMock.Object,
            _inventoryRepositoryMock.Object);

    private ManufactureOrder CreateOrderInState(ManufactureOrderState state) =>
        new ManufactureOrder
        {
            Id = 1,
            OrderNumber = "MO-2026-001",
            State = state,
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = "previous-user",
            CreatedByUser = "creator",
            CreatedDate = DateTime.UtcNow.AddDays(-1),
            PlannedDate = DateOnly.FromDateTime(DateTime.Today),
        };

    [Fact]
    public async Task Handle_TransitionToSemiProductManufactured_CapturesConditionsReadingWithLiveSource()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.Planned);
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var snapshot = new ConditionsSnapshot(
            InnerTemperature: 21.5m,
            InnerHumidity: 55.0m,
            OuterTemperature: 18.2m,
            OuterHumidity: 72.3m,
            RecordedAt: DateTime.UtcNow,
            Source: ConditionsReadingSource.Live);
        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.SemiProductManufactured,
            ChangeReason = "Test reason",
        };
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        order.ConditionsReadings.Should().HaveCount(1);
        var reading = order.ConditionsReadings.Single();
        reading.Stage.Should().Be(ManufactureOrderState.SemiProductManufactured);
        reading.InnerTemperature.Should().Be(21.5m);
        reading.InnerHumidity.Should().Be(55.0m);
        reading.OuterTemperature.Should().Be(18.2m);
        reading.OuterHumidity.Should().Be(72.3m);
        reading.Source.Should().Be(ConditionsReadingSource.Live);
    }

    [Fact]
    public async Task Handle_TransitionToCompleted_CapturesConditionsReadingWithCorrectStage()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.SemiProductManufactured);
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var snapshot = new ConditionsSnapshot(21m, 50m, 15m, 65m, DateTime.UtcNow, ConditionsReadingSource.Live);
        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.Completed,
            ChangeReason = "Done",
        };
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reading = order.ConditionsReadings.Single();
        reading.Stage.Should().Be(ManufactureOrderState.Completed);
    }

    [Fact]
    public async Task Handle_ConditionsProviderReturnsUnavailable_ReadingPersistedWithNullValuesTransitionSucceeds()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.Planned);
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var snapshot = new ConditionsSnapshot(null, null, null, null, DateTime.UtcNow, ConditionsReadingSource.Unavailable);
        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.SemiProductManufactured,
            ChangeReason = "Test",
        };
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reading = order.ConditionsReadings.Single();
        reading.InnerTemperature.Should().BeNull();
        reading.Source.Should().Be(ConditionsReadingSource.Unavailable);
    }

    [Fact]
    public async Task Handle_ConditionsProviderThrows_ReadingPersistedAsUnavailableTransitionSucceeds()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.Planned);
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.SemiProductManufactured,
            ChangeReason = "Test",
        };
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        order.ConditionsReadings.Should().HaveCount(1);
        var reading = order.ConditionsReadings.Single();
        reading.Source.Should().Be(ConditionsReadingSource.Unavailable);
        reading.InnerTemperature.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TransitionToNonConditionsStage_DoesNotCaptureConditionsReading()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.Planned);
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.Draft,
            ChangeReason = "Reset",
        };
        var handler = CreateHandler();

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert — provider never called, no reading added
        _conditionsProviderMock.Verify(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()), Times.Never);
        order.ConditionsReadings.Should().BeEmpty();
    }
}
