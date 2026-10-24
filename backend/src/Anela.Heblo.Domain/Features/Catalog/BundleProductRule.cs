namespace Anela.Heblo.Domain.Features.Catalog;

/// <summary>
/// Single source of truth for what counts as a bundle ("balíček") in this system.
/// ERP has no bundle product type — a bundle is a Product whose code carries a known prefix.
/// Both the catalog merge and the set-parts refresh depend on this rule agreeing with itself.
/// </summary>
public static class BundleProductRule
{
    private const string GiftPackagePrefix = "BAL";
    private const string SetPrefix = "SET";

    // Shoptet product sets (e.g. "Double shot mini"): sold as one invoice line whose components are
    // defined in ERP like a gift package's, but picked loose at packing rather than assembled ahead —
    // so they expand into component sales without becoming ProductType.Set.
    private const string ShoptetSetPrefix = "SA";

    public static bool IsBundleCode(string? productCode) =>
        !string.IsNullOrEmpty(productCode)
        && (productCode.StartsWith(GiftPackagePrefix, StringComparison.Ordinal)
            || productCode.StartsWith(SetPrefix, StringComparison.Ordinal));

    public static ProductType Resolve(ProductType erpType, string? productCode) =>
        erpType == ProductType.Product && IsBundleCode(productCode)
            ? ProductType.Set
            : erpType;

    /// <summary>
    /// Whether the product's ERP set definition should be fetched so its sales expand into component sales.
    /// </summary>
    public static bool HasComponentParts(ProductType erpType, string? productCode) =>
        erpType == ProductType.Product
        && (IsBundleCode(productCode)
            || (productCode?.StartsWith(ShoptetSetPrefix, StringComparison.Ordinal) ?? false));
}
