using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.DashboardTiles;

/// <summary>
/// Dashboard tile showing count of products inventoried in the last 30 days.
/// </summary>
public class ProductInventoryCountTile : InventoryCountTileBase
{
    public ProductInventoryCountTile(
        ICatalogRepository catalogRepository,
        TimeProvider timeProvider
        ) : base(catalogRepository, timeProvider)
    {
    }

    public override string Title => "Produkty inventarizované (30dní)";
    public override string Description => "Počet produktů inventarizovaných za posledních 30 dní";
    protected override ProductType TargetProductType => ProductType.Product;
}
