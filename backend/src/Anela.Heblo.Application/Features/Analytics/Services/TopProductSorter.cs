using Anela.Heblo.Application.Features.Analytics.Contracts;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface ITopProductSorter
{
    List<TopProductDto> Sort(List<TopProductDto> products, string? sortBy, bool sortDescending);
}

/// <summary>
/// Extracted sorting logic for the top products list
/// </summary>
public class TopProductSorter : ITopProductSorter
{
    /// <summary>
    /// Applies sorting to the top products list
    /// </summary>
    public List<TopProductDto> Sort(List<TopProductDto> products, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by TotalMargin descending
            return sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList();
        }

        return sortBy.ToLower() switch
        {
            "groupkey" or "productcode" => sortDescending
                ? products.OrderByDescending(p => p.GroupKey).ToList()
                : products.OrderBy(p => p.GroupKey).ToList(),
            "displayname" or "productname" => sortDescending
                ? products.OrderByDescending(p => p.DisplayName).ToList()
                : products.OrderBy(p => p.DisplayName).ToList(),
            "totalmargin" => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList(),
            // M0-M2 margin levels - amounts
            "m0amount" => sortDescending
                ? products.OrderByDescending(p => p.M0Amount).ToList()
                : products.OrderBy(p => p.M0Amount).ToList(),
            "m1amount" => sortDescending
                ? products.OrderByDescending(p => p.M1Amount).ToList()
                : products.OrderBy(p => p.M1Amount).ToList(),
            "m2amount" => sortDescending
                ? products.OrderByDescending(p => p.M2Amount).ToList()
                : products.OrderBy(p => p.M2Amount).ToList(),
            // M0-M2 margin levels - percentages
            "m0percentage" => sortDescending
                ? products.OrderByDescending(p => p.M0Percentage).ToList()
                : products.OrderBy(p => p.M0Percentage).ToList(),
            "m1percentage" => sortDescending
                ? products.OrderByDescending(p => p.M1Percentage).ToList()
                : products.OrderBy(p => p.M1Percentage).ToList(),
            "m2percentage" => sortDescending
                ? products.OrderByDescending(p => p.M2Percentage).ToList()
                : products.OrderBy(p => p.M2Percentage).ToList(),
            // Pricing
            "sellingprice" => sortDescending
                ? products.OrderByDescending(p => p.SellingPrice).ToList()
                : products.OrderBy(p => p.SellingPrice).ToList(),
            "purchaseprice" => sortDescending
                ? products.OrderByDescending(p => p.PurchasePrice).ToList()
                : products.OrderBy(p => p.PurchasePrice).ToList(),
            _ => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList()
        };
    }
}
