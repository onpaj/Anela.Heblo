using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.DashboardTiles;

/// <summary>
/// Dashboard tile showing summary of products by inventory age.
/// Shows counts for: < 120 days, 120-250 days, > 250 days since last stock taking.
/// </summary>
public class ProductInventorySummaryTile : InventorySummaryTileBase
{
    public ProductInventorySummaryTile(ICatalogRepository catalogRepository)
        : base(catalogRepository)
    {
    }

    public override string Title => "Produkty podle stáří inventury";
    public override string Description => "Přehled produktů podle doby od poslední inventury";
    protected override Func<CatalogAggregate, bool> ItemFilter => c => c.Type == ProductType.Product || c.Type == ProductType.Goods;
}
