using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;

public class RecalculatePricingResponse : BaseResponse
{
    public List<PricingRowDto> Rows { get; set; } = new();
    public PricingTotalsDto Totals { get; set; } = new();
    public List<PricingOverrideDto> Overrides { get; set; } = new();

    public RecalculatePricingResponse() { }

    public RecalculatePricingResponse(ErrorCodes errorCode, Dictionary<string, string> parameters)
        : base(errorCode, parameters) { }
}
