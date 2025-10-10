using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Catalog.DashboardTiles;

/// <summary>
/// Abstract base class for inventory count tiles that show count of items inventoried in the last 30 days.
/// </summary>
public abstract class InventoryCountTileBase : ITile
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly TimeProvider _timeProvider;

    protected InventoryCountTileBase(
        ICatalogRepository catalogRepository,
        TimeProvider timeProvider)
    {
        _catalogRepository = catalogRepository;
        _timeProvider = timeProvider;
    }

    public abstract string Title { get; }
    public abstract string Description { get; }
    protected abstract Func<CatalogAggregate, bool> ItemFilter { get; }
    
    protected int DaysOffset { get; set; } = 30;
    
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
            var cutoffDate = DateTime.UtcNow.AddDays(-DaysOffset);
            var catalogItems = await _catalogRepository.GetAllAsync(cancellationToken);

            var count = catalogItems
                .Where(ItemFilter)
                .Count(w => w.LastStockTaking.HasValue && w.LastStockTaking.Value >= cutoffDate);

            return new
            {
                status = "success",
                data = new
                {
                    count,
                    date = _timeProvider.GetUtcNow().DateTime
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