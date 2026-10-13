namespace Anela.Heblo.Domain.Features.ProductPricing;

public interface IErpPurchasePriceWriter
{
    /// <param name="erpItemId">Internal ceník id (<c>idcenik</c>). Addressing by code would create records.</param>
    /// <param name="purchasePrice">Purchase price excluding VAT, per the item's primary unit (<c>mj1</c>).</param>
    Task SetPurchasePriceAsync(int erpItemId, decimal purchasePrice, CancellationToken ct);
}
