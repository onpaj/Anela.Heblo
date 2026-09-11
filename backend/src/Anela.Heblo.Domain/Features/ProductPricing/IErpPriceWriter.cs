namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Writes the base selling price (<c>cenaZakl</c>) to the ERP's price list.</summary>
public interface IErpPriceWriter
{
    /// <param name="erpItemId">Internal ceník id (<c>idcenik</c>). Addressing by code would create records.</param>
    /// <param name="basePrice">
    /// The value to store in <c>cenaZakl</c>. Its VAT meaning is the ITEM'S OWN, not a fixed
    /// convention: for a <c>bezDph</c> item this is the price excluding VAT, for every other
    /// price type it is the price including VAT. The caller decides which, because only the
    /// caller knows the item's <c>typCenyDphK</c> — see <c>SetProductPriceHandler</c>.
    /// </param>
    Task SetBasePriceAsync(int erpItemId, decimal basePrice, CancellationToken ct);
}
