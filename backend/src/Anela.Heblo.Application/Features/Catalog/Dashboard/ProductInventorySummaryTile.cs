using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Catalog.Dashboard;

/// <summary>
/// Dashboard tile showing summary of products by inventory age.
/// Shows counts for: < 120 days, 120-250 days, > 250 days since last stock taking.
/// </summary>
public class ProductInventorySummaryTile : ITile
{
    private readonly ICatalogRepository _catalogRepository;

    public ProductInventorySummaryTile(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public string Title => "Produkty podle stáří inventury";
    public string Description => "Přehled produktů podle doby od poslední inventury";
    public TileSize Size => TileSize.Medium;
    public TileCategory Category => TileCategory.Warehouse;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public async Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var catalogItems = await _catalogRepository.GetAllAsync(cancellationToken);

            var products = catalogItems
                .Where(c => c.Type == ProductType.Product)
                .ToList();

            var recent = products.Count(p => p.LastStockTaking.HasValue &&
                                            (now - p.LastStockTaking.Value).TotalDays < 120);

            var medium = products.Count(p => p.LastStockTaking.HasValue &&
                                            (now - p.LastStockTaking.Value).TotalDays >= 120 &&
                                            (now - p.LastStockTaking.Value).TotalDays <= 250);

            var old = products.Count(p => p.LastStockTaking.HasValue &&
                                         (now - p.LastStockTaking.Value).TotalDays > 250);

            var never = products.Count(p => !p.LastStockTaking.HasValue);

            return new
            {
                status = "success",
                data = new
                {
                    recent,      // < 120 days
                    medium,      // 120-250 days
                    old,         // > 250 days
                    never,       // never inventoried
                    total = products.Count(),
                    date = now
                }
            };
        }
        catch (Exception ex)
        {
            return new
            {
                status = "error",
                error = ex.Message
            };
        }
    }
}
