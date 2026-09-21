using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

public class PricingSimulationCalculatorTests
{
    private readonly PricingSimulationCalculator _sut = new();

    // Price 420, material 175, manufacturing 70, sold 1000.
    // => M0 = 245 (58.33%), M1 = 175 (41.67%)
    private static PricingBaselineRow Row(
        string code = "P1", decimal price = 420m, decimal material = 175m,
        decimal manufacturing = 70m, double quantity = 1000d, bool hasData = true)
        => new(code, $"Product {code}", price, material, manufacturing, quantity, hasData);

    private static List<PricingOverrideDto> NoOverrides() => new();

    private PricingRowDto Single(PricingBaselineRow baseline, PricingEditDto? edit,
        IReadOnlyList<PricingOverrideDto>? overrides = null)
        => _sut.Calculate(new[] { baseline }, overrides ?? NoOverrides(), edit).Rows.Single();

    [Fact]
    public void Baseline_with_no_edits_derives_M0_and_M1_from_costs()
    {
        var row = Single(Row(), edit: null);

        row.M0Amount.Should().Be(245m);
        row.M1Amount.Should().Be(175m);
        row.M0Percentage.Should().BeApproximately(58.33m, 0.01m);
        row.M1Percentage.Should().BeApproximately(41.67m, 0.01m);
        row.BaselineM0Amount.Should().Be(245m);
        row.BaselineM1Amount.Should().Be(175m);
        row.IsEdited.Should().BeFalse();
    }

    [Fact]
    public void Baseline_M0_and_M1_amounts_stay_pinned_to_the_baseline_when_the_row_is_edited()
    {
        // Editing the price shifts the effective M0Amount/M1Amount (see
        // Editing_price_shifts_both_margins_and_leaves_costs_alone), but
        // BaselineM0Amount/BaselineM1Amount must keep reporting the untouched
        // baseline's margin -- the "before" side of the exported ceník's M0/M1 Kč
        // columns -- regardless of any override applied to this row.
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.Price,
            Value = 500m
        });

        row.M0Amount.Should().Be(325m);
        row.M1Amount.Should().Be(255m);
        row.BaselineM0Amount.Should().Be(245m);   // unchanged: baseline 420 - 175
        row.BaselineM1Amount.Should().Be(175m);   // unchanged: 245 - 70
    }

    [Fact]
    public void Editing_price_shifts_both_margins_and_leaves_costs_alone()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.Price,
            Value = 500m
        });

        row.Price.Should().Be(500m);
        row.MaterialCost.Should().Be(175m);
        row.ManufacturingCost.Should().Be(70m);
        row.M0Amount.Should().Be(325m);   // +80, the price delta
        row.M1Amount.Should().Be(255m);   // +80, the same delta
        row.IsEdited.Should().BeTrue();
    }

    [Fact]
    public void Editing_M0_implies_a_material_cost_and_shifts_M1_by_the_same_delta()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.M0Amount,
            Value = 265m
        });

        row.Price.Should().Be(420m);              // price is pinned
        row.MaterialCost.Should().Be(155m);       // 420 - 265
        row.ManufacturingCost.Should().Be(70m);   // unchanged
        row.M0Amount.Should().Be(265m);
        row.M1Amount.Should().Be(195m);           // +20, the same delta as M0
    }

    [Fact]
    public void Editing_M1_implies_a_manufacturing_cost_and_leaves_M0_untouched()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.M1Amount,
            Value = 195m
        });

        row.Price.Should().Be(420m);
        row.MaterialCost.Should().Be(175m);       // unchanged
        row.ManufacturingCost.Should().Be(50m);   // 245 - 195
        row.M0Amount.Should().Be(245m);           // unchanged
        row.M1Amount.Should().Be(195m);
    }

    [Fact]
    public void Editing_a_margin_percentage_is_equivalent_to_editing_the_amount()
    {
        // 50% of 420 = 210
        var byPercentage = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.M0Percentage,
            Value = 50m
        });
        var byAmount = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.M0Amount,
            Value = 210m
        });

        byPercentage.MaterialCost.Should().Be(byAmount.MaterialCost);
        byPercentage.M0Amount.Should().Be(byAmount.M0Amount);
    }

    [Fact]
    public void Editing_M1_percentage_is_equivalent_to_editing_M1_amount()
    {
        // 40% of 420 = 168
        var byPercentage = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.M1Percentage,
            Value = 40m
        });
        var byAmount = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.M1Amount,
            Value = 168m
        });

        byPercentage.ManufacturingCost.Should().Be(byAmount.ManufacturingCost);
        byPercentage.M1Amount.Should().Be(byAmount.M1Amount);
    }

    [Fact]
    public void Editing_forecast_quantity_does_not_change_the_row_margins()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.ForecastQuantity,
            Value = 800m
        });

        row.ForecastQuantity.Should().Be(800d);
        row.M0Amount.Should().Be(245m);
        row.M1Amount.Should().Be(175m);
    }

    [Fact]
    public void Edit_order_does_not_matter_because_edits_normalise_to_costs()
    {
        // Edit M0 then price ...
        var first = _sut.Calculate(new[] { Row() }, NoOverrides(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.M0Amount,
            Value = 265m
        });
        var m0ThenPrice = _sut.Calculate(new[] { Row() }, first.Overrides, new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.Price,
            Value = 500m
        }).Rows.Single();

        // ... versus price then M0, where M0 is set to what the first path produced.
        var second = _sut.Calculate(new[] { Row() }, NoOverrides(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.Price,
            Value = 500m
        });
        var priceThenM0 = _sut.Calculate(new[] { Row() }, second.Overrides, new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.M0Amount,
            Value = 345m // 500 - 155
        }).Rows.Single();

        m0ThenPrice.MaterialCost.Should().Be(priceThenM0.MaterialCost);
        m0ThenPrice.ManufacturingCost.Should().Be(priceThenM0.ManufacturingCost);
        m0ThenPrice.M0Amount.Should().Be(priceThenM0.M0Amount);
        m0ThenPrice.M1Amount.Should().Be(priceThenM0.M1Amount);
    }

    [Fact]
    public void Replaying_an_override_set_reproduces_the_same_rows()
    {
        var edited = _sut.Calculate(new[] { Row() }, NoOverrides(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.M1Amount,
            Value = 195m
        });

        var replayed = _sut.Calculate(new[] { Row() }, edited.Overrides, edit: null).Rows.Single();
        var original = edited.Rows.Single();

        replayed.Price.Should().Be(original.Price);
        replayed.MaterialCost.Should().Be(original.MaterialCost);
        replayed.ManufacturingCost.Should().Be(original.ManufacturingCost);
        replayed.M1Amount.Should().Be(original.M1Amount);
    }

    [Fact]
    public void Negative_margins_are_allowed_because_showing_a_loss_is_the_point()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.Price,
            Value = 200m
        });

        row.M0Amount.Should().Be(25m);
        row.M1Amount.Should().Be(-45m);   // 200 - 175 - 70
        row.M1Percentage.Should().BeApproximately(-22.5m, 0.01m);
    }

    [Fact]
    public void Zero_material_cost_is_allowed_M0_of_exactly_100_percent_is_computable()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.M0Percentage,
            Value = 100m
        });

        row.MaterialCost.Should().Be(0m);
        row.M0Percentage.Should().Be(100m);
    }

    [Theory]
    [InlineData(PricingEditField.Price, 0, ErrorCodes.PricingInvalidPrice)]
    [InlineData(PricingEditField.Price, -10, ErrorCodes.PricingInvalidPrice)]
    [InlineData(PricingEditField.M0Amount, 500, ErrorCodes.PricingNegativeMaterialCost)]
    [InlineData(PricingEditField.M0Percentage, 120, ErrorCodes.PricingNegativeMaterialCost)]
    [InlineData(PricingEditField.M1Amount, 300, ErrorCodes.PricingNegativeManufacturingCost)]
    [InlineData(PricingEditField.ForecastQuantity, -1, ErrorCodes.PricingNegativeQuantity)]
    public void Impossible_edits_are_rejected_with_the_matching_error_code(
        PricingEditField field, decimal value, ErrorCodes expected)
    {
        var act = () => Single(Row(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = field,
            Value = value
        });

        act.Should().Throw<PricingEditException>()
           .Which.ErrorCode.Should().Be(expected);
    }

    [Fact]
    public void Totals_sum_revenue_and_margin_over_all_rows()
    {
        var baseline = new[] { Row("P1"), Row("P2", price: 100m, material: 40m, manufacturing: 10m, quantity: 500d) };

        var result = _sut.Calculate(baseline, NoOverrides(), edit: null);

        // P1: 420 * 1000 = 420_000 ; P2: 100 * 500 = 50_000
        result.Totals.RevenueBefore.Should().Be(470_000m);
        result.Totals.RevenueAfter.Should().Be(470_000m);
        result.Totals.RevenueDelta.Should().Be(0m);
        // M0: 245 * 1000 + 60 * 500 = 275_000
        result.Totals.M0Before.Should().Be(275_000m);
        // M1: 175 * 1000 + 50 * 500 = 200_000
        result.Totals.M1Before.Should().Be(200_000m);
        result.Totals.EditedProductCount.Should().Be(0);
    }

    [Fact]
    public void A_cost_edit_moves_margin_but_never_revenue()
    {
        var result = _sut.Calculate(new[] { Row() }, NoOverrides(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.M1Amount,
            Value = 195m
        });

        result.Totals.RevenueDelta.Should().Be(0m);
        result.Totals.M1Delta.Should().Be(20_000m);   // +20 per unit x 1000
        result.Totals.M0Delta.Should().Be(0m);
        result.Totals.EditedProductCount.Should().Be(1);
    }

    [Fact]
    public void Rows_without_data_are_flagged_excluded_and_kept_out_of_totals()
    {
        var baseline = new[]
        {
            Row("P1"),
            Row("P2", price: 100m, material: 40m, manufacturing: 10m, quantity: 500d, hasData: false)
        };

        var result = _sut.Calculate(baseline, NoOverrides(), edit: null);

        result.Rows.Should().HaveCount(2);
        result.Rows.Single(r => r.ProductCode == "P2").IsExcluded.Should().BeTrue();
        result.Totals.ExcludedProductCount.Should().Be(1);
        result.Totals.RevenueBefore.Should().Be(420_000m);   // P2 is excluded, so it contributes nothing
    }

    [Fact]
    public void Delta_percentage_is_zero_rather_than_infinite_when_the_before_total_is_zero()
    {
        var baseline = new[] { Row("P1", quantity: 0d) };

        var result = _sut.Calculate(baseline, NoOverrides(), new PricingEditDto
        {
            ProductCode = "P1",
            Field = PricingEditField.Price,
            Value = 500m
        });

        result.Totals.RevenueBefore.Should().Be(0m);
        result.Totals.RevenueDeltaPercentage.Should().Be(0m);
    }

    [Fact]
    public void Duplicate_product_codes_in_overrides_are_resolved_with_last_wins()
    {
        var overrides = new List<PricingOverrideDto>
        {
            new() { ProductCode = "P1", Price = 450m },
            new() { ProductCode = "P1", Price = 500m }  // This should win
        };

        var result = _sut.Calculate(new[] { Row() }, overrides, edit: null);

        result.Rows.Single().Price.Should().Be(500m);
        result.Overrides.Single(o => o.ProductCode == "P1").Price.Should().Be(500m);
    }
}
