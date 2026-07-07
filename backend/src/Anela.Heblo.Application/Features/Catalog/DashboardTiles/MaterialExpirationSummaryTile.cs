using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Catalog.DashboardTiles;

/// <summary>
/// Dashboard tile showing material lots by proximity to their expiration date.
/// Counts lots (with stock on hand) that are: already expired, expiring within 30 days,
/// or expiring within 90 days. Lots expiring beyond the 90-day horizon are ignored.
/// Scope: materials only (ProductType.Material with HasExpiration).
/// </summary>
[TileId("materialexpirationsummary")]
public class MaterialExpirationSummaryTile : ITile
{
    private const int SoonDays = 30;
    private const int HorizonDays = 90;

    private readonly ICatalogRepository _catalogRepository;

    public MaterialExpirationSummaryTile(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public string Title => "Expirace surovin";
    public string Description => "Přehled surovin podle blížící se expirace šarží";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.Warehouse;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public string[] RequiredPermissions => Array.Empty<string>();

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);
            var soonThreshold = today.AddDays(SoonDays);
            var horizonThreshold = today.AddDays(HorizonDays);

            var catalogItems = await _catalogRepository.GetAllAsync(cancellationToken);

            var expirations = catalogItems
                .Where(item => item.Type == ProductType.Material && item.HasExpiration)
                .SelectMany(item => item.Stock.Lots)
                .Where(lot => lot.Amount > 0 && lot.Expiration.HasValue)
                .Select(lot => lot.Expiration!.Value)
                .ToList();

            var expired = expirations.Count(exp => exp < today);
            var within30 = expirations.Count(exp => exp >= today && exp <= soonThreshold);
            var within90 = expirations.Count(exp => exp > soonThreshold && exp <= horizonThreshold);

            return new
            {
                status = "success",
                data = new
                {
                    expired,     // already past expiration
                    within30,    // expiring within 30 days
                    within90,    // expiring within 31-90 days
                    total = expired + within30 + within90,
                    date = now
                },
                metadata = new
                {
                    lastUpdated = now,
                    source = "CatalogRepository"
                },
                drillDown = new
                {
                    filters = new { type = "Material" },
                    enabled = true,
                    tooltip = "Zobrazit suroviny"
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
