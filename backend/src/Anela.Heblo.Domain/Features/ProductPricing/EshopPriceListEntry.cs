namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>
/// One product's row in the e-shop's retail price list. <see cref="PriceWithVat"/> is the
/// regular list price; the action fields describe a discount that may or may not be running
/// today — whether it is, is decided by <c>ProductPriceEshop.FromPriceList</c>.
/// </summary>
public record EshopPriceListEntry(
    string ProductCode,
    decimal PriceWithVat,
    decimal? ActionPriceWithVat,
    DateOnly? ActionFrom,
    DateOnly? ActionUntil);
