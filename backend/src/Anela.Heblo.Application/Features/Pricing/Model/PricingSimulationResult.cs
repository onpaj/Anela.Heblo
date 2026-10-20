using Anela.Heblo.Application.Features.Pricing.Contracts;

namespace Anela.Heblo.Application.Features.Pricing.Model;

public record PricingSimulationResult(
    IReadOnlyList<PricingRowDto> Rows,
    PricingTotalsDto Totals,
    IReadOnlyList<PricingOverrideDto> Overrides);
