using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;

namespace Anela.Heblo.Application.Features.Pricing.Services;

public interface IPricingBaselineBuilder
{
    Task<IReadOnlyList<PricingBaselineRow>> BuildAsync(PricingFilterDto filter, CancellationToken ct);
}
