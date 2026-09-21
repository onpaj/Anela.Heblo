using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

public class RecalculatePricingHandlerTests
{
    private readonly Mock<IPricingBaselineBuilder> _baseline = new();

    // The real calculator, not a mock: this handler's job is to wire it up, and a mock
    // would let a wiring bug through.
    private RecalculatePricingHandler CreateSut()
        => new(_baseline.Object, new PricingSimulationCalculator());

    private void GivenBaseline(params PricingBaselineRow[] rows)
        => _baseline.Setup(b => b.BuildAsync(It.IsAny<PricingFilterDto>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(rows.ToList());

    private static PricingBaselineRow Row(string code = "P1")
        => new(code, $"Product {code}", 420m, 175m, 70m, 1000d, true);

    [Fact]
    public async Task Applies_the_edit_and_returns_recomputed_rows_and_totals()
    {
        GivenBaseline(Row());

        var response = await CreateSut().Handle(new RecalculatePricingRequest
        {
            Overrides = new List<PricingOverrideDto>(),
            Edit = new PricingEditDto
            {
                ProductCode = "P1", Field = PricingEditField.Price, Value = 500m
            }
        }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Rows.Single().Price.Should().Be(500m);
        response.Totals.RevenueAfter.Should().Be(500_000m);
        response.Overrides.Single().Price.Should().Be(500m);
    }

    [Fact]
    public async Task Returns_the_error_code_when_the_edit_is_impossible()
    {
        GivenBaseline(Row());

        var response = await CreateSut().Handle(new RecalculatePricingRequest
        {
            Overrides = new List<PricingOverrideDto>(),
            Edit = new PricingEditDto
            {
                ProductCode = "P1", Field = PricingEditField.M0Amount, Value = 500m
            }
        }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PricingNegativeMaterialCost);
    }

    [Fact]
    public async Task Rebuilds_the_baseline_from_the_catalog_and_ignores_any_client_supplied_baseline()
    {
        // The client sends only overrides; baselines are always re-derived server-side so
        // a tampered payload cannot change the reported margin.
        GivenBaseline(Row());

        var response = await CreateSut().Handle(new RecalculatePricingRequest
        {
            Overrides = new List<PricingOverrideDto>(),
            Edit = null
        }, CancellationToken.None);

        response.Rows.Single().BaselinePrice.Should().Be(420m);
        response.Rows.Single().BaselineMaterialCost.Should().Be(175m);
    }

    [Fact]
    public async Task Replays_an_existing_override_set_when_no_edit_is_supplied()
    {
        GivenBaseline(Row());

        var response = await CreateSut().Handle(new RecalculatePricingRequest
        {
            Overrides = new List<PricingOverrideDto>
            {
                new() { ProductCode = "P1", Price = 500m, MaterialCost = 175m,
                        ManufacturingCost = 70m, ForecastQuantity = 900d }
            },
            Edit = null
        }, CancellationToken.None);

        response.Rows.Single().Price.Should().Be(500m);
        response.Rows.Single().ForecastQuantity.Should().Be(900d);
        response.Totals.RevenueAfter.Should().Be(450_000m);
    }
}
