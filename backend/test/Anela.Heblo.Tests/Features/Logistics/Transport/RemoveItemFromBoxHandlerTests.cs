using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.RemoveItemFromBox;
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

public class RemoveItemFromBoxHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<RemoveItemFromBoxHandler>> _loggerMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly RemoveItemFromBoxHandler _handler;

    private static readonly DateTime FixedTime = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    public RemoveItemFromBoxHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _inventoryRepositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<RemoveItemFromBoxHandler>>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-id", "Test User", "test@example.com", true));

        _timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(FixedTime));

        _mapperMock
            .Setup(x => x.Map<TransportBoxDto>(It.IsAny<TransportBox>()))
            .Returns(new TransportBoxDto());

        _handler = new RemoveItemFromBoxHandler(
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
        var request = new RemoveItemFromBoxRequest { BoxId = 999, ItemId = 1 };

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
    public async Task Handle_ItemNotFound_ReturnsFailure()
    {
        // Arrange
        var box = CreateOpenBox();
        var request = new RemoveItemFromBoxRequest { BoxId = 1, ItemId = 999 };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Handle_ItemWithoutSourceInventory_NoInventoryInteraction()
    {
        // Arrange
        var box = CreateOpenBoxWithItem(itemId: 1, sourceInventoryId: null, amount: 5.0);
        var request = new RemoveItemFromBoxRequest { BoxId = 1, ItemId = 1 };

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
    public async Task Handle_ItemWithSourceInventory_RestoresInventory()
    {
        // Arrange
        var box = CreateOpenBoxWithItem(itemId: 1, sourceInventoryId: 42, amount: 10.0);
        var inventoryItem = CreateInventoryItem(productCode: "PROD-001", amount: 50m);

        var request = new RemoveItemFromBoxRequest { BoxId = 1, ItemId = 1 };

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

        // Inventory was restored (UpdateAsync called with item that has increased amount)
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(
                It.Is<ManufacturedProductInventoryItem>(i => i.Amount == 60m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ItemWithSourceInventory_InventoryItemDeleted_ProceedsWithRemoval()
    {
        // Arrange — inventory item was manually deleted, but removal should still succeed
        var box = CreateOpenBoxWithItem(itemId: 1, sourceInventoryId: 99, amount: 5.0);

        var request = new RemoveItemFromBoxRequest { BoxId = 1, ItemId = 1 };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufacturedProductInventoryItem?)null);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

    private static TransportBox CreateOpenBoxWithItem(int itemId, int? sourceInventoryId, double amount)
    {
        var box = CreateOpenBox();

        var itemsField = typeof(TransportBox).GetField("_items",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (itemsField != null)
        {
            var items = (List<TransportBoxItem>)itemsField.GetValue(box)!;

            var item = new TransportBoxItem(
                productCode: "PROD-001",
                productName: "Test Product",
                amount: amount,
                dateAdded: new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                userAdded: "Test User",
                sourceInventoryId: sourceInventoryId);

            // Entity<T>.Id is a public settable property
            item.Id = itemId;

            items.Add(item);
        }

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
