using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Catalog.DashboardTiles;

/// <summary>
/// Abstract base class for inventory summary tiles that show summary by inventory age.
/// Shows counts for: < 120 days, 120-250 days, > 250 days since last stock taking.
/// </summary>
public abstract class InventorySummaryTileBase : ITile
{
    private const double ThresholdCritical = 120;
    private const double ThresholdWarning = 250;
    private readonly ICatalogRepository _catalogRepository;

    protected InventorySummaryTileBase(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public abstract string Title { get; }
    public abstract string Description { get; }
    protected abstract Func<CatalogAggregate, bool> ItemFilter { get; }

    public TileSize Size => TileSize.Medium;
    public TileCategory Category => TileCategory.Warehouse;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var catalogItems = await _catalogRepository.GetAllAsync(cancellationToken);

            var filteredItems = catalogItems
                .Where(ItemFilter)
                .ToList();

            var recent = filteredItems.Count(item => item.LastStockTaking.HasValue &&
                                                    (now - item.LastStockTaking.Value).TotalDays < ThresholdCritical);

            var medium = filteredItems.Count(item => item.LastStockTaking.HasValue &&
                                                    (now - item.LastStockTaking.Value).TotalDays >= ThresholdCritical &&
                                                    (now - item.LastStockTaking.Value).TotalDays <= ThresholdWarning);

            var old = filteredItems.Count(item => item.LastStockTaking.HasValue &&
                                                 (now - item.LastStockTaking.Value).TotalDays > ThresholdWarning);

            var never = filteredItems.Count(item => !item.LastStockTaking.HasValue);

            return new
            {
                status = "success",
                data = new
                {
                    recent,      // < 120 days
                    medium,      // 120-250 days
                    old,         // > 250 days
                    never,       // never inventoried
                    total = filteredItems.Count(),
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