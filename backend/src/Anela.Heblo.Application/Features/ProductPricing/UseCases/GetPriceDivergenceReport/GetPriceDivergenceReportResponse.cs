using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetPriceDivergenceReport;

public class GetPriceDivergenceReportResponse : BaseResponse
{
    public List<PriceDivergenceRowDto> Rows { get; set; } = new();
    public PriceDivergenceSummaryDto Summary { get; set; } = new();
}
