using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Catalog.Dashboard;

/// <summary>
/// Dashboard tile showing count of products inventoried in the last 30 days.
/// </summary>
public class ProductInventoryCountTile : ITile
{
    private readonly ICatalogRepository _catalogRepository;

    public ProductInventoryCountTile(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public string Title => "Produkty inventarizované";
    public string Description => "Počet produktů inventarizovaných za posledních 30 dní";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.Warehouse;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public async Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-30);
            var catalogItems = await _catalogRepository.GetAllAsync(cancellationToken);

            var count = catalogItems
                .Where(c => c.Type == ProductType.Product &&
                           c.LastStockTaking.HasValue &&
                           c.LastStockTaking.Value >= cutoffDate)
                .Count();

            return new
            {
                status = "success",
                data = new
                {
                    count,
                    date = DateTime.UtcNow
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
