using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenarios;

public class GetPricingScenariosResponse : BaseResponse
{
    public List<PricingScenarioSummaryDto> Scenarios { get; set; } = new();
}
