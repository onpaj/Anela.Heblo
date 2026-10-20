namespace Anela.Heblo.Domain.Features.Catalog.Price;

public class ProductPriceEshop
{
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>The price customers pay today: the action price while an action runs, else the regular price.</summary>
    public decimal? PriceWithVat { get; set; }

    /// <summary>Without-VAT form of <see cref="PriceWithVat"/> (the effective price).</summary>
    public decimal? PriceWithoutVat { get; set; }

    public decimal? PurchasePrice { get; set; }

    /// <summary>The regular list price, regardless of any action.</summary>
    public decimal? RegularPriceWithVat { get; set; }

    /// <summary>The action price set in the e-shop, even when the action is not running today.</summary>
    public decimal? ActionPriceWithVat { get; set; }

    public DateOnly? ActionFrom { get; set; }
    public DateOnly? ActionUntil { get; set; }
    public bool IsInAction { get; set; }

    /// <summary>
    /// Builds the catalog price from a price list row, resolving the effective price for
    /// <paramref name="today"/> (the e-shop's local date). An action runs when its price is
    /// positive and today lies within [from, until]; a missing bound is open-ended, and both
    /// bounds are inclusive days.
    /// </summary>
    public static ProductPriceEshop FromPriceList(
        string productCode,
        decimal regularPriceWithVat,
        decimal? actionPriceWithVat,
        DateOnly? actionFrom,
        DateOnly? actionUntil,
        decimal vatRate,
        DateOnly today)
    {
        var isInAction = IsActionRunning(actionPriceWithVat, actionFrom, actionUntil, today);
        var effectivePrice = isInAction ? actionPriceWithVat!.Value : regularPriceWithVat;

        return new ProductPriceEshop
        {
            ProductCode = productCode,
            PriceWithVat = effectivePrice,
            PriceWithoutVat = Math.Round(effectivePrice / (1 + vatRate / 100m), 2, MidpointRounding.AwayFromZero),
            // Deliberately null: the price list carries no purchase price we trust.
            // CurrentPurchasePrice therefore falls through to the ERP value, which is the
            // source of truth. Do not try to source a purchase price from Shoptet again.
            PurchasePrice = null,
            RegularPriceWithVat = regularPriceWithVat,
            ActionPriceWithVat = actionPriceWithVat,
            ActionFrom = actionFrom,
            ActionUntil = actionUntil,
            IsInAction = isInAction,
        };
    }

    private static bool IsActionRunning(decimal? actionPrice, DateOnly? from, DateOnly? until, DateOnly today) =>
        actionPrice is > 0
        && (from is null || from <= today)
        && (until is null || until >= today);
}
