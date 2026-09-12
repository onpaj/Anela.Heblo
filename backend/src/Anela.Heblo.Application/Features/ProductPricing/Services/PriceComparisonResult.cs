using Anela.Heblo.Application.Features.ProductPricing.Contracts;

namespace Anela.Heblo.Application.Features.ProductPricing.Services;

public class PriceComparisonResult
{
    public List<PriceDivergenceRowDto> Rows { get; set; } = new();
    public PriceDivergenceSummaryDto Summary { get; set; } = new();
}
