using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SyncProductPrices;

/// <summary>
/// Carries rows only, no summary: the caller already holds the whole report and merges these
/// freshly-read rows into it, so a summary computed over this subset would describe neither
/// the selection the caller sees nor the catalogue the tiles count.
/// </summary>
public class SyncProductPricesResponse : BaseResponse
{
    public List<PriceDivergenceRowDto> Rows { get; set; } = new();
}
