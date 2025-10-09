using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Catalog.Dashboard;

/// <summary>
/// Dashboard tile showing summary of materials by inventory age.
/// Shows counts for: < 120 days, 120-250 days, > 250 days since last stock taking.
/// </summary>
public class MaterialInventorySummaryTile : ITile
{
    private readonly ICatalogRepository _catalogRepository;

    public MaterialInventorySummaryTile(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public string Title => "Materiály podle stáří inventury";
    public string Description => "Přehled materiálů podle doby od poslední inventury";
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

            var materials = catalogItems
                .Where(c => c.Type == ProductType.Material)
                .ToList();

            var recent = materials.Count(m => m.LastStockTaking.HasValue &&
                                             (now - m.LastStockTaking.Value).TotalDays < 120);

            var medium = materials.Count(m => m.LastStockTaking.HasValue &&
                                             (now - m.LastStockTaking.Value).TotalDays >= 120 &&
                                             (now - m.LastStockTaking.Value).TotalDays <= 250);

            var old = materials.Count(m => m.LastStockTaking.HasValue &&
                                          (now - m.LastStockTaking.Value).TotalDays > 250);

            var never = materials.Count(m => !m.LastStockTaking.HasValue);

            return new
            {
                status = "success",
                data = new
                {
                    recent,      // < 120 days
                    medium,      // 120-250 days
                    old,         // > 250 days
                    never,       // never inventoried
                    total = materials.Count(),
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
