using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ResidueDistributionCalculatorTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly ResidueDistributionCalculator _calculator;

    private const string SemiProductCode = "SP-001";
    private const string ProductCodeA = "FP-A";
    private const string ProductCodeB = "FP-B";

    public ResidueDistributionCalculatorTests()
    {
        _manufactureClientMock = new Mock<IManufactureClient>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();

        _calculator = new ResidueDistributionCalculator(
            _manufactureClientMock.Object,
            _catalogRepositoryMock.Object);
    }

    [Fact]
    public async Task CalculateAsync_ResidueWithinThreshold_ReturnsWithinThresholdTrue()
    {
        // 30 units × 100 g/unit = 3000 g + 80 units × 10 g/unit = 800 g = 3800 g theoretical
        // Actual = 4000 g → difference = 200 g → 5.26% < 10% threshold
        var order = BuildOrder(
            actualSemiProduct: 4000m,
            products: new[]
            {
                (ProductCodeA, "Product A", pieces: 30m, gramsPerUnit: 100.0),
                (ProductCodeB, "Product B", pieces: 80m, gramsPerUnit: 10.0)
            });

        SetupCatalogWithThreshold(SemiProductCode, allowedResiduePercentage: 10.0);

        var result = await _calculator.CalculateAsync(order);

        result.IsWithinAllowedThreshold.Should().BeTrue();
        result.TheoreticalConsumption.Should().Be(3800m);
        result.ActualSemiProductQuantity.Should().Be(4000m);
        result.Difference.Should().Be(200m);
        result.Products.Should().HaveCount(2);
        result.Products.Sum(p => p.AdjustedConsumption).Should().Be(4000m);

        var productA = result.Products.Single(p => p.ProductCode == ProductCodeA);
        var productB = result.Products.Single(p => p.ProductCode == ProductCodeB);

        // Product A proportion: 3000/3800 ≈ 0.7895 → adjusted ≈ 3157.9g
        productA.AdjustedConsumption.Should().BeApproximately(3157.9m, 0.1m);
        // Product B proportion: 800/3800 ≈ 0.2105 → adjusted ≈ 842.1g
        productB.AdjustedConsumption.Should().BeApproximately(842.1m, 0.1m);
    }

    [Fact]
    public async Task CalculateAsync_ResidueExceedsThreshold_ReturnsWithinThresholdFalse_WithCorrectDistribution()
    {
        // Same as above but threshold is 1% — 5.26% exceeds it
        var order = BuildOrder(
            actualSemiProduct: 4000m,
            products: new[]
            {
                (ProductCodeA, "Product A", pieces: 30m, gramsPerUnit: 100.0),
                (ProductCodeB, "Product B", pieces: 80m, gramsPerUnit: 10.0)
            });

        SetupCatalogWithThreshold(SemiProductCode, allowedResiduePercentage: 1.0);

        var result = await _calculator.CalculateAsync(order);

        result.IsWithinAllowedThreshold.Should().BeFalse();
        result.Products.Sum(p => p.AdjustedConsumption).Should().Be(4000m);
    }

    [Fact]
    public async Task CalculateAsync_DeficitWithinThreshold_ReturnsWithinThresholdTrue()
    {
        // 30 × 100 = 3000 + 120 × 10 = 1200 → 4200 g theoretical, actual = 4000 g
        // Difference = -200 g → 4.76% < 10%
        var order = BuildOrder(
            actualSemiProduct: 4000m,
            products: new[]
            {
                (ProductCodeA, "Product A", pieces: 30m, gramsPerUnit: 100.0),
                (ProductCodeB, "Product B", pieces: 120m, gramsPerUnit: 10.0)
            });

        SetupCatalogWithThreshold(SemiProductCode, allowedResiduePercentage: 10.0);

        var result = await _calculator.CalculateAsync(order);

        result.IsWithinAllowedThreshold.Should().BeTrue();
        result.Difference.Should().Be(-200m);
        result.Products.Sum(p => p.AdjustedConsumption).Should().Be(4000m);
    }

    [Fact]
    public async Task CalculateAsync_DeficitExceedsThreshold_ReturnsWithinThresholdFalse()
    {
        // Same as above but threshold = 1%
        var order = BuildOrder(
            actualSemiProduct: 4000m,
            products: new[]
            {
                (ProductCodeA, "Product A", pieces: 30m, gramsPerUnit: 100.0),
                (ProductCodeB, "Product B", pieces: 120m, gramsPerUnit: 10.0)
            });

        SetupCatalogWithThreshold(SemiProductCode, allowedResiduePercentage: 1.0);

        var result = await _calculator.CalculateAsync(order);

        result.IsWithinAllowedThreshold.Should().BeFalse();
        result.Products.Sum(p => p.AdjustedConsumption).Should().Be(4000m);
    }

    [Fact]
    public async Task CalculateAsync_ExactMatch_DifferencePercentageIsZero_WithinThreshold()
    {
        // actual = 3800 = theoretical exactly
        var order = BuildOrder(
            actualSemiProduct: 3800m,
            products: new[]
            {
                (ProductCodeA, "Product A", pieces: 30m, gramsPerUnit: 100.0),
                (ProductCodeB, "Product B", pieces: 80m, gramsPerUnit: 10.0)
            });

        SetupCatalogWithThreshold(SemiProductCode, allowedResiduePercentage: 0.0);

        var result = await _calculator.CalculateAsync(order);

        result.IsWithinAllowedThreshold.Should().BeTrue();
        result.DifferencePercentage.Should().Be(0);
        result.Difference.Should().Be(0m);
        result.Products.Sum(p => p.AdjustedConsumption).Should().Be(3800m);
    }

    [Fact]
    public async Task CalculateAsync_SingleProduct_GetsFullAllocation()
    {
        var order = BuildOrder(
            actualSemiProduct: 5000m,
            products: new[]
            {
                (ProductCodeA, "Product A", pieces: 50m, gramsPerUnit: 100.0)
            });

        SetupCatalogWithThreshold(SemiProductCode, allowedResiduePercentage: 5.0);

        var result = await _calculator.CalculateAsync(order);

        result.Products.Should().HaveCount(1);
        result.Products[0].AdjustedConsumption.Should().Be(5000m);
        result.Products[0].ProportionRatio.Should().Be(1.0);
    }

    [Fact]
    public async Task CalculateAsync_ProductWithZeroActualQuantity_IsExcludedFromDistribution()
    {
        // ProductB has ActualQuantity = 0 and PlannedQuantity = 0 → excluded
        var order = BuildOrderWithZeroProduct(
            actualSemiProduct: 3000m,
            activeProduct: (ProductCodeA, "Product A", pieces: 30m, gramsPerUnit: 100.0),
            zeroProductCode: ProductCodeB);

        SetupCatalogWithThreshold(SemiProductCode, allowedResiduePercentage: 10.0);

        var result = await _calculator.CalculateAsync(order);

        result.Products.Should().HaveCount(1);
        result.Products[0].ProductCode.Should().Be(ProductCodeA);
        result.Products[0].AdjustedConsumption.Should().Be(3000m);

        // Verify GetManufactureTemplateAsync was NOT called for zero-quantity product
        _manufactureClientMock.Verify(
            x => x.GetManufactureTemplateAsync(ProductCodeB, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CalculateAsync_RoundingRemainder_AdjustedConsumptionsSumExactlyToActual()
    {
        // Use quantities that will produce rounding issues at 4 decimal places
        var order = BuildOrder(
            actualSemiProduct: 1000m,
            products: new[]
            {
                (ProductCodeA, "Product A", pieces: 1m, gramsPerUnit: 1.0),
                (ProductCodeB, "Product B", pieces: 2m, gramsPerUnit: 1.0)
            });

        // theoretical: A=1, B=2, total=3
        // A proportion = 1/3, B proportion = 2/3
        // A adjusted = 1000 * 0.3333 = 333.3... → rounded to 4dp
        // B adjusted = 1000 * 0.6667 = 666.6... → rounded to 4dp
        // sum might not be 1000 without correction

        SetupCatalogWithThreshold(SemiProductCode, allowedResiduePercentage: 100.0);

        var result = await _calculator.CalculateAsync(order);

        result.Products.Sum(p => p.AdjustedConsumption).Should().Be(1000m);
    }

    [Fact]
    public async Task CalculateAsync_SinglePhaseOrder_ReturnsImmediatelyWithEmptyProducts()
    {
        // Single-phase: all products have the same code as the semiproduct
        var order = new UpdateManufactureOrderDto
        {
            SemiProduct = new UpdateManufactureOrderSemiProductDto
            {
                ProductCode = SemiProductCode,
                ProductName = "Semi Product",
                PlannedQuantity = 5000m,
                ActualQuantity = 4800m
            },
            Products = new List<UpdateManufactureOrderProductDto>
            {
                new UpdateManufactureOrderProductDto
                {
                    ProductCode = SemiProductCode, // same code as semiproduct
                    ProductName = "Semi Product",
                    PlannedQuantity = 5000m,
                    ActualQuantity = 4800m,
                    SemiProductCode = SemiProductCode
                }
            }
        };

        var result = await _calculator.CalculateAsync(order);

        result.IsWithinAllowedThreshold.Should().BeTrue();
        result.Products.Should().BeEmpty();

        // Nothing should be called — no templates, no catalog lookup
        _manufactureClientMock.Verify(
            x => x.GetManufactureTemplateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _catalogRepositoryMock.Verify(
            x => x.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CalculateAsync_AdjustedGramsPerUnit_IsAdjustedConsumptionDividedByPieces()
    {
        var order = BuildOrder(
            actualSemiProduct: 4000m,
            products: new[]
            {
                (ProductCodeA, "Product A", pieces: 40m, gramsPerUnit: 100.0)
            });

        SetupCatalogWithThreshold(SemiProductCode, allowedResiduePercentage: 10.0);

        var result = await _calculator.CalculateAsync(order);

        var productA = result.Products[0];
        productA.AdjustedGramsPerUnit.Should().Be(productA.AdjustedConsumption / productA.ActualPieces);
    }

    // ---- Helpers ----

    private UpdateManufactureOrderDto BuildOrder(
        decimal actualSemiProduct,
        (string code, string name, decimal pieces, double gramsPerUnit)[] products)
    {
        SetupTemplatesForProducts(products);

        var productDtos = products.Select(p => new UpdateManufactureOrderProductDto
        {
            ProductCode = p.code,
            ProductName = p.name,
            SemiProductCode = SemiProductCode,
            PlannedQuantity = p.pieces,
            ActualQuantity = p.pieces
        }).ToList();

        return new UpdateManufactureOrderDto
        {
            SemiProduct = new UpdateManufactureOrderSemiProductDto
            {
                ProductCode = SemiProductCode,
                ProductName = "Semi Product",
                PlannedQuantity = actualSemiProduct,
                ActualQuantity = actualSemiProduct
            },
            Products = productDtos
        };
    }

    private UpdateManufactureOrderDto BuildOrderWithZeroProduct(
        decimal actualSemiProduct,
        (string code, string name, decimal pieces, double gramsPerUnit) activeProduct,
        string zeroProductCode)
    {
        SetupTemplatesForProducts(new[] { activeProduct });

        return new UpdateManufactureOrderDto
        {
            SemiProduct = new UpdateManufactureOrderSemiProductDto
            {
                ProductCode = SemiProductCode,
                ProductName = "Semi Product",
                PlannedQuantity = actualSemiProduct,
                ActualQuantity = actualSemiProduct
            },
            Products = new List<UpdateManufactureOrderProductDto>
            {
                new UpdateManufactureOrderProductDto
                {
                    ProductCode = activeProduct.code,
                    ProductName = activeProduct.name,
                    SemiProductCode = SemiProductCode,
                    PlannedQuantity = activeProduct.pieces,
                    ActualQuantity = activeProduct.pieces
                },
                new UpdateManufactureOrderProductDto
                {
                    ProductCode = zeroProductCode,
                    ProductName = "Zero Product",
                    SemiProductCode = SemiProductCode,
                    PlannedQuantity = 0m,
                    ActualQuantity = 0m
                }
            }
        };
    }

    private void SetupTemplatesForProducts(
        (string code, string name, decimal pieces, double gramsPerUnit)[] products)
    {
        foreach (var (code, _, _, gramsPerUnit) in products)
        {
            var template = new ManufactureTemplate
            {
                ProductCode = code,
                ProductName = code,
                Ingredients = new List<Ingredient>
                {
                    new Ingredient
                    {
                        ProductCode = SemiProductCode,
                        ProductName = "Semi Product",
                        Amount = gramsPerUnit,
                        ProductType = ProductType.SemiProduct
                    }
                }
            };

            _manufactureClientMock
                .Setup(x => x.GetManufactureTemplateAsync(code, It.IsAny<CancellationToken>()))
                .ReturnsAsync(template);
        }
    }

    private void SetupCatalogWithThreshold(string productCode, double allowedResiduePercentage)
    {
        var catalogItem = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Semi Product",
            Properties = new CatalogProperties
            {
                AllowedResiduePercentage = allowedResiduePercentage
            }
        };

        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);
    }
}
