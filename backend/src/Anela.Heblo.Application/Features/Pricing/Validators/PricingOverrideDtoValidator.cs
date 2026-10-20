using Anela.Heblo.Application.Features.Pricing.Contracts;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Pricing.Validators;

/// <summary>
/// Bounds for one entry of the sparse override collection. PricingSimulationCalculator only
/// validates the values it derives from an <c>Edit</c> gesture, so an override sent directly
/// (a replayed collection, a hand-crafted request) would otherwise reach both the recalculate
/// and the persisted scenario unchecked. Shared by RecalculatePricingRequestValidator and
/// SavePricingScenarioRequestValidator so the two can never drift apart.
/// </summary>
public class PricingOverrideDtoValidator : AbstractValidator<PricingOverrideDto>
{
    public PricingOverrideDtoValidator()
    {
        RuleFor(x => x.ProductCode).NotEmpty();

        // Each value is optional (the collection is sparse); only a value that IS set is bounded.
        RuleFor(x => x.Price).GreaterThan(0m).When(x => x.Price.HasValue);
        RuleFor(x => x.MaterialCost).GreaterThanOrEqualTo(0m).When(x => x.MaterialCost.HasValue);
        RuleFor(x => x.ManufacturingCost).GreaterThanOrEqualTo(0m).When(x => x.ManufacturingCost.HasValue);
        RuleFor(x => x.ForecastQuantity).GreaterThanOrEqualTo(0d).When(x => x.ForecastQuantity.HasValue);
    }

    /// <summary>
    /// A product code may appear at most once. A duplicate would violate the
    /// (ScenarioId, ProductCode) unique index on save and collapse the snapshot lookup in
    /// GetPricingScenarioHandler, so it is rejected at the boundary rather than at the database.
    /// </summary>
    public static bool HasDistinctProductCodes(IReadOnlyCollection<PricingOverrideDto>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return true;
        }

        return overrides
            .Select(o => o.ProductCode ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == overrides.Count;
    }

    public const string DuplicateProductCodesMessage =
        "'Overrides' must not contain the same product code more than once.";
}
