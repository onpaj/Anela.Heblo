namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Writes the selling price, INCLUDING VAT, to the ERP's price list.</summary>
public interface IErpPriceWriter
{
    /// <param name="erpItemId">Internal ceník id (<c>idcenik</c>). Addressing by code would create records.</param>
    /// <param name="priceWithVat">
    /// The price INCLUDING VAT. The implementation is responsible for telling the ERP that
    /// this is what the number means, so the value does not get reinterpreted according to
    /// however the item happens to be configured.
    /// </param>
    Task SetPriceWithVatAsync(int erpItemId, decimal priceWithVat, CancellationToken ct);
}
