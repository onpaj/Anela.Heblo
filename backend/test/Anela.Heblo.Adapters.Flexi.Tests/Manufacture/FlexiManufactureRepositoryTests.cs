using Anela.Heblo.Adapters.Flexi.Manufacture;
using AutoFixture;
using AutoFixture.Xunit2;
using FluentAssertions;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Model;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

public class FlexiManufactureRepositoryTests
{
    private readonly Fixture _fixture = new();

    [Theory]
    [AutoData]
    public async Task GetManufactureTemplateAsync_WhenBoMExists_ReturnsCorrectTemplate(
        string productId,
        string productCode,
        string productName,
        double amount)
    {
        // Arrange
        var mockBoMClient = new Mock<IBoMClient>();
        var repository = new FlexiManufactureRepository(mockBoMClient.Object);

        var headerBoM = new BoMItemV2
        {
            Id = 1,
            Level = 1,
            IngredientCode = $"code:{productCode}",
            IngredientFullName = productName,
            Amount = amount
        };

        var ingredient1 = new BoMItemV2
        {
            Id = 2,
            Level = 2,
            IngredientCode = "code:INGREDIENT1",
            IngredientFullName = "Ingredient 1",
            Amount = 5.0
        };

        var ingredient2 = new BoMItemV2
        {
            Id = 3,
            Level = 3,
            IngredientCode = "code:INGREDIENT2",
            IngredientFullName = "Ingredient 2",
            Amount = 2.5
        };

        var bomList = new List<BoMItemV2> { headerBoM, ingredient1, ingredient2 };

        mockBoMClient.Setup(x => x.GetAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomList);

        // Act
        var result = await repository.GetManufactureTemplateAsync(productId);

        // Assert
        result.Should().NotBeNull();
        result.TemplateId.Should().Be(1);
        result.ProductCode.Should().Be(productCode);
        result.ProductName.Should().Be(productName);
        result.Amount.Should().Be(amount);
        result.Ingredients.Should().HaveCount(2);

        result.Ingredients[0].TemplateId.Should().Be(2);
        result.Ingredients[0].ProductCode.Should().Be("INGREDIENT1");
        result.Ingredients[0].ProductName.Should().Be("Ingredient 1");
        result.Ingredients[0].Amount.Should().Be(5.0);

        result.Ingredients[1].TemplateId.Should().Be(3);
        result.Ingredients[1].ProductCode.Should().Be("INGREDIENT2");
        result.Ingredients[1].ProductName.Should().Be("Ingredient 2");
        result.Ingredients[1].Amount.Should().Be(2.5);
    }

    [Theory]
    [AutoData]
    public async Task GetManufactureTemplateAsync_WhenNoHeaderFound_ThrowsApplicationException(string productId)
    {
        // Arrange
        var mockBoMClient = new Mock<IBoMClient>();
        var repository = new FlexiManufactureRepository(mockBoMClient.Object);

        // Only ingredients, no header with Level = 1
        var bomList = new List<BoMItemV2>
        {
            new BoMItemV2 { Id = 1, Level = 2, IngredientCode = "code:INGREDIENT1", IngredientFullName = "Ingredient 1", Amount = 5.0 },
            new BoMItemV2 { Id = 2, Level = 3, IngredientCode = "code:INGREDIENT2", IngredientFullName = "Ingredient 2", Amount = 2.5 }
        };

        mockBoMClient.Setup(x => x.GetAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomList);

        // Act & Assert
        var act = async () => await repository.GetManufactureTemplateAsync(productId);

        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage($"No BoM header for product {productId} found");
    }

    [Theory]
    [AutoData]
    public async Task GetManufactureTemplateAsync_WhenEmptyBoMReturned_ThrowsApplicationException(string productId)
    {
        // Arrange
        var mockBoMClient = new Mock<IBoMClient>();
        var repository = new FlexiManufactureRepository(mockBoMClient.Object);

        mockBoMClient.Setup(x => x.GetAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoMItemV2>());

        // Act & Assert
        var act = async () => await repository.GetManufactureTemplateAsync(productId);

        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage($"No BoM header for product {productId} found");
    }

    [Theory]
    [AutoData]
    public async Task FindByIngredientAsync_ReturnsTemplatesExcludingSameIngredient(
        string ingredientCode,
        CancellationToken cancellationToken)
    {
        // Arrange
        var mockBoMClient = new Mock<IBoMClient>();
        var repository = new FlexiManufactureRepository(mockBoMClient.Object);

        var templates = new List<BoMItemV2>
        {
            new BoMItemV2
            {
                Id = 1,
                ParentCode = "code:PRODUCT1",
                ParentFullName = "Product 1",
                Amount = 10.0
            },
            new BoMItemV2
            {
                Id = 2,
                ParentCode = "code:PRODUCT2",
                ParentFullName = "Product 2",
                Amount = 5.0
            },
            // This should be filtered out as it has the same code as the ingredient
            new BoMItemV2
            {
                Id = 3,
                ParentCode = $"code:{ingredientCode}",
                ParentFullName = "Same as ingredient",
                Amount = 1.0
            }
        };

        mockBoMClient.Setup(x => x.GetByIngredientAsync(ingredientCode, cancellationToken))
            .ReturnsAsync(templates);

        // Act
        var result = await repository.FindByIngredientAsync(ingredientCode, cancellationToken);

        // Assert
        result.Should().HaveCount(2);
        result.Should().NotContain(x => x.ProductCode == ingredientCode);

        result[0].TemplateId.Should().Be(1);
        result[0].ProductCode.Should().Be("PRODUCT1");
        result[0].ProductName.Should().Be("Product 1");
        result[0].Amount.Should().Be(10.0);

        result[1].TemplateId.Should().Be(2);
        result[1].ProductCode.Should().Be("PRODUCT2");
        result[1].ProductName.Should().Be("Product 2");
        result[1].Amount.Should().Be(5.0);
    }

    [Theory]
    [AutoData]
    public async Task FindByIngredientAsync_WhenNoTemplatesFound_ReturnsEmptyList(
        string ingredientCode,
        CancellationToken cancellationToken)
    {
        // Arrange
        var mockBoMClient = new Mock<IBoMClient>();
        var repository = new FlexiManufactureRepository(mockBoMClient.Object);

        mockBoMClient.Setup(x => x.GetByIngredientAsync(ingredientCode, cancellationToken))
            .ReturnsAsync(new List<BoMItemV2>());

        // Act
        var result = await repository.FindByIngredientAsync(ingredientCode, cancellationToken);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_HandlesCodePrefixCorrectly()
    {
        // Arrange
        var mockBoMClient = new Mock<IBoMClient>();
        var repository = new FlexiManufactureRepository(mockBoMClient.Object);

        var bomList = new List<BoMItemV2>
        {
            new BoMItemV2
            {
                Id = 1,
                Level = 1,
                IngredientCode = "code:  PRODUCT_WITH_SPACES  ",
                IngredientFullName = "Product With Spaces",
                Amount = 10.0
            },
            new BoMItemV2
            {
                Id = 2,
                Level = 2,
                IngredientCode = "code:INGREDIENT_NO_SPACES",
                IngredientFullName = "Ingredient No Spaces",
                Amount = 5.0
            }
        };

        mockBoMClient.Setup(x => x.GetAsync("test-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomList);

        // Act
        var result = await repository.GetManufactureTemplateAsync("test-id");

        // Assert
        result.ProductCode.Should().Be("PRODUCT_WITH_SPACES");
        result.Ingredients[0].ProductCode.Should().Be("INGREDIENT_NO_SPACES");
    }
}