using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetProductCompositionHandlerTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock = new();
    private readonly GetProductCompositionHandler _handler;

    public GetProductCompositionHandlerTests()
    {
        _handler = new GetProductCompositionHandler(_manufactureClientMock.Object);
    }

    private static ManufactureTemplate BuildTemplate(params (string Code, string Name, int Order)[] ingredients)
    {
        return new ManufactureTemplate
        {
            ProductCode = "PRD1",
            Ingredients = ingredients.Select(i => new Ingredient
            {
                ProductCode = i.Code,
                ProductName = i.Name,
                Amount = 10,
                Order = i.Order
            }).ToList()
        };
    }

    [Fact]
    public async Task Handle_SortsByIngredientOrder_Ascending()
    {
        // Arrange
        var template = BuildTemplate(
            ("A", "Alpha", 2),
            ("B", "Beta", 1),
            ("C", "Gamma", 3));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Select(i => i.ProductCode).Should().Equal("B", "A", "C");
        response.Ingredients.Select(i => i.Order).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Handle_ZeroOrderItems_AppearLastSortedByName()
    {
        // Arrange — Flexi returns 0 for unordered items
        var template = BuildTemplate(
            ("A", "Alpha", 2),
            ("B", "Unnamed", 0),
            ("C", "Zebra", 0),
            ("D", "Delta", 1));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert: ordered items first (1, 2), then unordered by name (Unnamed before Zebra alphabetically)
        response.Ingredients.Select(i => i.ProductCode).Should().Equal("D", "A", "B", "C");
        response.Ingredients.Select(i => i.Order).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task Handle_TemplateNull_ReturnsEmptyList()
    {
        // Arrange
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AssignsContiguous1BasedDisplayOrder()
    {
        // Arrange
        var template = BuildTemplate(
            ("A", "Alpha", 1),
            ("B", "Beta", 2));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Select(i => i.Order).Should().Equal(1, 2);
    }
}
