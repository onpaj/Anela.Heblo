using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderSchedule;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class UpdateManufactureOrderScheduleHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<UpdateManufactureOrderScheduleHandler>> _loggerMock;
    private readonly UpdateManufactureOrderScheduleHandler _handler;

    private const int ValidOrderId = 1;
    private static readonly DateOnly ValidSemiProductDate = DateOnly.FromDateTime(DateTime.Today.AddDays(5));
    private static readonly DateOnly ValidProductDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10));

    public UpdateManufactureOrderScheduleHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderScheduleHandler>>();

        _handler = new UpdateManufactureOrderScheduleHandler(
            _repositoryMock.Object,
            _mapperMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldUpdateScheduleAndReturnSuccess()
    {
        // Arrange
        var request = CreateValidRequest();
        var existingOrder = CreateExistingOrder();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) => order);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Be("Schedule updated successfully");
    }

    [Fact]
    public async Task Handle_WithNonExistentOrder_ShouldReturnResourceNotFoundError()
    {
        // Arrange
        var request = CreateValidRequest();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        result.Message.Should().Be("Manufacture order not found");
    }

    [Fact]
    public async Task Handle_WithCancelledOrder_ShouldReturnValidationError()
    {
        // Arrange
        var request = CreateValidRequest();
        var existingOrder = CreateExistingOrder();
        existingOrder.State = ManufactureOrderState.Cancelled;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.CannotUpdateCancelledOrder);
        result.Message.Should().Contain("Cannot update schedule for cancelled orders");
    }

    [Fact]
    public async Task Handle_WithCompletedOrder_ShouldReturnValidationError()
    {
        // Arrange
        var request = CreateValidRequest();
        var existingOrder = CreateExistingOrder();
        existingOrder.State = ManufactureOrderState.Completed;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.CannotUpdateCompletedOrder);
        result.Message.Should().Contain("Cannot update schedule for completed orders");
    }

    [Fact]
    public async Task Handle_WithValidFuturePlannedDate_ShouldUpdateSuccessfully()
    {
        // Arrange
        var request = CreateValidRequest();
        request.PlannedDate = ValidProductDate.AddDays(5); // Future date should be valid

        var existingOrder = CreateExistingOrder();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) => order);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Schedule updated successfully");
    }

    [Fact]
    public async Task Handle_WithPastSemiProductDate_ShouldReturnValidationError()
    {
        // Arrange
        var request = CreateValidRequest();
        request.PlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)); // Past date

        var existingOrder = CreateExistingOrder();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.CannotScheduleInPast);
        result.Message.Should().Contain("Cannot schedule manufacturing in the past");
    }

    [Fact]
    public async Task Handle_WithPastProductDate_ShouldReturnValidationError()
    {
        // Arrange
        var request = CreateValidRequest();
        request.PlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)); // Past date

        var existingOrder = CreateExistingOrder();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.CannotScheduleInPast);
        result.Message.Should().Contain("Cannot schedule manufacturing in the past");
    }

    [Fact]
    public async Task Handle_ShouldUpdateScheduleDates()
    {
        // Arrange
        var request = CreateValidRequest();
        var existingOrder = CreateExistingOrder();
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        updatedOrder.Should().NotBeNull();
        updatedOrder!.PlannedDate.Should().Be(request.PlannedDate);
    }


    [Fact]
    public async Task Handle_WithNoChanges_ShouldReturnNoChangesMessage()
    {
        // Arrange
        var request = CreateValidRequest();
        request.PlannedDate = null;

        var existingOrder = CreateExistingOrder();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Be("No changes were made to the schedule");
    }

    [Fact]
    public async Task Handle_WithSameScheduleDates_ShouldReturnNoChangesMessage()
    {
        // Arrange
        var existingOrder = CreateExistingOrder();
        var request = new UpdateManufactureOrderScheduleRequest
        {
            Id = ValidOrderId,
            PlannedDate = existingOrder.PlannedDate,
            ChangeReason = "Test change"
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Be("No changes were made to the schedule");
    }

    [Fact]
    public async Task Handle_WhenRepositoryThrowsException_ShouldReturnInternalServerError()
    {
        // Arrange
        var request = CreateValidRequest();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        result.Message.Should().Be("An error occurred while updating the schedule");
    }

    [Fact]
    public async Task Handle_WhenRepositoryUpdateThrows_ShouldReturnInternalServerError()
    {
        // Arrange
        var request = CreateValidRequest();
        var existingOrder = CreateExistingOrder();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Update failed"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        result.Message.Should().Be("An error occurred while updating the schedule");
    }

    [Fact]
    public async Task Handle_WhenExceptionOccurs_ShouldLogError()
    {
        // Arrange
        var request = CreateValidRequest();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Error updating schedule for manufacture order {ValidOrderId}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static UpdateManufactureOrderScheduleRequest CreateValidRequest()
    {
        return new UpdateManufactureOrderScheduleRequest
        {
            Id = ValidOrderId,
            PlannedDate = ValidSemiProductDate,
            ChangeReason = "Test schedule update"
        };
    }

    private static ManufactureOrder CreateExistingOrder()
    {
        return new ManufactureOrder
        {
            Id = ValidOrderId,
            OrderNumber = "MO-2024-001",
            CreatedDate = DateTime.UtcNow.AddDays(-1),
            CreatedByUser = "Original User",
            ResponsiblePerson = "Test User",
            PlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            State = ManufactureOrderState.Draft,
            StateChangedAt = DateTime.UtcNow.AddDays(-1),
            StateChangedByUser = "Original User",
            SemiProduct = new ManufactureOrderSemiProduct
            {
                Id = 1,
                ProductCode = "SEMI001",
                ProductName = "Semi Product 1",
                PlannedQuantity = 1000m
            },
            Products = new List<ManufactureOrderProduct>
            {
                new()
                {
                    Id = 1,
                    ProductCode = "PROD001",
                    ProductName = "Product 1",
                    PlannedQuantity = 50m,
                    SemiProductCode = "SEMI001"
                }
            },
            Notes = new List<ManufactureOrderNote>()
        };
    }
}