using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetProductCompositionHandlerTests
{
    private readonly Mock<IManufactureRepository> _manufactureRepositoryMock;
    private readonly GetProductCompositionHandler _handler;

    public GetProductCompositionHandlerTests()
    {
        _manufactureRepositoryMock = new Mock<IManufactureRepository>();
        _handler = new GetProductCompositionHandler(_manufactureRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_NoManufactureTemplate_ReturnsEmptyIngredientList()
    {
        // Arrange
        var request = new GetProductCompositionRequest { ProductCode = "NONEXISTENT" };

        _manufactureRepositoryMock
            .Setup(x => x.GetManufactureTemplateAsync("NONEXISTENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Ingredients.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ValidTemplate_ReturnsMappedIngredients()
    {
        // Arrange
        var request = new GetProductCompositionRequest { ProductCode = "PROD001" };

        var template = new ManufactureTemplate
        {
            TemplateId = 1,
            ProductCode = "PROD001",
            ProductName = "Test Product",
            Ingredients = new List<Ingredient>
            {
                new Ingredient
                {
                    ProductCode = "ING001",
                    ProductName = "Ingredient 1",
                    Amount = 50.5,
                    OriginalAmount = 50.5
                },
                new Ingredient
                {
                    ProductCode = "ING002",
                    ProductName = "Ingredient 2",
                    Amount = 100.25,
                    OriginalAmount = 100.25
                }
            }
        };

        _manufactureRepositoryMock
            .Setup(x => x.GetManufactureTemplateAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        result.Ingredients[0].ProductCode.Should().Be("ING001");
        result.Ingredients[0].ProductName.Should().Be("Ingredient 1");
        result.Ingredients[0].Amount.Should().Be(50.5);
        result.Ingredients[0].Unit.Should().Be("g");
        result.Ingredients[1].ProductCode.Should().Be("ING002");
    }
}
