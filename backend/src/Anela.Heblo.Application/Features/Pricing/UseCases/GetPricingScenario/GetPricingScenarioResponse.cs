using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenario;

public class GetPricingScenarioResponse : BaseResponse
{
    public PricingScenarioSummaryDto Scenario { get; set; } = new();
    public List<PricingRowDto> Rows { get; set; } = new();
    public PricingTotalsDto Totals { get; set; } = new();
    public List<PricingOverrideDto> Overrides { get; set; } = new();

    public GetPricingScenarioResponse() { }

    public GetPricingScenarioResponse(ErrorCodes errorCode, Dictionary<string, string> parameters)
        : base(errorCode, parameters) { }
}
