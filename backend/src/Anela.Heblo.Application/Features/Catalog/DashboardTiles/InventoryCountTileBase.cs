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
    protected abstract ProductType TargetProductType { get; }
    
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
                .Where(c => c.Type == TargetProductType &&
                           c.LastStockTaking.HasValue &&
                           c.LastStockTaking.Value >= cutoffDate)
                .Count();

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