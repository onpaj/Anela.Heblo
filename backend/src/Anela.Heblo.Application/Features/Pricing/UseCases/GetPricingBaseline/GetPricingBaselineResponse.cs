using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingBaseline;

public class GetPricingBaselineResponse : BaseResponse
{
    public List<PricingRowDto> Rows { get; set; } = new();
    public PricingTotalsDto Totals { get; set; } = new();
}
