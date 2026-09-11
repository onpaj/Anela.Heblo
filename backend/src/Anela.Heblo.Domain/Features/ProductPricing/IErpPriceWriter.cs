namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Writes the base selling price to the ERP's price list.</summary>
public interface IErpPriceWriter
{
    /// <param name="erpItemId">Internal ceník id (<c>idcenik</c>). Addressing by code would create records.</param>
    Task SetPriceWithoutVatAsync(int erpItemId, decimal priceWithoutVat, CancellationToken ct);
}
