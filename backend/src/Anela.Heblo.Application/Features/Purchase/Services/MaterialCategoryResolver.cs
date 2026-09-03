using Anela.Heblo.Application.Features.Purchase.Contracts;

namespace Anela.Heblo.Application.Features.Purchase.Services;

/// <summary>
/// Splits materials into categories by their product code prefix.
/// Labels are "ETI...", packaging is "VIC..." (caps) and "LAH..." (bottles),
/// everything else falls into <see cref="MaterialCategoryFilter.Other"/>.
/// </summary>
public static class MaterialCategoryResolver
{
    private const string LabelPrefix = "ETI";

    private static readonly string[] PackagingPrefixes = { "VIC", "LAH" };

    public static bool Matches(string productCode, MaterialCategoryFilter category) => category switch
    {
        MaterialCategoryFilter.Labels => IsLabel(productCode),
        MaterialCategoryFilter.Packaging => IsPackaging(productCode),
        MaterialCategoryFilter.Other => !IsLabel(productCode) && !IsPackaging(productCode),
        _ => true
    };

    private static bool IsLabel(string productCode) => HasPrefix(productCode, LabelPrefix);

    private static bool IsPackaging(string productCode) => PackagingPrefixes.Any(prefix => HasPrefix(productCode, prefix));

    private static bool HasPrefix(string productCode, string prefix) =>
        productCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
