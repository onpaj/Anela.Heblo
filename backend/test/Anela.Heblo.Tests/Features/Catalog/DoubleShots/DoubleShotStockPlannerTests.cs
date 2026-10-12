using Anela.Heblo.Application.Features.Catalog.DoubleShots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.DoubleShots;

public class DoubleShotStockPlannerTests
{
    private static EshopStock Row(string code, decimal stock) => new() { Code = code, Stock = stock };

    [Fact]
    public void Plan_EmitsAdjustment_WhenVariantDiffersFromMirror()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 0), Row("SA015030S", 213)]);

        plan.PairCount.Should().Be(1);
        plan.Adjustments.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { VariantCode = "SA015030V", SetCode = "SA015030S", Current = 0m, Target = 213m, Delta = 213m });
    }

    [Fact]
    public void Plan_EmitsNoAdjustment_WhenAlreadyEqual()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 213), Row("SA015030S", 213)]);

        plan.PairCount.Should().Be(1);
        plan.Adjustments.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ClampsNegativeMirrorToZero()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 5), Row("SA015030S", -3)]);

        plan.Adjustments.Should().ContainSingle().Which.Target.Should().Be(0m);
        plan.Adjustments[0].Delta.Should().Be(-5m);
    }

    [Fact]
    public void Plan_LowersVariant_WhenMirrorDropped()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA014005V", 400), Row("SA014005S", 396)]);

        plan.Adjustments.Should().ContainSingle().Which.Delta.Should().Be(-4m);
    }

    [Fact]
    public void Plan_RaisesNegativeVariant_ToMirror()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA014005V", -2), Row("SA014005S", 10)]);

        plan.Adjustments.Should().ContainSingle().Which.Delta.Should().Be(12m);
    }

    [Fact]
    public void Plan_ReportsUnpairedVariant_AndDoesNotAdjustIt()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA099030V", 7)]);

        plan.PairCount.Should().Be(0);
        plan.Adjustments.Should().BeEmpty();
        plan.UnpairedVariantCodes.Should().Equal("SA099030V");
    }

    [Fact]
    public void Plan_IgnoresNonDoubleShotCodes()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("BAL0001V", 1000), Row("BAL0001S", 3), Row("SA001", 28), Row("SA015030S", 213)]);

        plan.PairCount.Should().Be(0);
        plan.Adjustments.Should().BeEmpty();
        plan.UnpairedVariantCodes.Should().BeEmpty();
    }

    [Fact]
    public void Plan_NeverTargetsSetCodes()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 0), Row("SA015030S", 213), Row("SA016005V", 1), Row("SA016005S", 385)]);

        plan.Adjustments.Select(a => a.VariantCode).Should().OnlyContain(c => c.EndsWith('V'));
    }

    [Fact]
    public void Plan_ToleratesDuplicateCodes_FirstRowWins()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 0), Row("SA015030S", 213), Row("SA015030S", 1), Row("SA015030V", 99)]);

        plan.PairCount.Should().Be(1);
        plan.Adjustments.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Current = 0m, Target = 213m });
    }

    [Fact]
    public void Plan_KeepsFractionalStockExact()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 0.5m), Row("SA015030S", 2.25m)]);

        plan.Adjustments.Should().ContainSingle().Which.Delta.Should().Be(1.75m);
    }

    [Fact]
    public void Plan_ReturnsEmpty_ForEmptyInput()
    {
        var plan = DoubleShotStockPlanner.Plan([]);

        plan.PairCount.Should().Be(0);
        plan.Adjustments.Should().BeEmpty();
        plan.UnpairedVariantCodes.Should().BeEmpty();
    }
}
