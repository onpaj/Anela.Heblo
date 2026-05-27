using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.RemoveItemFromBox;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
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
    private readonly Mock<IInventoryReservationService> _inventoryReservationServiceMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<RemoveItemFromBoxHandler>> _loggerMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly RemoveItemFromBoxHandler _handler;

    private static readonly DateTime FixedTime = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    public RemoveItemFromBoxHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _inventoryReservationServiceMock = new Mock<IInventoryReservationService>();
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
            _inventoryReservationServiceMock.Object,
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
        _inventoryReservationServiceMock.Verify(
            x => x.RestoreAsync(
                It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ItemWithSourceInventory_DelegatesRestoreToInventoryService()
    {
        // Arrange
        var box = CreateOpenBoxWithItem(itemId: 1, sourceInventoryId: 42, amount: 10.0);

        var request = new RemoveItemFromBoxRequest { BoxId = 1, ItemId = 1 };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _inventoryReservationServiceMock
            .Setup(x => x.RestoreAsync(
                42, 10m, It.IsAny<string>(), It.IsAny<DateTime>(),
                1, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        _inventoryReservationServiceMock.Verify(
            x => x.RestoreAsync(
                42, 10m, "Test User", FixedTime,
                1, "B001", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ItemWithSourceInventory_WhenInventoryMissing_StillSucceeds()
    {
        // Arrange — inventory item was deleted; adapter handles the null case internally (log + skip)
        var box = CreateOpenBoxWithItem(itemId: 1, sourceInventoryId: 99, amount: 5.0);

        var request = new RemoveItemFromBoxRequest { BoxId = 1, ItemId = 1 };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        // RestoreAsync is called but returns normally (adapter handles missing inventory internally)
        _inventoryReservationServiceMock
            .Setup(x => x.RestoreAsync(
                99, 5m, It.IsAny<string>(), It.IsAny<DateTime>(),
                1, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _inventoryReservationServiceMock.Verify(
            x => x.RestoreAsync(
                99, It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
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

}
