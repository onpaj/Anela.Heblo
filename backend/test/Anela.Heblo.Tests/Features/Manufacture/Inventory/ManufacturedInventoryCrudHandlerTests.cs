using AutoMapper;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;
using Anela.Heblo.Application.Features.Manufacture.UseCases.DeleteManufacturedInventoryItem;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Inventory;

public class CreateManufacturedInventoryItemHandlerTests
{
    private readonly Mock<IManufacturedProductInventoryRepository> _repositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly CreateManufacturedInventoryItemHandler _handler;

    public CreateManufacturedInventoryItemHandlerTests()
    {
        _repositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _mapperMock = new Mock<IMapper>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _currentUserServiceMock
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("id", "TestUser", "test@example.com", true));

        _handler = new CreateManufacturedInventoryItemHandler(
            _repositoryMock.Object,
            _mapperMock.Object,
            _currentUserServiceMock.Object,
            TimeProvider.System);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesItemAndReturnsMappedDto()
    {
        // Arrange
        var request = new CreateManufacturedInventoryItemRequest
        {
            ProductCode = "PROD-001",
            ProductName = "Test Product",
            Amount = 10m,
            LotNumber = "LOT-42",
            ExpirationDate = new DateOnly(2027, 1, 1),
            ManufactureOrderId = 5,
        };

        var createdItem = new ManufacturedProductInventoryItem(
            request.ProductCode,
            request.ProductName,
            request.Amount,
            "TestUser",
            DateTime.UtcNow,
            request.LotNumber,
            request.ExpirationDate,
            request.ManufactureOrderId);

        var expectedDto = new ManufacturedProductInventoryItemDto
        {
            Id = 1,
            ProductCode = "PROD-001",
            ProductName = "Test Product",
            Amount = 10m,
        };

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdItem);

        _mapperMock
            .Setup(m => m.Map<ManufacturedProductInventoryItemDto>(It.IsAny<ManufacturedProductInventoryItem>()))
            .Returns(expectedDto);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Item.Should().NotBeNull();
        result.Item!.ProductCode.Should().Be("PROD-001");
        _repositoryMock.Verify(
            r => r.AddAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class UpdateManufacturedInventoryItemHandlerTests
{
    private readonly Mock<IManufacturedProductInventoryRepository> _repositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly UpdateManufacturedInventoryItemHandler _handler;

    public UpdateManufacturedInventoryItemHandlerTests()
    {
        _repositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _mapperMock = new Mock<IMapper>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _currentUserServiceMock
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("id", "TestUser", "test@example.com", true));

        _handler = new UpdateManufacturedInventoryItemHandler(
            _repositoryMock.Object,
            _mapperMock.Object,
            _currentUserServiceMock.Object,
            TimeProvider.System);
    }

    [Fact]
    public async Task Handle_ExistingItem_CallsManualAdjustAndReturnsDto()
    {
        // Arrange
        var existingItem = new ManufacturedProductInventoryItem(
            "PROD-001", "Test Product", 5m, "creator", DateTime.UtcNow);

        var expectedDto = new ManufacturedProductInventoryItemDto
        {
            Id = 1,
            ProductCode = "PROD-001",
            Amount = 15m,
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingItem);

        _mapperMock
            .Setup(m => m.Map<ManufacturedProductInventoryItemDto>(existingItem))
            .Returns(expectedDto);

        var request = new UpdateManufacturedInventoryItemRequest
        {
            Id = 1,
            NewAmount = 15m,
            Note = "Adjustment note",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Item.Should().NotBeNull();
        existingItem.Amount.Should().Be(15m);
        _repositoryMock.Verify(r => r.UpdateAsync(existingItem, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ItemNotFound_ReturnsNotFoundError()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufacturedProductInventoryItem?)null);

        var request = new UpdateManufacturedInventoryItemRequest
        {
            Id = 99,
            NewAmount = 10m,
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ManufacturedInventoryItemNotFound);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class DeleteManufacturedInventoryItemHandlerTests
{
    private readonly Mock<IManufacturedProductInventoryRepository> _repositoryMock;
    private readonly DeleteManufacturedInventoryItemHandler _handler;

    public DeleteManufacturedInventoryItemHandlerTests()
    {
        _repositoryMock = new Mock<IManufacturedProductInventoryRepository>();

        _handler = new DeleteManufacturedInventoryItemHandler(
            _repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ExistingItem_DeletesItem()
    {
        // Arrange
        var existingItem = new ManufacturedProductInventoryItem(
            "PROD-001", "Test Product", 8m, "creator", DateTime.UtcNow);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingItem);

        var request = new DeleteManufacturedInventoryItemRequest { Id = 1, Note = "removing" };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteAsync(existingItem, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ItemNotFound_ReturnsNotFoundError()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufacturedProductInventoryItem?)null);

        var request = new DeleteManufacturedInventoryItemRequest { Id = 99 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ManufacturedInventoryItemNotFound);
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
