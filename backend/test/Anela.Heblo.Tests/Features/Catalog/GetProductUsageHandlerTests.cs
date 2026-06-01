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
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly GetProductUsageHandler _handler;

    public GetProductUsageHandlerTests()
    {
        _manufactureClientMock = new Mock<IManufactureClient>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _handler = new GetProductUsageHandler(_manufactureClientMock.Object, _catalogRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_NoTemplatesFound_ReturnsEmptyList()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "NONEXISTENT" };

        _manufactureClientMock.Setup(x => x.FindByIngredientAsync("NONEXISTENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureTemplate>());

        _catalogRepositoryMock.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ManufactureTemplates.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoMmqConfigured_ReturnsUnscaledTemplates()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "INGREDIENT001" };
        var originalTemplates = CreateManufactureTemplates();

        _manufactureClientMock.Setup(x => x.FindByIngredientAsync("INGREDIENT001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalTemplates);

        // Template product has no MMQ configured
        _catalogRepositoryMock.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>
            {
                ["TEMPLATE001"] = CreateCatalogItem("TEMPLATE001", mmq: 0)
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ManufactureTemplates.Should().HaveCount(1);

        // Templates should remain unscaled when MMQ is 0
        var template = result.ManufactureTemplates.First();
        template.Amount.Should().Be(2500); // Original amount unchanged
        template.OriginalAmount.Should().Be(2500); // Set from original Amount
    }

    [Fact]
    public async Task Handle_MmqConfigured_ScalesTemplatesCorrectly()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "INGREDIENT001" };
        var originalTemplates = CreateManufactureTemplates();

        _manufactureClientMock.Setup(x => x.FindByIngredientAsync("INGREDIENT001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalTemplates);

        // Template product with MMQ configured
        _catalogRepositoryMock.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>
            {
                ["TEMPLATE001"] = CreateCatalogItem("TEMPLATE001", mmq: 5000) // MMQ = 5000g
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ManufactureTemplates.Should().HaveCount(1);

        var template = result.ManufactureTemplates.First();

        // Handler scaling logic: scalingFactor = MMQ / BatchSize = 5000 / 2500 = 2.0
        // template.Amount = OriginalAmount * scalingFactor = 2500 * 2.0 = 5000

        template.Amount.Should().Be(5000); // 2500 * 2.0
        template.OriginalAmount.Should().Be(2500); // Set from original Amount
        template.BatchSize.Should().Be(5000); // Updated to MMQ
    }

    [Fact]
    public async Task Handle_MmqSmallerThanTemplate_ScalesDownCorrectly()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "INGREDIENT001" };
        var originalTemplates = CreateManufactureTemplates();

        _manufactureClientMock.Setup(x => x.FindByIngredientAsync("INGREDIENT001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalTemplates);

        // Template product with smaller MMQ
        _catalogRepositoryMock.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>
            {
                ["TEMPLATE001"] = CreateCatalogItem("TEMPLATE001", mmq: 1250) // MMQ = 1250g (half of BatchSize)
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        var template = result.ManufactureTemplates.First();

        // Handler scaling logic: scalingFactor = MMQ / BatchSize = 1250 / 2500 = 0.5
        // template.Amount = OriginalAmount * scalingFactor = 2500 * 0.5 = 1250

        template.Amount.Should().Be(1250); // 2500 * 0.5
        template.OriginalAmount.Should().Be(2500); // Set from original Amount
        template.BatchSize.Should().Be(1250); // Updated to MMQ
    }

    [Fact]
    public async Task Handle_InvalidTemplateBaseQuantity_DoesNotScale()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "INGREDIENT001" };

        // Create template with invalid OriginalAmount (will be set to 0, then checked)
        var invalidTemplates = new List<ManufactureTemplate>
        {
            new ManufactureTemplate
            {
                TemplateId = 1,
                ProductCode = "TEMPLATE001",
                ProductName = "Test Template",
                Amount = 0, // This will become OriginalAmount
                OriginalAmount = 0, // Will be set from Amount
                BatchSize = 2500,
                Ingredients = new List<Ingredient>()
            }
        };

        _manufactureClientMock.Setup(x => x.FindByIngredientAsync("INGREDIENT001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalidTemplates);

        // Template product with MMQ configured
        _catalogRepositoryMock.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>
            {
                ["TEMPLATE001"] = CreateCatalogItem("TEMPLATE001", mmq: 5000)
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        var template = result.ManufactureTemplates.First();

        // Template should not be scaled due to invalid OriginalAmount (0)
        template.Amount.Should().Be(0); // Original amount unchanged
        template.OriginalAmount.Should().Be(0); // Set from Amount
        template.BatchSize.Should().Be(2500); // Not updated due to no scaling
    }

    [Fact]
    public async Task Handle_UsesAmountWhenOriginalAmountIsZero()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "INGREDIENT001" };

        // Create template with OriginalAmount = 0, should fallback to Amount
        var templates = new List<ManufactureTemplate>
        {
            new ManufactureTemplate
            {
                TemplateId = 1,
                ProductCode = "TEMPLATE001",
                ProductName = "Test Template",
                Amount = 500, // This should be used as OriginalAmount
                OriginalAmount = 0, // Will fallback to Amount
                BatchSize = 500,
                Ingredients = new List<Ingredient>()
            }
        };

        _manufactureClientMock.Setup(x => x.FindByIngredientAsync("INGREDIENT001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        // Template product with MMQ configured
        _catalogRepositoryMock.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>
            {
                ["TEMPLATE001"] = CreateCatalogItem("TEMPLATE001", mmq: 1000)
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        var template = result.ManufactureTemplates.First();

        // Scaling factor should be MMQ / BatchSize = 1000 / 500 = 2.0
        var expectedScalingFactor = 2.0;

        template.Amount.Should().Be(500 * expectedScalingFactor); // 1000 (OriginalAmount * scalingFactor)
        template.OriginalAmount.Should().Be(500); // Set from Amount
        template.BatchSize.Should().Be(1000); // Updated to MMQ
    }

    [Fact]
    public async Task Handle_EmptyTemplatesList_ReturnsSuccessWithEmptyList()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "INGREDIENT001" };

        _manufactureClientMock.Setup(x => x.FindByIngredientAsync("INGREDIENT001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureTemplate>());

        _catalogRepositoryMock.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ManufactureTemplates.Should().BeEmpty();
    }

    /// <summary>
    /// Regression test for N+1 issue: verifies GetByIdsAsync is called exactly once
    /// regardless of how many manufacture templates are returned.
    /// </summary>
    [Fact]
    public async Task Handle_MultipleTemplates_MakesSingleBulkFetch()
    {
        // Arrange
        var request = new GetProductUsageRequest { ProductCode = "INGREDIENT001" };

        var templates = new List<ManufactureTemplate>
        {
            new ManufactureTemplate { TemplateId = 1, ProductCode = "PROD001", Amount = 1000, OriginalAmount = 1000, BatchSize = 1000, Ingredients = new List<Ingredient>() },
            new ManufactureTemplate { TemplateId = 2, ProductCode = "PROD002", Amount = 2000, OriginalAmount = 2000, BatchSize = 2000, Ingredients = new List<Ingredient>() },
            new ManufactureTemplate { TemplateId = 3, ProductCode = "PROD003", Amount = 3000, OriginalAmount = 3000, BatchSize = 3000, Ingredients = new List<Ingredient>() },
        };

        _manufactureClientMock.Setup(x => x.FindByIngredientAsync("INGREDIENT001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        _catalogRepositoryMock.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>
            {
                ["PROD001"] = CreateCatalogItem("PROD001", mmq: 2000),
                ["PROD002"] = CreateCatalogItem("PROD002", mmq: 4000),
                ["PROD003"] = CreateCatalogItem("PROD003", mmq: 6000),
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert — bulk fetch called exactly once (not once per template)
        _catalogRepositoryMock.Verify(
            x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "GetByIdsAsync should be called exactly once to avoid N+1 DB queries");

        result.ManufactureTemplates.Should().HaveCount(3);
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
                BatchSize = 2500, // Template batch size
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
