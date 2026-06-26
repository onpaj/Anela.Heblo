using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class CreateLotHandlerTests
{
    private readonly Mock<ILotRepository> _lotRepo;
    private readonly Mock<ICatalogRepository> _catalogRepo;
    private readonly Mock<ICurrentUserService> _currentUser;
    private readonly CreateLotHandler _handler;

    public CreateLotHandlerTests()
    {
        _lotRepo = new Mock<ILotRepository>();
        _catalogRepo = new Mock<ICatalogRepository>();
        _currentUser = new Mock<ICurrentUserService>();

        _currentUser.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test User", "test@anela.cz", true));

        _handler = new CreateLotHandler(
            NullLogger<CreateLotHandler>.Instance,
            _lotRepo.Object,
            _catalogRepo.Object,
            _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesLotAndReturnsSuccess()
    {
        // Arrange
        var material = CreateMaterialCatalogItem("MAT001");
        _catalogRepo.Setup(r => r.GetByIdAsync("MAT001", default)).ReturnsAsync(material);
        _lotRepo.Setup(r => r.ExistsAsync("MAT001", "LOT-ABC", default)).ReturnsAsync(false);
        _lotRepo.Setup(r => r.AddAsync(It.IsAny<Lot>(), default)).ReturnsAsync((Lot l, CancellationToken _) => l);
        _lotRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateLotRequest
        {
            MaterialCode = "MAT001",
            LotCode = "LOT-ABC",
            Expiration = new DateOnly(2027, 6, 30),
            ReceivedDate = new DateOnly(2026, 5, 13),
            Notes = "Test lot"
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("MAT001", result.Lot.MaterialCode);
        Assert.Equal("LOT-ABC", result.Lot.LotCode);
        _lotRepo.Verify(r => r.AddAsync(It.IsAny<Lot>(), default), Times.Once);
        _lotRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_MaterialNotFound_ReturnsInventoryMaterialNotFound()
    {
        // Arrange
        _catalogRepo.Setup(r => r.GetByIdAsync("MISSING", default)).ReturnsAsync((CatalogAggregate?)null);

        var request = new CreateLotRequest
        {
            MaterialCode = "MISSING",
            LotCode = "L1",
            ReceivedDate = DateOnly.FromDateTime(DateTime.Today)
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InventoryMaterialNotFound, result.ErrorCode);
        _lotRepo.Verify(r => r.AddAsync(It.IsAny<Lot>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_MaterialIsNotMaterialType_ReturnsInventoryMaterialInvalidType()
    {
        // Arrange
        var product = CreateCatalogItem("PROD001", ProductType.Product);
        _catalogRepo.Setup(r => r.GetByIdAsync("PROD001", default)).ReturnsAsync(product);

        var request = new CreateLotRequest
        {
            MaterialCode = "PROD001",
            LotCode = "L1",
            ReceivedDate = DateOnly.FromDateTime(DateTime.Today)
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InventoryMaterialInvalidType, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_DuplicateLot_ReturnsLotAlreadyExists()
    {
        // Arrange
        var material = CreateMaterialCatalogItem("MAT001");
        _catalogRepo.Setup(r => r.GetByIdAsync("MAT001", default)).ReturnsAsync(material);
        _lotRepo.Setup(r => r.ExistsAsync("MAT001", "LOT-DUP", default)).ReturnsAsync(true);

        var request = new CreateLotRequest
        {
            MaterialCode = "MAT001",
            LotCode = "LOT-DUP",
            ReceivedDate = DateOnly.FromDateTime(DateTime.Today)
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotAlreadyExists, result.ErrorCode);
    }

    private static CatalogAggregate CreateMaterialCatalogItem(string productCode)
        => CreateCatalogItem(productCode, ProductType.Material);

    private static CatalogAggregate CreateCatalogItem(string productCode, ProductType productType)
        => new CatalogAggregate { ProductCode = productCode, Type = productType };
}
