using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class UpdateManufactureOrderStatusHandlerConditionsTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IManufactureInventoryWriteDownService> _inventoryWriteDownServiceMock;
    private readonly Mock<IManufactureConditionsCaptureService> _conditionsCaptureServiceMock;
    private readonly Mock<ILogger<UpdateManufactureOrderStatusHandler>> _loggerMock;

    public UpdateManufactureOrderStatusHandlerConditionsTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderStatusHandler>>();
        _inventoryWriteDownServiceMock = new Mock<IManufactureInventoryWriteDownService>();
        _conditionsCaptureServiceMock = new Mock<IManufactureConditionsCaptureService>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: "test-id",
                Name: "Test User",
                Email: "test@example.com",
                IsAuthenticated: true));

        _inventoryWriteDownServiceMock
            .Setup(x => x.WriteDownAsync(It.IsAny<ManufactureOrder>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _conditionsCaptureServiceMock
            .Setup(x => x.CaptureAsync(It.IsAny<ManufactureOrder>(), It.IsAny<ManufactureOrderState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, ManufactureOrderState stage, CancellationToken _) => new ManufactureOrderConditionsReading
            {
                ManufactureOrderId = order.Id,
                Stage = stage,
                Source = ConditionsReadingSource.Live,
                RecordedAt = DateTime.UtcNow,
            });
    }

    private UpdateManufactureOrderStatusHandler CreateHandler() =>
        new UpdateManufactureOrderStatusHandler(
            _repositoryMock.Object,
            TimeProvider.System,
            _loggerMock.Object,
            _currentUserServiceMock.Object,
            _inventoryWriteDownServiceMock.Object,
            _conditionsCaptureServiceMock.Object);

    private ManufactureOrder CreateOrderInState(ManufactureOrderState state)
    {
        var order = new ManufactureOrder
        {
            Id = 1,
            OrderNumber = "MO-2026-001",
            CreatedByUser = "creator",
            CreatedDate = DateTime.UtcNow.AddDays(-1),
            PlannedDate = DateOnly.FromDateTime(DateTime.Today),
        };
        order.InitializeState(state, DateTime.UtcNow, "previous-user");
        return order;
    }

    [Fact]
    public async Task Handle_TransitionToSemiProductManufactured_CallsConditionsCaptureService()
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
            NewState = ManufactureOrderState.SemiProductManufactured,
            ChangeReason = "Test reason",
        };
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _conditionsCaptureServiceMock.Verify(
            x => x.CaptureAsync(order, ManufactureOrderState.SemiProductManufactured, It.IsAny<CancellationToken>()),
            Times.Once);
        order.ConditionsReadings.Should().HaveCount(1);
        order.ConditionsReadings.Single().Stage.Should().Be(ManufactureOrderState.SemiProductManufactured);
    }

    [Fact]
    public async Task Handle_TransitionToCompleted_CallsConditionsCaptureServiceWithCorrectStage()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.SemiProductManufactured);
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

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
        _conditionsCaptureServiceMock.Verify(
            x => x.CaptureAsync(order, ManufactureOrderState.Completed, It.IsAny<CancellationToken>()),
            Times.Once);
        var reading = order.ConditionsReadings.Single();
        reading.Stage.Should().Be(ManufactureOrderState.Completed);
    }

    [Fact]
    public async Task Handle_DoesNotAddDuplicateConditionsReading_WhenStageAlreadyHasReading()
    {
        // Arrange
        var originalRecordedAt = DateTime.UtcNow.AddHours(-1);
        var order = CreateOrderInState(ManufactureOrderState.Planned);
        order.ConditionsReadings.Add(new ManufactureOrderConditionsReading
        {
            ManufactureOrderId = 1,
            Stage = ManufactureOrderState.SemiProductManufactured,
            InnerTemperature = 22.0m,
            Source = ConditionsReadingSource.Live,
            RecordedAt = originalRecordedAt,
        });

        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.SemiProductManufactured,
            ChangeReason = "Re-entering stage after rollback",
        };
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        order.ConditionsReadings.Should().HaveCount(1);
        order.ConditionsReadings.Single().RecordedAt.Should().Be(originalRecordedAt);
        _conditionsCaptureServiceMock.Verify(
            x => x.CaptureAsync(It.IsAny<ManufactureOrder>(), It.IsAny<ManufactureOrderState>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

        // Assert — service never called, no reading added
        _conditionsCaptureServiceMock.Verify(
            x => x.CaptureAsync(It.IsAny<ManufactureOrder>(), It.IsAny<ManufactureOrderState>(), It.IsAny<CancellationToken>()),
            Times.Never);
        order.ConditionsReadings.Should().BeEmpty();
    }
}
