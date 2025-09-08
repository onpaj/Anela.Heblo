using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IProductFilterService
{
    Task<List<AnalyticsProduct>> FilterProductsAsync(
        IAsyncEnumerable<AnalyticsProduct> productsStream,
        string? productFilter,
        string? categoryFilter,
        int maxProducts,
        CancellationToken cancellationToken = default);

    bool PassesFilters(AnalyticsProduct product, string? productFilter, string? categoryFilter);
}

public class ProductFilterService : IProductFilterService
{
    public async Task<List<AnalyticsProduct>> FilterProductsAsync(
        IAsyncEnumerable<AnalyticsProduct> productsStream,
        string? productFilter,
        string? categoryFilter,
        int maxProducts,
        CancellationToken cancellationToken = default)
    {
        var products = new List<AnalyticsProduct>();

        await foreach (var product in productsStream.WithCancellation(cancellationToken))
        {
            if (!PassesFilters(product, productFilter, categoryFilter))
                continue;

            products.Add(product);

            // Apply limit during filtering to avoid processing too many products
            if (products.Count >= maxProducts)
                break;
        }

        return products;
    }

    public bool PassesFilters(AnalyticsProduct product, string? productFilter, string? categoryFilter)
    {
        // Apply product name filter if specified
        if (!string.IsNullOrWhiteSpace(productFilter) &&
            !product.ProductName.Contains(productFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Apply category filter if specified
        if (!string.IsNullOrWhiteSpace(categoryFilter) &&
            !string.Equals(product.ProductCategory, categoryFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}