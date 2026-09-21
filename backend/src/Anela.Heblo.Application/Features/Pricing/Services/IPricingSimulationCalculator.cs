using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;

namespace Anela.Heblo.Application.Features.Pricing.Services;

public interface IPricingSimulationCalculator
{
    /// <summary>
    /// Applies the stored overrides and then the incoming edit (if any) to the baseline,
    /// returning fully computed rows, totals, and the normalised override set.
    /// </summary>
    /// <exception cref="PricingEditException">The edit implies a negative cost or a non-positive price.</exception>
    PricingSimulationResult Calculate(
        IReadOnlyList<PricingBaselineRow> baseline,
        IReadOnlyList<PricingOverrideDto> overrides,
        PricingEditDto? edit);
}
