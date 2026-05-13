using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.AddItemToBox;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class AddItemToBoxHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<AddItemToBoxHandler>> _loggerMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly AddItemToBoxHandler _handler;

    private static readonly DateTime FixedTime = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    public AddItemToBoxHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _inventoryRepositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<AddItemToBoxHandler>>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-id", "Test User", "test@example.com", true));

        _timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(FixedTime));

        _mapperMock
            .Setup(x => x.Map<TransportBoxItemDto>(It.IsAny<TransportBoxItem>()))
            .Returns(new TransportBoxItemDto());

        _mapperMock
            .Setup(x => x.Map<TransportBoxDto>(It.IsAny<TransportBox>()))
            .Returns(new TransportBoxDto());

        _handler = new AddItemToBoxHandler(
            _repositoryMock.Object,
            _inventoryRepositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object,
            _mapperMock.Object,
            _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_BoxNotFound_ReturnsFailure()
    {
        // Arrange
        var request = new AddItemToBoxRequest
        {
            BoxId = 999,
            ProductCode = "PROD-001",
            ProductName = "Test Product",
            Amount = 5.0
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
    public async Task Handle_WithoutSourceInventoryId_AddsItemWithoutInventoryInteraction()
    {
        // Arrange
        var box = CreateOpenBox();
        var request = new AddItemToBoxRequest
        {
            BoxId = 1,
            ProductCode = "PROD-001",
            ProductName = "Test Product",
            Amount = 5.0
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _inventoryRepositoryMock.Verify(
            x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithSourceInventoryId_ConsumesInventoryAndSetsLotOnItem()
    {
        // Arrange
        var box = CreateOpenBox();
        var inventoryItem = CreateInventoryItem(productCode: "PROD-001", amount: 100m);

        var request = new AddItemToBoxRequest
        {
            BoxId = 1,
            ProductCode = "PROD-001",
            ProductName = "Test Product",
            Amount = 10.0,
            SourceInventoryId = 42,
            LotNumber = "LOT-123",
            ExpirationDate = new DateOnly(2026, 12, 31)
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inventoryItem);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        // Inventory was consumed (UpdateAsync called with item that has reduced amount)
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(
                It.Is<ManufacturedProductInventoryItem>(i => i.Amount == 90m),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Box item has the lot and source inventory set
        var addedItem = box.Items.Single();
        addedItem.SourceInventoryId.Should().Be(42);
        addedItem.LotNumber.Should().Be("LOT-123");
        addedItem.ExpirationDate.Should().Be(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public async Task Handle_WithSourceInventoryId_InsufficientStock_ReturnsError()
    {
        // Arrange
        var box = CreateOpenBox();
        var inventoryItem = CreateInventoryItem(productCode: "PROD-001", amount: 5m);

        var request = new AddItemToBoxRequest
        {
            BoxId = 1,
            ProductCode = "PROD-001",
            ProductName = "Test Product",
            Amount = 100.0,  // more than available
            SourceInventoryId = 42
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inventoryItem);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ManufacturedInventoryInsufficientStock);

        // Box save should not have been called
        _repositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithSourceInventoryId_InventoryNotFound_ReturnsError()
    {
        // Arrange
        var box = CreateOpenBox();
        var request = new AddItemToBoxRequest
        {
            BoxId = 1,
            ProductCode = "PROD-001",
            ProductName = "Test Product",
            Amount = 5.0,
            SourceInventoryId = 999
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufacturedProductInventoryItem?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ManufacturedInventoryItemNotFound);
    }

    private static TransportBox CreateOpenBox()
    {
        var box = new TransportBox();

        var stateProperty = typeof(TransportBox).GetProperty("State");
        stateProperty?.SetValue(box, TransportBoxState.Opened);

        // Entity<T>.Id is a public settable property
        box.Id = 1;

        var codeField = typeof(TransportBox).GetField("<Code>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        codeField?.SetValue(box, "B001");

        return box;
    }

    private static ManufacturedProductInventoryItem CreateInventoryItem(string productCode, decimal amount)
    {
        return new ManufacturedProductInventoryItem(
            productCode: productCode,
            productName: "Test Product",
            amount: amount,
            createdBy: "system",
            createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
