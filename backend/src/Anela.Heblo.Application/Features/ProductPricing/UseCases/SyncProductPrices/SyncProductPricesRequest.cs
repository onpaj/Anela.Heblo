using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SyncProductPrices;

/// <summary>
/// Re-reads Shoptet and Flexi for one selection of products. A read, despite the POST — the
/// selection is a list of codes long enough to belong in a body rather than a query string.
/// </summary>
public class SyncProductPricesRequest : IRequest<SyncProductPricesResponse>
{
    public List<string> ProductCodes { get; set; } = new();
}
