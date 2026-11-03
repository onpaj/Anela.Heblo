using Anela.Heblo.Application.Features.Manufacture.Services;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class BatchDistributionCalculatorCoverageWeightTests
{
    private static ProductBatch CreateBatch(double coverageWeightOfNd)
    {
        return new ProductBatch
        {
            ProductCode = "SEMI",
            ProductName = "Semi",
            TotalWeight = 10_000,
            BatchSize = 10_000,
            BatchCount = 1,
            Variants = new List<ProductVariant>
            {
                new() { ProductCode = "ND30", ProductName = "ND 30ml", Weight = 40, DailySales = 2.0, EffectiveStock = 100, CoverageWeight = coverageWeightOfNd },
                new() { ProductCode = "N5", ProductName = "Regular 5ml", Weight = 8, DailySales = 1.5, EffectiveStock = 80 },
                new() { ProductCode = "ND50", ProductName = "ND 50ml", Weight = 70, DailySales = 1.0, EffectiveStock = 30, CoverageWeight = coverageWeightOfNd },
            }
        };
    }

    [Fact]
    public void OptimizeBatch_DefaultCoverageWeight_BehavesLikeEqualCoverage()
    {
        var batch = CreateBatch(coverageWeightOfNd: 1.0);

        new BatchDistributionCalculator().OptimizeBatch(batch, minimizeResidue: false);

        var coverages = batch.Variants.Select(v => v.UpstockTotal).ToList();
        (coverages.Max() - coverages.Min()).Should().BeLessThan(2.0);
    }

    [Fact]
    public void OptimizeBatch_WeightedVariants_EndWithProportionallyMoreDaysOfStock()
    {
        var batch = CreateBatch(coverageWeightOfNd: 1.2);

        new BatchDistributionCalculator().OptimizeBatch(batch, minimizeResidue: false);

        var regular = batch.Variants.Single(v => v.ProductCode == "N5");
        foreach (var nd in batch.Variants.Where(v => v.CoverageWeight > 1.0))
        {
            (nd.UpstockTotal / regular.UpstockTotal).Should().BeApproximately(1.2, 0.05);
        }
    }

    [Fact]
    public void OptimizeBatch_WeightedVariants_StillRespectTotalWeight()
    {
        var batch = CreateBatch(coverageWeightOfNd: 1.2);

        new BatchDistributionCalculator().OptimizeBatch(batch);

        var used = batch.Variants.Sum(v => v.SuggestedAmount * v.Weight);
        used.Should().BeLessThanOrEqualTo(batch.TotalWeight);
    }

    [Fact]
    public void OptimizeBatch_WeightedVariants_TakeVolumeFromRegularVariants()
    {
        var baseline = CreateBatch(coverageWeightOfNd: 1.0);
        var weighted = CreateBatch(coverageWeightOfNd: 1.2);
        var calculator = new BatchDistributionCalculator();

        calculator.OptimizeBatch(baseline, minimizeResidue: false);
        calculator.OptimizeBatch(weighted, minimizeResidue: false);

        weighted.Variants.Single(v => v.ProductCode == "ND30").SuggestedAmount
            .Should().BeGreaterThan(baseline.Variants.Single(v => v.ProductCode == "ND30").SuggestedAmount);
        weighted.Variants.Single(v => v.ProductCode == "N5").SuggestedAmount
            .Should().BeLessThan(baseline.Variants.Single(v => v.ProductCode == "N5").SuggestedAmount);
    }
}
