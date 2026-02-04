using System.Reflection;
using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.IssuedOrders;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

public class FlexiManufactureClientTests
{

    [Fact]
    public async Task GetManufactureTemplateAsync_WhenBoMExists_ReturnsCorrectTemplate()
    {
        // Arrange
        var mockBoMClient = new Mock<IBoMClient>();
        var mockProductSetsClient = new Mock<IProductSetsClient>();
        var client = CreateClient(mockBoMClient.Object, mockProductSetsClient.Object);

        var productId = "test-product-id";
        var productCode = "TEST_PRODUCT";
        var productName = "Test Product";
        var amount = 100.0;

        var headerBoM = CreateBoMItem(1, 1, amount, $"code:{productCode}", productName);
        var ingredient1 = CreateBoMItem(2, 2, 5.0, "code:INGREDIENT1", "Ingredient 1");
        var ingredient2 = CreateBoMItem(3, 3, 2.5, "code:INGREDIENT2", "Ingredient 2");

        var bomList = new List<BoMItemFlexiDto> { headerBoM, ingredient1, ingredient2 };

        mockBoMClient.Setup(x => x.GetAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomList);

        // Act
        var result = await client.GetManufactureTemplateAsync(productId);

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

    [Fact]
    public async Task GetManufactureTemplateAsync_WhenNoHeaderFound_ThrowsApplicationException()
    {
        // Arrange
        var mockBoMClient = new Mock<IBoMClient>();
        var mockProductSetsClient = new Mock<IProductSetsClient>();
        var repository = CreateClient(mockBoMClient.Object, mockProductSetsClient.Object);

        var productId = "test-product-id";

        // Only ingredients, no header with Level = 1
        var ingredient1 = CreateBoMItem(1, 2, 5.0, "code:INGREDIENT1", "Ingredient 1");
        var ingredient2 = CreateBoMItem(2, 3, 2.5, "code:INGREDIENT2", "Ingredient 2");

        var bomList = new List<BoMItemFlexiDto> { ingredient1, ingredient2 };

        mockBoMClient.Setup(x => x.GetAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomList);

        // Act & Assert
        var act = async () => await repository.GetManufactureTemplateAsync(productId);

        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage($"No BoM header for product {productId} found");
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_WhenEmptyBoMReturned_ThrowsApplicationException()
    {
        // Arrange
        var mockBoMClient = new Mock<IBoMClient>();
        var mockProductSetsClient = new Mock<IProductSetsClient>();
        var repository = CreateClient(mockBoMClient.Object, mockProductSetsClient.Object);

        var productId = "test-product-id";

        mockBoMClient.Setup(x => x.GetAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoMItemFlexiDto>());

        // Act & Assert
        var act = async () => await repository.GetManufactureTemplateAsync(productId);

        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage($"No BoM header for product {productId} found");
    }

    [Fact]
    public async Task FindByIngredientAsync_ReturnsTemplatesExcludingSameIngredient()
    {
        // Arrange
        var mockBoMClient = new Mock<IBoMClient>();
        var mockProductSetsClient = new Mock<IProductSetsClient>();
        var repository = CreateClient(mockBoMClient.Object, mockProductSetsClient.Object);

        var ingredientCode = "TESTINGREDIENT";
        var cancellationToken = CancellationToken.None;

        var template1 = CreateBoMItem(1, 0, 10.0, "", "", "code:PRODUCT1", "Product 1");
        var template2 = CreateBoMItem(2, 0, 5.0, "", "", "code:PRODUCT2", "Product 2");

        // This should be filtered out as it has the same code as the ingredient
        var template3 = CreateBoMItem(3, 0, 1.0, "", "", $"code:{ingredientCode}", "Same as ingredient");

        var templates = new List<BoMItemFlexiDto> { template1, template2, template3 };

        mockBoMClient.Setup(x => x.GetByIngredientAsync(ingredientCode, cancellationToken))
            .ReturnsAsync(templates);

        // Act
        var result = await repository.FindByIngredientAsync(ingredientCode, cancellationToken);

        // Assert
        result.Should().HaveCount(2);
        result.Should().NotContain(x => x.ProductCode == "TESTINGREDIENT");

        result[0].TemplateId.Should().Be(1);
        result[0].ProductCode.Should().Be("PRODUCT1");
        result[0].ProductName.Should().Be("Product 1");
        result[0].Amount.Should().Be(10.0);

        result[1].TemplateId.Should().Be(2);
        result[1].ProductCode.Should().Be("PRODUCT2");
        result[1].ProductName.Should().Be("Product 2");
        result[1].Amount.Should().Be(5.0);
    }

    [Fact]
    public async Task FindByIngredientAsync_WhenNoTemplatesFound_ReturnsEmptyList()
    {
        // Arrange
        var mockBoMClient = new Mock<IBoMClient>();
        var mockProductSetsClient = new Mock<IProductSetsClient>();
        var repository = CreateClient(mockBoMClient.Object, mockProductSetsClient.Object);

        var ingredientCode = "TESTINGREDIENT";
        var cancellationToken = CancellationToken.None;

        mockBoMClient.Setup(x => x.GetByIngredientAsync(ingredientCode, cancellationToken))
            .ReturnsAsync(new List<BoMItemFlexiDto>());

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
        var mockProductSetsClient = new Mock<IProductSetsClient>();
        var repository = CreateClient(mockBoMClient.Object, mockProductSetsClient.Object);

        var headerBoM = CreateBoMItem(1, 1, 10.0, "code:  PRODUCT_WITH_SPACES  ", "Product With Spaces");
        var ingredient = CreateBoMItem(2, 2, 5.0, "code:INGREDIENT_NO_SPACES", "Ingredient No Spaces");

        var bomList = new List<BoMItemFlexiDto> { headerBoM, ingredient };

        mockBoMClient.Setup(x => x.GetAsync("test-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomList);

        // Act
        var result = await repository.GetManufactureTemplateAsync("test-id");

        // Assert
        result.ProductCode.Should().Be("PRODUCT_WITH_SPACES");
        result.Ingredients[0].ProductCode.Should().Be("INGREDIENT_NO_SPACES");
    }

    private BoMItemFlexiDto CreateBoMItem(int id, int level, double amount, string ingredientCode = "", string ingredientFullName = "", string parentCode = "", string parentFullName = "")
    {
        var item = new BoMItemFlexiDto
        {
            Id = id,
            Level = level,
            Amount = amount
        };

        // Set up ingredient information
        if (!string.IsNullOrEmpty(ingredientCode) || !string.IsNullOrEmpty(ingredientFullName))
        {
            item.Ingredient = new List<BomProductFlexiDto>
            {
                new ()
                {
                    Code = ingredientCode,
                    Name = ingredientFullName
                }
            };
        }

        // Set up parent information for FindByIngredientAsync
        if (!string.IsNullOrEmpty(parentCode) || !string.IsNullOrEmpty(parentFullName))
        {
            item.ParentList = new List<ParentBomFlexiDto>
            {
                new ()
                {
                    Amount = amount,
                    Name = parentFullName
                }
            };

            item.ParentProductList = new List<BomProductFlexiDto>
            {
                new ()
                {
                    Code = parentCode,
                    Name = parentFullName,
                }
            };
        }

        return item;
    }

    private static FlexiManufactureClient CreateClient(IBoMClient bomClient, IProductSetsClient productSetsClient)
    {
        var mockOrdersClient = new Mock<IIssuedOrdersClient>();
        var mockStockClient = new Mock<IErpStockClient>();
        var mockStockMovementClient = new Mock<IStockItemsMovementClient>();
        var mockLogger = new Mock<ILogger<FlexiManufactureClient>>();
        var timeProvider = TimeProvider.System;

        return new FlexiManufactureClient(
            mockOrdersClient.Object,
            mockStockClient.Object,
            mockStockMovementClient.Object,
            bomClient,
            productSetsClient,
            timeProvider,
            mockLogger.Object
        );
    }
}