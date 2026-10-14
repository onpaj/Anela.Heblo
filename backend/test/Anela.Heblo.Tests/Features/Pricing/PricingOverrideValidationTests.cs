using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;
using Anela.Heblo.Application.Features.Pricing.UseCases.SavePricingScenario;
using Anela.Heblo.Application.Features.Pricing.Validators;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

/// <summary>
/// The calculator only validates values it derives from an <c>Edit</c> gesture, so an override
/// posted directly reaches the recalculate — and, on save, the database — unchecked. These
/// tests pin the boundary rules on BOTH request validators: a bad override must never be
/// recalculated and must never be persisted.
/// </summary>
public class PricingOverrideValidationTests
{
    private readonly RecalculatePricingRequestValidator _recalculateValidator = new();
    private readonly SavePricingScenarioRequestValidator _saveValidator = new();

    private static RecalculatePricingRequest Recalculate(params PricingOverrideDto[] overrides) =>
        new() { Overrides = overrides.ToList() };

    private static SavePricingScenarioRequest Save(params PricingOverrideDto[] overrides) =>
        new() { Name = "Scénář", Overrides = overrides.ToList() };

    private static PricingOverrideDto Override(string productCode = "PROD001") =>
        new() { ProductCode = productCode };

    public static TheoryData<PricingOverrideDto> InvalidOverrides() => new()
    {
        new PricingOverrideDto { ProductCode = "PROD001", Price = 0m },
        new PricingOverrideDto { ProductCode = "PROD001", Price = -1m },
        new PricingOverrideDto { ProductCode = "PROD001", MaterialCost = -0.01m },
        new PricingOverrideDto { ProductCode = "PROD001", ManufacturingCost = -5m },
        new PricingOverrideDto { ProductCode = "PROD001", ForecastQuantity = -1d },
        new PricingOverrideDto { ProductCode = "", Price = 100m },
    };

    public static TheoryData<PricingOverrideDto> ValidOverrides() => new()
    {
        new PricingOverrideDto { ProductCode = "PROD001" },
        new PricingOverrideDto { ProductCode = "PROD001", Price = 0.01m },
        new PricingOverrideDto { ProductCode = "PROD001", MaterialCost = 0m, ManufacturingCost = 0m },
        new PricingOverrideDto { ProductCode = "PROD001", ForecastQuantity = 0d },
        new PricingOverrideDto
        {
            ProductCode = "PROD001",
            Price = 499m,
            MaterialCost = 175m,
            ManufacturingCost = 70m,
            ForecastQuantity = 1240d
        },
    };

    [Theory]
    [MemberData(nameof(InvalidOverrides))]
    public void RecalculateValidator_RejectsOutOfRangeOverride(PricingOverrideDto invalid)
    {
        var result = _recalculateValidator.Validate(Recalculate(invalid));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(InvalidOverrides))]
    public void SaveValidator_RejectsOutOfRangeOverride(PricingOverrideDto invalid)
    {
        var result = _saveValidator.Validate(Save(invalid));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ValidOverrides))]
    public void RecalculateValidator_AcceptsInRangeOverride(PricingOverrideDto valid)
    {
        var result = _recalculateValidator.Validate(Recalculate(valid));

        result.IsValid.Should().BeTrue(because: string.Join(", ", result.Errors));
    }

    [Theory]
    [MemberData(nameof(ValidOverrides))]
    public void SaveValidator_AcceptsInRangeOverride(PricingOverrideDto valid)
    {
        var result = _saveValidator.Validate(Save(valid));

        result.IsValid.Should().BeTrue(because: string.Join(", ", result.Errors));
    }

    [Fact]
    public void RecalculateValidator_RejectsDuplicateProductCodes()
    {
        var result = _recalculateValidator.Validate(Recalculate(Override(), Override()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SaveValidator_RejectsDuplicateProductCodes()
    {
        // A duplicate would violate the (ScenarioId, ProductCode) unique index — an unhandled
        // 500 plus a poisoned change tracker — and collapse GetPricingScenarioHandler's
        // ToDictionary on reload.
        var result = _saveValidator.Validate(Save(Override(), Override()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SaveValidator_RejectsDuplicateProductCodesDifferingOnlyByCase()
    {
        var result = _saveValidator.Validate(Save(Override("PROD001"), Override("prod001")));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SaveValidator_AcceptsDistinctProductCodes()
    {
        var result = _saveValidator.Validate(Save(Override("PROD001"), Override("PROD002")));

        result.IsValid.Should().BeTrue(because: string.Join(", ", result.Errors));
    }

    [Fact]
    public void RecalculateValidator_AcceptsAnEmptyOverrideCollection()
    {
        var result = _recalculateValidator.Validate(Recalculate());

        result.IsValid.Should().BeTrue(because: string.Join(", ", result.Errors));
    }
}
