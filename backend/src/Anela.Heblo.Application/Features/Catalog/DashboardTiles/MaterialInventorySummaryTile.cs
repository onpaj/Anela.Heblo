using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.DashboardTiles;

/// <summary>
/// Dashboard tile showing summary of materials by inventory age.
/// Shows counts for: < 120 days, 120-250 days, > 250 days since last stock taking.
/// </summary>
public class MaterialInventorySummaryTile : InventorySummaryTileBase
{
    public MaterialInventorySummaryTile(ICatalogRepository catalogRepository)
        : base(catalogRepository)
    {
    }

    public override string Title => "Materiály podle stáří inventury";
    public override string Description => "Přehled materiálů podle doby od poslední inventury";
    protected override Func<CatalogAggregate, bool> ItemFilter => c => c.Type == ProductType.Material;

    protected override object GenerateDrillDownFilters()
    {
        return new { type = "Material" };
    }
}
