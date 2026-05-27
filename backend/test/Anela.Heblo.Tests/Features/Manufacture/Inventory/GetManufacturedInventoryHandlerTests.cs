using AutoMapper;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufacturedInventory;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Inventory;

public class GetManufacturedInventoryHandlerTests
{
    private readonly Mock<IManufacturedProductInventoryRepository> _repositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly GetManufacturedInventoryHandler _handler;

    public GetManufacturedInventoryHandlerTests()
    {
        _repositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _mapperMock = new Mock<IMapper>();

        _handler = new GetManufacturedInventoryHandler(
            _repositoryMock.Object,
            _mapperMock.Object);
    }

    [Fact]
    public async Task Handle_WithItems_ReturnsMappedDtos()
    {
        // Arrange
        var items = new List<ManufacturedProductInventoryItem>
        {
            new("CODE1", "Product 1", 5m, "user", DateTime.UtcNow),
            new("CODE2", "Product 2", 10m, "user", DateTime.UtcNow),
        };
        var dtos = new List<ManufacturedProductInventoryItemDto>
        {
            new() { Id = 1, ProductCode = "CODE1", ProductName = "Product 1", Amount = 5m },
            new() { Id = 2, ProductCode = "CODE2", ProductName = "Product 2", Amount = 10m },
        };

        _repositoryMock
            .Setup(r => r.GetPagedListAsync(It.IsAny<ManufacturedInventoryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ManufacturedProductInventoryItem>)items, 2));

        _mapperMock
            .Setup(m => m.Map<List<ManufacturedProductInventoryItemDto>>(items))
            .Returns(dtos);

        var request = new GetManufacturedInventoryRequest();

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_OnlyWithStock_PassesFilterToRepository()
    {
        // Arrange
        var request = new GetManufacturedInventoryRequest { OnlyWithStock = true };

        _repositoryMock
            .Setup(r => r.GetPagedListAsync(It.IsAny<ManufacturedInventoryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ManufacturedProductInventoryItem>)new List<ManufacturedProductInventoryItem>(), 0));

        _mapperMock
            .Setup(m => m.Map<List<ManufacturedProductInventoryItemDto>>(It.IsAny<object>()))
            .Returns(new List<ManufacturedProductInventoryItemDto>());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetPagedListAsync(
                It.Is<ManufacturedInventoryFilter>(f => f.OnlyWithStock == true),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithSearch_PassesSearchToRepository()
    {
        // Arrange
        const string searchTerm = "PROD-001";
        var request = new GetManufacturedInventoryRequest { Search = searchTerm };

        _repositoryMock
            .Setup(r => r.GetPagedListAsync(It.IsAny<ManufacturedInventoryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ManufacturedProductInventoryItem>)new List<ManufacturedProductInventoryItem>(), 0));

        _mapperMock
            .Setup(m => m.Map<List<ManufacturedProductInventoryItemDto>>(It.IsAny<object>()))
            .Returns(new List<ManufacturedProductInventoryItemDto>());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetPagedListAsync(
                It.Is<ManufacturedInventoryFilter>(f => f.Search == searchTerm),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsSuccessWithEmptyList()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetPagedListAsync(It.IsAny<ManufacturedInventoryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ManufacturedProductInventoryItem>)new List<ManufacturedProductInventoryItem>(), 0));

        _mapperMock
            .Setup(m => m.Map<List<ManufacturedProductInventoryItemDto>>(It.IsAny<object>()))
            .Returns(new List<ManufacturedProductInventoryItemDto>());

        var request = new GetManufacturedInventoryRequest();

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }
}
