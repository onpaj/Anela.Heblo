using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceResponse : BaseResponse
{
    public SetProductPriceResponse() { }

    public SetProductPriceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }

    public decimal PriceWithVat { get; set; }
}
