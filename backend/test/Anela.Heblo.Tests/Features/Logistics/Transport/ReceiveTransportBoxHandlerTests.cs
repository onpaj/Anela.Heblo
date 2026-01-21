using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.UseCases.ReceiveTransportBox;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class ReceiveTransportBoxHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<IStockUpOperationRepository> _stockUpOperationRepositoryMock;
    private readonly Mock<ILogger<ReceiveTransportBoxHandler>> _loggerMock;
    private readonly ReceiveTransportBoxHandler _handler;

    public ReceiveTransportBoxHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _stockUpOperationRepositoryMock = new Mock<IStockUpOperationRepository>();
        _loggerMock = new Mock<ILogger<ReceiveTransportBoxHandler>>();
        _handler = new ReceiveTransportBoxHandler(
            _loggerMock.Object,
            _repositoryMock.Object,
            _stockUpOperationRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_EmptyUserName_ReturnsFailureResponse()
    {
        // Arrange
        var request = new ReceiveTransportBoxRequest
        {
            BoxId = 1,
            UserName = ""
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
    }

    [Fact]
    public async Task Handle_BoxNotFound_ReturnsFailureResponse()
    {
        // Arrange
        var request = new ReceiveTransportBoxRequest
        {
            BoxId = 999,
            UserName = "TestUser"
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((TransportBox?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxNotFound);
    }

    [Fact]
    public async Task Handle_BoxInInvalidState_ReturnsFailureResponse()
    {
        // Arrange
        var request = new ReceiveTransportBoxRequest
        {
            BoxId = 1,
            UserName = "TestUser"
        };
        var box = CreateTestBox(TransportBoxState.Opened, "B001");

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxStateChangeError);
        result.BoxId.Should().Be(1);
        result.BoxCode.Should().Be("B001");
    }

    [Theory]
    [InlineData(TransportBoxState.Reserve)]
    [InlineData(TransportBoxState.InTransit)]
    public async Task Handle_BoxInValidState_ReturnsSuccessResponse(TransportBoxState state)
    {
        // Arrange
        var request = new ReceiveTransportBoxRequest
        {
            BoxId = 1,
            UserName = "TestUser"
        };
        var box = CreateTestBox(state, "B001");

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.BoxId.Should().Be(1);
        result.BoxCode.Should().Be("B001");

        // Verify the domain method was called (box should now be in Received state)
        box.State.Should().Be(TransportBoxState.Received);

        // Verify repository methods were called
        _repositoryMock.Verify(x => x.UpdateAsync(box, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidationException_ReturnsFailureResponse()
    {
        // Arrange
        var request = new ReceiveTransportBoxRequest
        {
            BoxId = 1,
            UserName = "TestUser"
        };
        var box = CreateTestBox(TransportBoxState.Reserve, "B001");

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        // Setup UpdateAsync to throw ValidationException to simulate domain validation failure
        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException("Invalid state transition"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.BoxId.Should().Be(1);
        result.BoxCode.Should().Be("B001");
    }

    [Fact]
    public async Task Handle_GenericException_ReturnsFailureResponse()
    {
        // Arrange
        var request = new ReceiveTransportBoxRequest
        {
            BoxId = 1,
            UserName = "TestUser"
        };
        var box = CreateTestBox(TransportBoxState.Reserve, "B001");

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Exception);
        result.BoxId.Should().Be(1);
        result.BoxCode.Should().Be("B001");
    }

    [Fact]
    public async Task Handle_SuccessfulReceive_SetsReceivedStateAndClearsLocation()
    {
        // Arrange
        var request = new ReceiveTransportBoxRequest
        {
            BoxId = 1,
            UserName = "TestUser"
        };
        var box = CreateTestBoxWithLocation(TransportBoxState.Reserve, "B001", "Warehouse A");

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        // Check that domain method effects are applied
        box.State.Should().Be(TransportBoxState.Received);
        box.Location.Should().BeNull(); // Location should be cleared after receiving
    }

    private TransportBox CreateTestBox(TransportBoxState state, string code)
    {
        var box = new TransportBox();

        // Set state and code using reflection
        var stateProperty = typeof(TransportBox).GetProperty("State");
        stateProperty?.SetValue(box, state);

        var codeField = typeof(TransportBox).GetField("<Code>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        codeField?.SetValue(box, code);

        // Set Id for testing
        var idProperty = typeof(TransportBox).GetProperty("Id");
        idProperty?.SetValue(box, 1);

        return box;
    }

    private TransportBox CreateTestBoxWithLocation(TransportBoxState state, string code, string location)
    {
        var box = CreateTestBox(state, code);

        // Set location
        var locationProperty = typeof(TransportBox).GetProperty("Location");
        locationProperty?.SetValue(box, location);

        return box;
    }

    private TransportBox CreateTestBoxWithItems(TransportBoxState state, string code, int itemCount)
    {
        var box = CreateTestBox(state, code);

        // Add items to the box using reflection
        var itemsField = typeof(TransportBox).GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var items = (List<TransportBoxItem>)itemsField!.GetValue(box)!;

        for (int i = 0; i < itemCount; i++)
        {
            var item = new TransportBoxItem(
                $"PROD{i:D3}",
                $"Product {i}",
                10.0,
                DateTime.UtcNow,
                "TestUser");
            items.Add(item);
        }

        return box;
    }

    [Fact]
    public async Task Handle_ReceiveBox_CreatesStockUpOperationsForEachItem()
    {
        // Arrange
        var box = CreateTestBoxWithItems(TransportBoxState.InTransit, "B001", 3);
        var request = new ReceiveTransportBoxRequest
        {
            BoxId = 1,
            UserName = "TestUser"
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _stockUpOperationRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockUpOperation op, CancellationToken ct) => op);

        _stockUpOperationRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        box.State.Should().Be(TransportBoxState.Received);

        // Verify StockUpOperations were created (3 items = 3 operations)
        _stockUpOperationRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        // Verify operations were saved
        _stockUpOperationRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify each operation has correct format BOX-{boxId:000000}-{productCode}
        _stockUpOperationRepositoryMock.Verify(
            x => x.AddAsync(
                It.Is<StockUpOperation>(op =>
                    op.DocumentNumber.StartsWith("BOX-000001-PROD") &&
                    op.SourceType == StockUpSourceType.TransportBox &&
                    op.SourceId == 1),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }
}