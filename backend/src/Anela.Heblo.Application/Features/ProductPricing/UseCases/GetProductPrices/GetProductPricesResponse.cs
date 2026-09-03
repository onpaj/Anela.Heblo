using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetProductPrices;

public class GetProductPricesResponse : BaseResponse
{
    public List<ProductPriceDto> Prices { get; set; } = new();
}
