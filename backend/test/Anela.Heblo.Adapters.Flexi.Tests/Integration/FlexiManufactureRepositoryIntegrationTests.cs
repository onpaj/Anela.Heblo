using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;

namespace Anela.Heblo.Adapters.Flexi.Tests.Integration;

[Collection("FlexiIntegration")]
public class FlexiManufactureRepositoryIntegrationTests : IClassFixture<FlexiIntegrationTestFixture>
{
    private readonly FlexiIntegrationTestFixture _fixture;
    private readonly IBoMClient _bomClient;
    private readonly FlexiManufactureRepository _repository;

    public FlexiManufactureRepositoryIntegrationTests(FlexiIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _bomClient = _fixture.ServiceProvider.GetRequiredService<IBoMClient>();
        _repository = new FlexiManufactureRepository(_bomClient);
    }

    [Theory]
    [InlineData("KRE003030")]
    [InlineData("KRE003001M")]
    public async Task GetManufactureTemplateAsync_WithRealFlexiConnection_ReturnsManufactureTemplate(string productCode)
    {
        // Arrange
        // Act
        var result = await _repository.GetManufactureTemplateAsync(productCode);

        // Assert
        result.Should().NotBeNull();
        result.TemplateId.Should().NotBe(0);
        result.ProductCode.Should().NotBeNullOrWhiteSpace();
        result.ProductName.Should().NotBeNullOrWhiteSpace();
        result.Amount.Should().BeGreaterThan(0);
        result.Ingredients.Should().NotBeNull();
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_WithNonExistentProduct_ThrowsException()
    {
        // Arrange
        const string nonExistentProductId = "NON_EXISTENT_PRODUCT_12345";

        // Act
        var act = async () => await _repository.GetManufactureTemplateAsync(nonExistentProductId);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task FindByIngredientAsync_WithRealFlexiConnection_ReturnsManufactureTemplates()
    {
        // Arrange
        // You need to provide a valid ingredient code that exists in your FlexiBee system
        const string ingredientCode = "HYD007"; // Replace with actual ingredient code

        // Act
        var result = await _repository.FindByIngredientAsync(ingredientCode, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<List<Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate>>();

        if (result.Any())
        {
            result.Should().OnlyContain(template => template.ProductCode != ingredientCode);
            result.Should().OnlyContain(template => !string.IsNullOrWhiteSpace(template.ProductCode));
            result.Should().OnlyContain(template => !string.IsNullOrWhiteSpace(template.ProductName));
            result.Should().OnlyContain(template => template.Amount > 0);
        }
    }

    [Fact]
    public async Task FindByIngredientAsync_WithNonExistentIngredient_ReturnsEmptyList()
    {
        // Arrange
        const string nonExistentIngredientCode = "NON_EXISTENT_INGREDIENT_12345";

        // Act
        var result = await _repository.FindByIngredientAsync(nonExistentIngredientCode, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("KRE003030")]
    [InlineData("KRE003001M")]
    public async Task Integration_FullWorkflow_TestWithRealData(string productCode)
    {
        // This test demonstrates a full workflow with real FlexiBee data
        // You should replace the IDs with actual values from your FlexiBee system

        // Step 1: Get a manufacture template for a known product
        var template = await _repository.GetManufactureTemplateAsync(productCode);

        template.Should().NotBeNull();
        template.Ingredients.Should().NotBeEmpty();

        // Step 2: For each ingredient, find where else it's used
        foreach (var ingredient in template.Ingredients.Take(3)) // Test first 3 ingredients
        {
            var usageTemplates = await _repository.FindByIngredientAsync(ingredient.ProductCode, CancellationToken.None);

            // The ingredient might be used in other products
            usageTemplates.Should().NotBeNull();

            // Verify the original product is not in the results
            usageTemplates.Should().Contain(t => t.ProductCode == template.ProductCode);
        }
    }
}