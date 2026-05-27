using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetProductCompositionHandlerTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock = new();
    private readonly Mock<IProductIngredientOrderRepository> _orderRepoMock = new();
    private readonly GetProductCompositionHandler _handler;

    public GetProductCompositionHandlerTests()
    {
        _handler = new GetProductCompositionHandler(
            _manufactureClientMock.Object,
            _orderRepoMock.Object);
    }

    [Fact]
    public async Task Handle_NoSavedOrder_AssignsContiguousOrder()
    {
        // Arrange
        var template = new ManufactureTemplate
        {
            ProductCode = "PRD1",
            Ingredients = new List<Ingredient>
            {
                new() { ProductCode = "A", ProductName = "Alpha", Amount = 10 },
                new() { ProductCode = "B", ProductName = "Beta",  Amount = 20 },
            }
        };
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _orderRepoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>());

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Should().HaveCount(2);
        response.Ingredients.Select(i => i.Order).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Handle_SavedOrderApplied_SortsByCustomOrder()
    {
        // Arrange
        var template = new ManufactureTemplate
        {
            ProductCode = "PRD1",
            Ingredients = new List<Ingredient>
            {
                new() { ProductCode = "A", ProductName = "Alpha", Amount = 10 },
                new() { ProductCode = "B", ProductName = "Beta",  Amount = 20 },
                new() { ProductCode = "C", ProductName = "Gamma", Amount = 30 },
            }
        };
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _orderRepoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>
            {
                new() { ParentProductCode = "PRD1", IngredientProductCode = "C", SortOrder = 1 },
                new() { ParentProductCode = "PRD1", IngredientProductCode = "A", SortOrder = 2 },
                new() { ParentProductCode = "PRD1", IngredientProductCode = "B", SortOrder = 3 },
            });

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Select(i => i.ProductCode).Should().Equal("C", "A", "B");
        response.Ingredients.Select(i => i.Order).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Handle_NewIngredientNotInSavedOrder_AppearsLast()
    {
        // Arrange
        var template = new ManufactureTemplate
        {
            ProductCode = "PRD1",
            Ingredients = new List<Ingredient>
            {
                new() { ProductCode = "A", ProductName = "Alpha", Amount = 10 },
                new() { ProductCode = "B", ProductName = "Beta",  Amount = 20 },
                new() { ProductCode = "NEW", ProductName = "Newcomer", Amount = 5 },
            }
        };
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _orderRepoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>
            {
                new() { ParentProductCode = "PRD1", IngredientProductCode = "B", SortOrder = 1 },
                new() { ParentProductCode = "PRD1", IngredientProductCode = "A", SortOrder = 2 },
            });

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Last().ProductCode.Should().Be("NEW");
        response.Ingredients.Last().Order.Should().Be(3);
    }

    [Fact]
    public async Task Handle_TemplateNull_ReturnsEmptyList()
    {
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        response.Ingredients.Should().BeEmpty();
    }
}
