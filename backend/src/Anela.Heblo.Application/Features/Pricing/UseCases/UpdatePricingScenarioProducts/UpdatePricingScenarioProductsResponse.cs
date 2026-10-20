using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.UpdatePricingScenarioProducts;

public class UpdatePricingScenarioProductsResponse : BaseResponse
{
    public PricingScenarioSummaryDto Scenario { get; set; } = new();
    public List<PricingRowDto> Rows { get; set; } = new();
    public PricingTotalsDto Totals { get; set; } = new();
    public List<PricingOverrideDto> Overrides { get; set; } = new();

    /// <summary>Requested removals that matched no product in the scenario.</summary>
    public List<string> UnmatchedRemovals { get; set; } = new();

    public UpdatePricingScenarioProductsResponse() { }

    public UpdatePricingScenarioProductsResponse(ErrorCodes errorCode, Dictionary<string, string> parameters)
        : base(errorCode, parameters) { }
}
