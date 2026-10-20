using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SyncProductPrices;

/// <summary>
/// Synchronises one selection of products from Shoptet into Flexi: re-reads both systems,
/// writes the Shoptet price into the ERP wherever the comparison says it is needed, and
/// returns the rows as they stand afterwards. The selection is a list of codes long enough to
/// belong in a body rather than a query string.
/// </summary>
public class SyncProductPricesRequest : IRequest<SyncProductPricesResponse>
{
    public List<string> ProductCodes { get; set; } = new();
}
