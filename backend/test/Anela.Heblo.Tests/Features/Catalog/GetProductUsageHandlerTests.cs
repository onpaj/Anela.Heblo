using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetProductUsageHandlerTests
{
    private readonly Mock<IManufactureRepository> _manufactureRepositoryMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly GetProductUsageHandler _handler;

    public GetProductUsageHandlerTests()
    {
        _manufactureRepositoryMock = new Mock<IManufactureRepository>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _handler = new GetProductUsageHandler(_manufactureRepositoryMock.Object, _catalogRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ProductNotFound_ReturnsProductNotFoundError()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "NONEXISTENT" };

        _catalogRepositoryMock.Setup(x => x.SingleOrDefaultAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ProductNotFound);
        result.Params.Should().ContainKey("productCode");
        result.Params!["productCode"].Should().Be("NONEXISTENT");
    }

    [Fact]
    public async Task Handle_NoMmqConfigured_ReturnsUnscaledTemplates()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "PROD001" };
        var catalogItem = CreateCatalogItem("PROD001", mmq: 0); // MMQ not configured
        var originalTemplates = CreateManufactureTemplates();

        _catalogRepositoryMock.Setup(x => x.SingleOrDefaultAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);

        _manufactureRepositoryMock.Setup(x => x.FindByIngredientAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalTemplates);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ManufactureTemplates.Should().HaveCount(1);

        // Templates should remain unscaled
        var template = result.ManufactureTemplates.First();
        template.Amount.Should().Be(2500); // Original amount unchanged
        template.Ingredients.First().Amount.Should().Be(100); // Original ingredient amount unchanged
    }

    [Fact]
    public async Task Handle_MmqConfigured_ScalesTemplatesCorrectly()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "PROD001" };
        var catalogItem = CreateCatalogItem("PROD001", mmq: 5000); // MMQ = 5000g
        var originalTemplates = CreateManufactureTemplates(); // Template base = 2500g

        _catalogRepositoryMock.Setup(x => x.SingleOrDefaultAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);

        _manufactureRepositoryMock.Setup(x => x.FindByIngredientAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalTemplates);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ManufactureTemplates.Should().HaveCount(1);

        var template = result.ManufactureTemplates.First();

        // Scaling factor should be 5000/2500 = 2.0
        var expectedScalingFactor = 2.0;

        // Template amount should be scaled
        template.Amount.Should().Be(2500 * expectedScalingFactor); // 5000
        template.OriginalAmount.Should().Be(2500); // Original amount preserved

        // Ingredient amounts should be scaled
        template.Ingredients.Should().HaveCount(2);
        template.Ingredients[0].Amount.Should().Be(100 * expectedScalingFactor); // 200
        template.Ingredients[0].OriginalAmount.Should().Be(100); // Original amount preserved
        template.Ingredients[0].Price.Should().Be(10m); // Price unchanged

        template.Ingredients[1].Amount.Should().Be(50 * expectedScalingFactor); // 100
        template.Ingredients[1].OriginalAmount.Should().Be(50); // Original amount preserved
        template.Ingredients[1].Price.Should().Be(5m); // Price unchanged
    }

    [Fact]
    public async Task Handle_MmqSmallerThanTemplate_ScalesDownCorrectly()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "PROD001" };
        var catalogItem = CreateCatalogItem("PROD001", mmq: 1250); // MMQ = 1250g (half of template)
        var originalTemplates = CreateManufactureTemplates(); // Template base = 2500g

        _catalogRepositoryMock.Setup(x => x.SingleOrDefaultAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);

        _manufactureRepositoryMock.Setup(x => x.FindByIngredientAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalTemplates);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        var template = result.ManufactureTemplates.First();

        // Scaling factor should be 1250/2500 = 0.5
        var expectedScalingFactor = 0.5;

        // Template amount should be scaled down
        template.Amount.Should().Be(2500 * expectedScalingFactor); // 1250

        // Ingredient amounts should be scaled down
        template.Ingredients[0].Amount.Should().Be(100 * expectedScalingFactor); // 50
        template.Ingredients[1].Amount.Should().Be(50 * expectedScalingFactor); // 25
    }

    [Fact]
    public async Task Handle_InvalidTemplateBaseQuantity_DoesNotScale()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "PROD001" };
        var catalogItem = CreateCatalogItem("PROD001", mmq: 5000);

        // Create template with invalid base quantity (both OriginalAmount and Amount are 0)
        var invalidTemplates = new List<ManufactureTemplate>
        {
            new ManufactureTemplate
            {
                TemplateId = 1,
                ProductCode = "TEMPLATE001",
                ProductName = "Test Template",
                Amount = 0, // Invalid Amount
                OriginalAmount = 0, // Invalid OriginalAmount
                Ingredients = new List<Ingredient>
                {
                    new Ingredient { TemplateId = 1, ProductCode = "ING001", ProductName = "Ingredient 1",
                                   Amount = 10, OriginalAmount = 10, Price = 1m }
                }
            }
        };

        _catalogRepositoryMock.Setup(x => x.SingleOrDefaultAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);

        _manufactureRepositoryMock.Setup(x => x.FindByIngredientAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalidTemplates);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        var template = result.ManufactureTemplates.First();

        // Template should not be scaled due to invalid base quantity
        template.Amount.Should().Be(0); // Original amount unchanged
        template.Ingredients.First().Amount.Should().Be(10); // Original ingredient amount unchanged
    }

    [Fact]
    public async Task Handle_UsesAmountWhenOriginalAmountIsZero()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "PROD001" };
        var catalogItem = CreateCatalogItem("PROD001", mmq: 1000);

        // Create template with OriginalAmount = 0, should fallback to Amount
        var templates = new List<ManufactureTemplate>
        {
            new ManufactureTemplate
            {
                TemplateId = 1,
                ProductCode = "TEMPLATE001",
                ProductName = "Test Template",
                Amount = 500, // This should be used as base quantity
                OriginalAmount = 0, // Will fallback to Amount
                Ingredients = new List<Ingredient>
                {
                    new Ingredient { TemplateId = 1, ProductCode = "ING001", ProductName = "Ingredient 1",
                                   Amount = 25, OriginalAmount = 25, Price = 2m }
                }
            }
        };

        _catalogRepositoryMock.Setup(x => x.SingleOrDefaultAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);

        _manufactureRepositoryMock.Setup(x => x.FindByIngredientAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        var template = result.ManufactureTemplates.First();

        // Scaling factor should be 1000/500 = 2.0 (using Amount as base)
        var expectedScalingFactor = 2.0;

        template.Amount.Should().Be(500 * expectedScalingFactor); // 1000
        template.Ingredients.First().Amount.Should().Be(25 * expectedScalingFactor); // 50
    }

    [Fact]
    public async Task Handle_EmptyTemplatesList_ReturnsSuccessWithEmptyList()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "PROD001" };
        var catalogItem = CreateCatalogItem("PROD001", mmq: 5000);

        _catalogRepositoryMock.Setup(x => x.SingleOrDefaultAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);

        _manufactureRepositoryMock.Setup(x => x.FindByIngredientAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureTemplate>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ManufactureTemplates.Should().BeEmpty();
    }

    private CatalogAggregate CreateCatalogItem(string productCode, double mmq)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = $"Product {productCode}",
            MinimalManufactureQuantity = mmq
        };
    }

    private List<ManufactureTemplate> CreateManufactureTemplates()
    {
        return new List<ManufactureTemplate>
        {
            new ManufactureTemplate
            {
                TemplateId = 1,
                ProductCode = "TEMPLATE001",
                ProductName = "Test Template",
                Amount = 2500, // Template produces 2500g
                OriginalAmount = 2500, // Base quantity for scaling
                Ingredients = new List<Ingredient>
                {
                    new Ingredient
                    {
                        TemplateId = 1,
                        ProductCode = "INGREDIENT001",
                        ProductName = "Ingredient 1",
                        Amount = 100, // 100g needed for template
                        OriginalAmount = 100,
                        Price = 10m
                    },
                    new Ingredient
                    {
                        TemplateId = 1,
                        ProductCode = "INGREDIENT002",
                        ProductName = "Ingredient 2",
                        Amount = 50, // 50g needed for template
                        OriginalAmount = 50,
                        Price = 5m
                    }
                }
            }
        };
    }
}