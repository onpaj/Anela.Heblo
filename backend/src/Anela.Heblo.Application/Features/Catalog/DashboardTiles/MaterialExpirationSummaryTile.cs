using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Catalog.DashboardTiles;

/// <summary>
/// Dashboard tile showing materials by proximity to their nearest lot expiration date.
/// Counts each material once (by its earliest in-stock expiring lot) as: already expired,
/// expiring within 30 days, expiring within 90 days, or healthy (more than 90 days out).
/// This mirrors the drill-down list, which buckets materials by CatalogAggregate.MinimalExpiration.
/// Scope: materials only (ProductType.Material with HasExpiration); materials without an
/// in-stock expiring lot are excluded from every bucket.
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

            // Bucket each material by its earliest in-stock expiring lot (MinimalExpiration).
            // Materials without such a lot have a null MinimalExpiration and are excluded.
            var expirations = catalogItems
                .Where(item => item.Type == ProductType.Material && item.HasExpiration)
                .Select(item => item.MinimalExpiration)
                .Where(exp => exp.HasValue)
                .Select(exp => exp!.Value)
                .ToList();

            var expired = expirations.Count(exp => exp < today);
            var within30 = expirations.Count(exp => exp >= today && exp <= soonThreshold);
            var within90 = expirations.Count(exp => exp > soonThreshold && exp <= horizonThreshold);
            var ok = expirations.Count(exp => exp > horizonThreshold);

            return new
            {
                status = "success",
                data = new
                {
                    expired,     // already past expiration
                    within30,    // expiring within 30 days
                    within90,    // expiring within 31-90 days
                    ok,          // healthy: expiring beyond the 90-day horizon
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
                    filters = new { type = "Material", sortBy = "expiration", sortDescending = false },
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
