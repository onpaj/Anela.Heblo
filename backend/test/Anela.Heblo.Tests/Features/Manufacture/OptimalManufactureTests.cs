using Anela.Heblo.Application.Features.Manufacture.Services;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class OptimalManufactureTests
{
    [Theory]
    [MemberData(nameof(GetTestCases))]
    public void Optimize_ShouldUseAllTheWeight(ProductBatch distribution)
    {
        var calculator = new BatchDistributionCalculator();
        // Act
        calculator.OptimizeBatch(distribution);

        var totalManufacturedWeight = distribution.Variants.Sum(s => s.SuggestedAmount * s.Weight);

        if (totalManufacturedWeight > 0)
            Math.Abs(totalManufacturedWeight - distribution.TotalWeight).Should()
                .BeLessThan(distribution.Variants.DefaultIfEmpty()?.Min(m => m?.Weight ?? 0) ?? 0);
    }

    [Theory]
    [MemberData(nameof(GetTestCases))]
    public void Optimize_ShouldDistribute(ProductBatch distribution)
    {
        var calculator = new BatchDistributionCalculator();
        var comparedVariant = distribution.Variants.Where(w => w.DailySales > 0.5).ToList();

        var deviationBefore = StandardDeviation(comparedVariant.Select(s => s.UpstockTotal));
        // Act
        calculator.OptimizeBatch(distribution, false);

        var deviationAfter = StandardDeviation(comparedVariant.Select(s => s.UpstockTotal));

        deviationAfter.Should().BeLessThanOrEqualTo(deviationBefore);
    }

    [Theory]
    [MemberData(nameof(GetTestCases))]
    public void Optimize_TotalWeightIsTheSameAsInputWeight(ProductBatch distribution)
    {
        var calculator = new BatchDistributionCalculator();
        // Act
        calculator.OptimizeBatch(distribution);

        var maxDeviation = distribution.ValidVariants.DefaultIfEmpty()?.Min(m => m?.Weight ?? 0) ?? 0;
        var residue = distribution.TotalWeight - distribution.ValidVariants.Sum(s => s.Weight * s.SuggestedAmount);

        residue.Should().BeGreaterThanOrEqualTo(0);
        residue.Should().BeLessThan(maxDeviation);
    }

    public static IEnumerable<object[]> GetTestCases()
    {
        yield return new object[]
        {
            new ProductBatch()
            {
                ProductCode = string.Empty,
                ProductName = string.Empty,
                Variants =
                    new List<ProductVariant>
                    {
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 100, DailySales = 3.4, EffectiveStock = 12 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 30, DailySales = 5.2, EffectiveStock = 5 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 5, DailySales = 9.7, EffectiveStock = 49 }
                    },
                TotalWeight = 5000
            }
        };

        yield return new object[]
        {
            new ProductBatch()
            {
                ProductCode = string.Empty,
                ProductName = string.Empty,
                Variants =
                    new List<ProductVariant>
                    {
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 100, DailySales = 20.5, EffectiveStock = 12 },
                    },
                TotalWeight = 5000
            }
        };

        yield return new object[]
        {
            new ProductBatch()
            {
                ProductCode = string.Empty,
                ProductName = string.Empty,
                Variants =
                    new List<ProductVariant>
                    {
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 100, DailySales = 3.4, EffectiveStock = 0 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 30, DailySales = 5.2, EffectiveStock = 0 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 5, DailySales = 9.7, EffectiveStock = 49 }
                    },
                TotalWeight = 5000
            }
        };

        yield return new object[]
        {
            new ProductBatch()
            {
                ProductCode = string.Empty,
                ProductName = string.Empty,
                Variants =
                    new List<ProductVariant>
                    {
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 100, DailySales = 0, EffectiveStock = 0 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 30, DailySales = 5.2, EffectiveStock = 0 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 5, DailySales = 0, EffectiveStock = 49 }
                    },
                TotalWeight = 5000
            }
        };



        yield return new object[]
        {
            new ProductBatch()
            {
                ProductCode = string.Empty,
                ProductName = string.Empty,
                Variants =
                    new List<ProductVariant>
                    {
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 100, DailySales = 3.4, EffectiveStock = 0 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 30, DailySales = 5.2, EffectiveStock = 0 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 5, DailySales = 9.7, EffectiveStock = 49 }
                    },
                TotalWeight = 0
            }
        };

        yield return new object[]
        {
            new ProductBatch()
            {
                ProductCode = string.Empty,
                ProductName = string.Empty,
                Variants =
                    new List<ProductVariant>
                    {
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 500, DailySales = 0.02, EffectiveStock = 0 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 50, DailySales = 30.43, EffectiveStock = 703 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 180, DailySales = 31.08, EffectiveStock = 488 }
                    },
                TotalWeight = 12000
            }
        };

        yield return new object[]
        {
            new ProductBatch()
            {
                ProductCode = string.Empty,
                ProductName = string.Empty,
                Variants =
                    new List<ProductVariant>
                    {
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 500, DailySales = 12.9, EffectiveStock = 869 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 50, DailySales = 30.43, EffectiveStock = 703 },
                        new ProductVariant { ProductCode = string.Empty, ProductName = string.Empty, Weight = 180, DailySales = 31.08, EffectiveStock = 488 }
                    },
                TotalWeight = 12000
            }
        };
    }


    private static double StandardDeviation(IEnumerable<double> values)
    {
        var avg = values.DefaultIfEmpty().Average();
        var variance = values.Select(v => Math.Pow(v - avg, 2)).DefaultIfEmpty().Average();
        return Math.Sqrt(variance);
    }
}