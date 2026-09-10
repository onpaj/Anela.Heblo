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

    public static bool IsBundleCode(string? productCode) =>
        !string.IsNullOrEmpty(productCode)
        && (productCode.StartsWith(GiftPackagePrefix, StringComparison.Ordinal)
            || productCode.StartsWith(SetPrefix, StringComparison.Ordinal));

    public static ProductType Resolve(ProductType erpType, string? productCode) =>
        erpType == ProductType.Product && IsBundleCode(productCode)
            ? ProductType.Set
            : erpType;
}
