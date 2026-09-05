using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceRequest : IRequest<SetProductPriceResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal PriceWithVat { get; set; }
}
